using System.Net;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.LinkAdder.Models;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.LinkAdder.Configuration;

namespace Jellyfin.Plugin.LinkAdder.Services;
public sealed partial class LinkAdderService
{
    private sealed record LinkCacheEntry(DateTime ExpiresUtc, Lazy<Task<IReadOnlyList<Uri>>> Loader);
    private sealed record IndexedTitle(string Name, string SearchName, string Url, string Source, RemoteRoot Root);
    private sealed record SearchIndexEntry(DateTime ExpiresUtc, Lazy<Task<IReadOnlyList<IndexedTitle>>> Loader);
    private static readonly ConcurrentDictionary<(RemoteRoot Root, string Url), LinkCacheEntry> SearchLinkCache = new();
    private static readonly ConcurrentDictionary<string, SearchIndexEntry> SearchIndexes = new(StringComparer.Ordinal);
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchIndexDuration = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".webm", ".wmv", ".mpg", ".mpeg" };
    private readonly HttpClient _http;
    private readonly CustomSource[] _customSources;
    private sealed record RemoteRoot(string Name, Uri Url, string Username = "", string Password = "");
    public LinkAdderService(HttpClient http, IEnumerable<CustomSource>? customSources = null) { _http=http; _http.Timeout=TimeSpan.FromSeconds(20); _http.DefaultRequestHeaders.UserAgent.ParseAdd("Local-ISP-FTP-Loader/2.3"); _customSources=(customSources??[]).ToArray(); }

    public async Task<IReadOnlyList<RemoteSearchResult>> SearchRemoteAsync(string mediaType,string query,CancellationToken ct,bool includePosters=true)
    {
        query=(query??string.Empty).Trim();
        if(query.Length<2)throw new InvalidOperationException("Enter at least 2 characters.");
        var roots=RemoteRoots(mediaType);
        if(roots.Length==0)throw new InvalidOperationException("Add a local ISP media source for this media type first.");
        var normalizedQuery=NormalizeSearchText(query);
        if (normalizedQuery.Length == 0) return [];
        var queryTokens=normalizedQuery.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var index=await GetSearchIndexAsync(mediaType,roots,ct).ConfigureAwait(false);
        var matches=index.Select(item=>new { Item=item, Score=SearchScore(item.SearchName,normalizedQuery,queryTokens) })
            .Where(x=>x.Score>0)
            .OrderByDescending(x=>x.Score)
            .ThenBy(x=>x.Item.Name,StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .Select(x=>x.Item)
            .ToArray();
        if(!includePosters)return matches.Select(item=>new RemoteSearchResult(item.Name,item.Url,item.Source,string.Empty)).ToArray();
        return await AddPostersAsync(matches,ct).ConfigureAwait(false);
    }

    public async Task WarmSearchIndexesAsync(CancellationToken ct)
    {
        var warmups=new[]{"movie","series","anime"}.Select(async mediaType=>
        {
            var roots=RemoteRoots(mediaType);
            if(roots.Length==0)return;
            try{await GetSearchIndexAsync(mediaType,roots,ct).ConfigureAwait(false);}
            catch when(!ct.IsCancellationRequested){}
        });
        await Task.WhenAll(warmups).ConfigureAwait(false);
    }

    public async Task<int> TestConnectionAsync(string sourceUrl,string? username,string? password,CancellationToken ct)
    {
        if(!Uri.TryCreate(sourceUrl?.Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https" or "ftp"))throw new InvalidOperationException("Use a complete HTTP, HTTPS, or FTP URL.");
        var root=new RemoteRoot("Connection test",uri.AbsolutePath.EndsWith('/')?uri:new Uri(uri.AbsoluteUri+"/"),(username??string.Empty).Trim(),password??string.Empty);
        return (await ListLinksAsync(root.Url,root,ct).ConfigureAwait(false)).Count;
    }
    public async Task<PreviewResult> PreviewAsync(string sourceUrl,string mediaType,string? requestedTitle,CancellationToken ct)
    {
        if(mediaType=="anime")mediaType="series";
        var source=ValidateConfiguredSource(sourceUrl); IReadOnlyList<Uri> videos=IsVideo(source)?[source]:await CrawlVideosAsync(source,ct).ConfigureAwait(false); if(videos.Count==0) throw new InvalidOperationException("No supported video files were found."); var title=string.IsNullOrWhiteSpace(requestedTitle)?InferTitle(source,mediaType):CleanName(requestedTitle); return new(title,MakePlan(videos,mediaType,title));
    }

    public AddResult Write(PreviewResult preview,string outputRoot)
    {
        var created = new List<string>();
        var skipped = new List<string>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        foreach (var item in preview.Files)
        {
            var parts = item.RelativePath.Split(Path.DirectorySeparatorChar, 2);
            if (parts.Length != 2) throw new InvalidOperationException("Invalid planned path.");
            var destination = Path.GetFullPath(Path.Combine(root, parts[1]));
            if (!destination.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe output path rejected.");
            if (File.Exists(destination))
            {
                skipped.Add(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllText(temporary, item.Url + Environment.NewLine);
                File.Move(temporary, destination);
                created.Add(destination);
            }
            catch (IOException) when (File.Exists(destination))
            {
                skipped.Add(destination);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        return new(created,skipped,"Library scan requested.")
        {
            Title = preview.Title,
            TitlePath = Path.Combine(root, preview.Title)
        };
    }

    public async Task<AddResult> DownloadAsync(PreviewResult preview,string outputRoot,CancellationToken ct,Action<double,string>? progress=null)
    {
        var created=new List<string>(); var skipped=new List<string>();
        for(var index=0;index<preview.Files.Count;index++)
        {
            var item=preview.Files[index];
            ct.ThrowIfCancellationRequested();
            var source=ValidateConfiguredSource(item.Url);
            if(!IsVideo(source))throw new InvalidOperationException("The selected source contains an unsupported video file.");
            var parts=item.RelativePath.Split(Path.DirectorySeparatorChar,2);
            if(parts.Length!=2)throw new InvalidOperationException("Invalid planned path.");
            var root=Path.GetFullPath(outputRoot);
            var relative=Path.ChangeExtension(parts[1],Path.GetExtension(WebUtility.UrlDecode(source.AbsolutePath)));
            var destination=Path.GetFullPath(Path.Combine(root,relative));
            if(!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe output path rejected.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if(File.Exists(destination)){skipped.Add(destination);progress?.Invoke(100d*(index+1)/preview.Files.Count,$"Skipped existing file {index+1} of {preview.Files.Count}.");continue;}
            var temporary=destination+"."+Guid.NewGuid().ToString("N")+".part";
            try
            {
                var fileNumber=index+1;
                await DownloadFileAsync(source,temporary,ct,(fraction,fileName)=>progress?.Invoke(100d*(index+fraction)/preview.Files.Count,$"Downloading {fileNumber} of {preview.Files.Count}: {fileName}")).ConfigureAwait(false);
                File.Move(temporary,destination);
                created.Add(destination);
                progress?.Invoke(100d*(index+1)/preview.Files.Count,$"Downloaded {fileNumber} of {preview.Files.Count}.");
            }
            finally
            {
                if(File.Exists(temporary))File.Delete(temporary);
            }
        }
        return new(created,skipped,"Library scan requested.");
    }

    private async Task DownloadFileAsync(Uri source,string destination,CancellationToken ct,Action<double,string>? progress)
    {
        var root=FindRoot(source);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*128,useAsync:true);
        if(source.Scheme=="ftp")
        {
#pragma warning disable SYSLIB0014
            var request=(FtpWebRequest)WebRequest.Create(source);request.Method=WebRequestMethods.Ftp.DownloadFile;request.UsePassive=true;request.UseBinary=true;request.KeepAlive=false;
            request.Credentials=root.Username.Length>0?new NetworkCredential(root.Username,root.Password):new NetworkCredential("anonymous","anonymous@");
            using var registration=ct.Register(request.Abort);
            using var response=(FtpWebResponse)await request.GetResponseAsync().WaitAsync(ct).ConfigureAwait(false);
            await CopyWithProgressAsync(response.GetResponseStream(),output,response.ContentLength,Path.GetFileName(WebUtility.UrlDecode(source.AbsolutePath)),ct,progress).ConfigureAwait(false);
#pragma warning restore SYSLIB0014
            return;
        }
        using var handler=new HttpClientHandler();
        if(root.Username.Length>0)handler.Credentials=new NetworkCredential(root.Username,root.Password);
        using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Local-ISP-FTP-Loader/2.6");
        using var responseMessage=await client.GetAsync(source,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();
        await using var input=await responseMessage.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await CopyWithProgressAsync(input,output,responseMessage.Content.Headers.ContentLength??-1,Path.GetFileName(WebUtility.UrlDecode(source.AbsolutePath)),ct,progress).ConfigureAwait(false);
    }

    private static async Task CopyWithProgressAsync(Stream input,Stream output,long length,string fileName,CancellationToken ct,Action<double,string>? progress)
    {
        var buffer=new byte[1024*128];long copied=0;int read;
        while((read=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
        {
            await output.WriteAsync(buffer.AsMemory(0,read),ct).ConfigureAwait(false);copied+=read;
            progress?.Invoke(length>0?Math.Clamp((double)copied/length,0,1):0,fileName);
        }
        progress?.Invoke(1,fileName);
    }

    private async Task<IReadOnlyList<IndexedTitle>> GetSearchIndexAsync(string mediaType,RemoteRoot[] roots,CancellationToken ct)
    {
        var signature = Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(roots)));
        var key=$"{mediaType}\u001e{signature}";
        while(true)
        {
            var now=DateTime.UtcNow;
            if(SearchIndexes.TryGetValue(key,out var current)&&current.ExpiresUtc>now)
            {
                try{return await current.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
                catch when(!ct.IsCancellationRequested){SearchIndexes.TryRemove(new KeyValuePair<string,SearchIndexEntry>(key,current));throw;}
            }
            var replacement=new SearchIndexEntry(now.Add(SearchIndexDuration),new Lazy<Task<IReadOnlyList<IndexedTitle>>>(async()=>
            {
                var groups=await Task.WhenAll(roots.Select(root=>IndexRootWithTimeoutAsync(root,mediaType,CancellationToken.None))).ConfigureAwait(false);
                return groups.SelectMany(x=>x).DistinctBy(x=>x.Url,StringComparer.Ordinal).ToArray();
            },LazyThreadSafetyMode.ExecutionAndPublication));
            var installed=current is null?SearchIndexes.TryAdd(key,replacement):SearchIndexes.TryUpdate(key,replacement,current);
            if(!installed)continue;
            try{return await replacement.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
            catch when(!ct.IsCancellationRequested){SearchIndexes.TryRemove(new KeyValuePair<string,SearchIndexEntry>(key,replacement));throw;}
        }
    }

    private async Task<IReadOnlyList<IndexedTitle>> IndexRootWithTimeoutAsync(RemoteRoot root,string mediaType,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try{return await IndexRootAsync(root,mediaType,timeout.Token).ConfigureAwait(false);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return [];}
    }

    private async Task<IReadOnlyList<IndexedTitle>> IndexRootAsync(RemoteRoot root,string mediaType,CancellationToken ct)
    {
        var results=new List<IndexedTitle>();
        var visited=new HashSet<string>(StringComparer.Ordinal);
        var pages=new[]{root.Url};
        using var concurrency=new SemaphoreSlim(8,8);
        for(var depth=0;depth<=2&&pages.Length>0&&visited.Count<300;depth++)
        {
            var remaining=300-visited.Count;
            var current=pages.Where(page=>!visited.Contains(page.AbsoluteUri)).Take(remaining).ToArray();
            foreach (var page in current) visited.Add(page.AbsoluteUri);
            var loads=current.Select(async page=>
            {
                await concurrency.WaitAsync(ct).ConfigureAwait(false);
                try{return (Page:page,Links:await ListSearchLinksAsync(page,root,ct).ConfigureAwait(false));}
                catch(Exception ex) when(ex is WebException or HttpRequestException or TaskCanceledException&&!ct.IsCancellationRequested){return (Page:page,Links:(IReadOnlyList<Uri>)[]);}
                finally{concurrency.Release();}
            });
            var loaded=await Task.WhenAll(loads).ConfigureAwait(false);
            var next=new List<Uri>();
            foreach(var group in loaded)
            {
                foreach(var link in group.Links)
                {
                    if (!IsWithin(root.Url, link)) continue;
                    var name = Uri.UnescapeDataString(Path.GetFileName(link.AbsolutePath.TrimEnd('/')));
                    if (name is "" or "." or ".." || ExtraRegex().IsMatch(name)) continue;
                    if (IsVideo(link))
                    {
                        var fileTitle = InferTitle(link, mediaType == "movie" ? "movie" : "series");
                        results.Add(new(fileTitle, NormalizeSearchText(fileTitle), link.AbsoluteUri, root.Name, root));
                        continue;
                    }
                    if (!link.AbsolutePath.EndsWith('/')) continue;
                    var structural=IsStructuralFolder(name);
                    if(!structural)results.Add(new(name,NormalizeSearchText(CleanName(name)),link.AbsoluteUri,root.Name,root));
                    if(depth<2&&structural)next.Add(link);
                }
            }
            pages=next.DistinctBy(x=>x.AbsoluteUri,StringComparer.Ordinal).ToArray();
        }
        return results;
    }

    private async Task<IReadOnlyList<RemoteSearchResult>> AddPostersAsync(IReadOnlyList<IndexedTitle> matches,CancellationToken ct)
    {
        using var concurrency=new SemaphoreSlim(8,8);
        var tasks=matches.Select(async (item,index)=>
        {
            if(index>=24 || IsVideo(new Uri(item.Url)))return new RemoteSearchResult(item.Name,item.Url,item.Source,string.Empty);
            await concurrency.WaitAsync(ct).ConfigureAwait(false);
            try{return new RemoteSearchResult(item.Name,item.Url,item.Source,await FindPosterAsync(new Uri(item.Url),item.Root,ct).ConfigureAwait(false));}
            finally{concurrency.Release();}
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static int SearchScore(string normalizedName,string normalizedQuery,IReadOnlyList<string> queryTokens)
    {
        if(normalizedName.Equals(normalizedQuery,StringComparison.OrdinalIgnoreCase))return 1000;
        if(normalizedName.StartsWith(normalizedQuery+" ",StringComparison.OrdinalIgnoreCase))return 900;
        if(normalizedName.Contains(" "+normalizedQuery+" ",StringComparison.OrdinalIgnoreCase)||normalizedName.EndsWith(" "+normalizedQuery,StringComparison.OrdinalIgnoreCase))return 800;
        var nameTokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.All(token => nameTokens.Contains(token))) return 600;
        if (queryTokens.All(token => nameTokens.Any(word => word == token || (token.Length >= 2 && word.StartsWith(token, StringComparison.Ordinal))))) return 500;
        if(normalizedName.Contains(normalizedQuery,StringComparison.OrdinalIgnoreCase))return 400;
        return 0;
    }

    private static string NormalizeSearchText(string value)
    {
        var text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        text=SearchPunctuationRegex().Replace(text," ");
        return SearchSpaceRegex().Replace(text," ").Trim();
    }

    private async Task<string> FindPosterAsync(Uri folder,RemoteRoot root,CancellationToken ct)
    {
        try{var image=(await ListSearchLinksAsync(folder,root,ct).ConfigureAwait(false)).FirstOrDefault(link=>!link.AbsolutePath.StartsWith("/_h5ai/",StringComparison.OrdinalIgnoreCase)&&PosterRegex().IsMatch(link.AbsolutePath));return image?.Scheme is "http" or "https"?image.AbsoluteUri:string.Empty;}catch(WebException){return string.Empty;}catch(HttpRequestException){return string.Empty;}catch(TaskCanceledException) when(!ct.IsCancellationRequested){return string.Empty;}
    }

    private static bool IsStructuralFolder(string name) => StructuralRegex().IsMatch(name);

    private RemoteRoot[] RemoteRoots(string mediaType)
    {
        if(mediaType is not("movie" or "series" or "anime"))throw new InvalidOperationException("Choose Movies, Shows, or Anime.");
        return _customSources.Where(x=>x.MediaType==mediaType||(x.MediaType=="both"&&mediaType is "movie" or "series")).Select(x=>new RemoteRoot(x.Name,new Uri(x.Url),x.Username,x.Password)).ToArray();
    }

    private async Task<IReadOnlyList<Uri>> CrawlVideosAsync(Uri root,CancellationToken ct)
    {
        var videos = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(Uri Page, int Depth)>();
        var source = FindRoot(root);
        queue.Enqueue((root, 0));
        while (queue.TryDequeue(out var entry))
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(entry.Page.AbsoluteUri)) continue;
            if (visited.Count > 500) throw new InvalidOperationException("This folder is too large. Select a more specific movie or series folder.");
            IReadOnlyList<Uri> links;
            try
            {
                links = await ListSearchLinksAsync(entry.Page, source, ct).ConfigureAwait(false);
            }
            catch (WebException ex) when (entry.Depth > 0 && ex.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable })
            {
                // FTP name-only listings can report sidecar files as potential folders.
                ex.Response?.Dispose();
                continue;
            }
            foreach (var link in links)
            {
                if (!IsWithin(root, link)) continue;
                var name = Uri.UnescapeDataString(Path.GetFileName(link.AbsolutePath.TrimEnd('/')));
                if (ExtraRegex().IsMatch(name)) continue;
                if (IsVideo(link)) videos.Add(link.AbsoluteUri);
                else if (entry.Depth < 6 && link.AbsolutePath.EndsWith('/')) queue.Enqueue((link, entry.Depth + 1));
            }
        }
        return videos.Order(StringComparer.Ordinal).Select(url => new Uri(url)).ToArray();
    }

    public Uri ValidateConfiguredSource(string value)
    {
        if(!Uri.TryCreate(value?.Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https" or "ftp")) throw new InvalidOperationException("Use a complete HTTP, HTTPS, or FTP link.");
        if(!IPAddress.TryParse(uri.Host,out var address) || !IsPrivate(address)) throw new InvalidOperationException("Only local-server IP links are allowed.");
        if(!RemoteRoots("movie").Concat(RemoteRoots("series")).Concat(RemoteRoots("anime")).Any(x=>IsWithin(x.Url,uri)||x.Url.AbsoluteUri.Equals(uri.AbsoluteUri,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The link is not inside a configured media source.");return uri;
    }

    private RemoteRoot FindRoot(Uri value)=>RemoteRoots("movie").Concat(RemoteRoots("series")).Concat(RemoteRoots("anime")).OrderByDescending(x=>x.Url.AbsolutePath.Length).First(x=>IsWithin(x.Url,value)||x.Url.AbsoluteUri.Equals(value.AbsoluteUri,StringComparison.OrdinalIgnoreCase));
    private async Task<IReadOnlyList<Uri>> ListLinksAsync(Uri folder,RemoteRoot root,CancellationToken ct)
    {
        if (folder.Scheme != "ftp")
        {
            if (root.Username.Length == 0)
                return ExtractLinks(folder, await _http.GetStringAsync(folder, ct).ConfigureAwait(false)).ToArray();
            using var handler = new HttpClientHandler { Credentials = new NetworkCredential(root.Username, root.Password) };
            using var client = new HttpClient(handler) { Timeout = _http.Timeout };
            return ExtractLinks(folder, await client.GetStringAsync(folder, ct).ConfigureAwait(false)).ToArray();
        }
#pragma warning disable SYSLIB0014
        var request=(FtpWebRequest)WebRequest.Create(folder);request.Method=WebRequestMethods.Ftp.ListDirectory;request.UsePassive=true;request.UseBinary=true;request.KeepAlive=false;if(root.Username.Length>0)request.Credentials=new NetworkCredential(root.Username,root.Password);else request.Credentials=new NetworkCredential("anonymous","anonymous@");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_http.Timeout);
        using var registration = timeout.Token.Register(request.Abort);
        using var response = (FtpWebResponse)await request.GetResponseAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(response.GetResponseStream());
        var links = new List<Uri>();
        while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } raw)
        {
            var name = Path.GetFileName(raw.TrimEnd('/'));
            if (name is "" or "." or "..") continue;
            var extension = Path.GetExtension(name);
            if (SidecarExtensions.Contains(extension)) continue;
            var isFile = VideoExtensions.Contains(extension) || PosterRegex().IsMatch(name);
            links.Add(new Uri(folder, Uri.EscapeDataString(name) + (isFile ? string.Empty : "/")));
        }
        return links;
#pragma warning restore SYSLIB0014
    }
    private async Task<IReadOnlyList<Uri>> ListSearchLinksAsync(Uri folder,RemoteRoot root,CancellationToken ct)
    {
        var key = (root, folder.AbsoluteUri);
        while(true)
        {
            var now=DateTime.UtcNow;
            if(SearchLinkCache.TryGetValue(key,out var current)&&current.ExpiresUtc>now)
            {
                try{return await current.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
                catch when(!ct.IsCancellationRequested){SearchLinkCache.TryRemove(new KeyValuePair<(RemoteRoot, string),LinkCacheEntry>(key,current));throw;}
            }
            var replacement=new LinkCacheEntry(now.Add(SearchCacheDuration),new Lazy<Task<IReadOnlyList<Uri>>>(()=>ListLinksAsync(folder,root,CancellationToken.None),LazyThreadSafetyMode.ExecutionAndPublication));
            var installed=current is null?SearchLinkCache.TryAdd(key,replacement):SearchLinkCache.TryUpdate(key,replacement,current);
            if(!installed)continue;
            try{return await replacement.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
            catch when(!ct.IsCancellationRequested){SearchLinkCache.TryRemove(new KeyValuePair<(RemoteRoot, string),LinkCacheEntry>(key,replacement));throw;}
        }
    }
    private static bool IsPrivate(IPAddress address){if(IPAddress.IsLoopback(address))return true;if(address.AddressFamily!=AddressFamily.InterNetwork)return false;var b=address.GetAddressBytes();return b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168);}
    private static IEnumerable<Uri> ExtractLinks(Uri page,string body){foreach(Match m in HrefRegex().Matches(body)){var href=WebUtility.HtmlDecode(m.Groups[1].Value);if(Uri.TryCreate(page,href,out var link)&&(link.Scheme=="http"||link.Scheme=="https"))yield return new UriBuilder(link){Fragment=string.Empty}.Uri;}}
    private static bool IsWithin(Uri root,Uri value)=>value.Scheme==root.Scheme&&value.Host.Equals(root.Host,StringComparison.OrdinalIgnoreCase)&&value.Port==root.Port&&value.AbsolutePath.StartsWith(root.AbsolutePath.EndsWith('/')?root.AbsolutePath:root.AbsolutePath+'/',StringComparison.Ordinal);
    private static bool IsVideo(Uri uri)=>!uri.AbsolutePath.EndsWith('/') && VideoExtensions.Contains(Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath)));
    private static IReadOnlyList<PlannedFile> MakePlan(IReadOnlyList<Uri> videos,string mediaType,string title)
    {
        if (mediaType is not ("movie" or "series")) throw new InvalidOperationException("Choose Movie or Series.");
        var result = new List<PlannedFile>();
        foreach (var video in videos)
        {
            var path = Uri.UnescapeDataString(video.AbsolutePath);
            var raw = Path.GetFileNameWithoutExtension(path);
            if (mediaType == "movie")
            {
                var name = videos.Count == 1 ? title : CleanName(raw);
                result.Add(new(video.AbsoluteUri, Path.Combine("Movies", title, name + ".strm")));
                continue;
            }

            var seasonFolder = SeasonRegex().Matches(path).LastOrDefault();
            var season = seasonFolder is null ? 1 : int.Parse(seasonFolder.Groups[1].Value);
            var match = EpisodeRegex().Match(raw);
            var code = CleanName(raw);
            if (match.Success)
            {
                season = int.Parse(match.Groups[1].Value);
                code = $"S{season:00}E{int.Parse(match.Groups[2].Value):00}";
                if (match.Groups[3].Success) code += $"-E{int.Parse(match.Groups[3].Value):00}";
            }
            else
            {
                var numbered = NumberedEpisodeRegex().Match(code);
                if (numbered.Success) code = $"S{season:00}E{int.Parse(numbered.Groups[1].Value):00}";
            }
            result.Add(new(video.AbsoluteUri, Path.Combine("Shows", title, $"Season {season:00}", $"{title} - {code}.strm")));
        }
        return result.DistinctBy(file => file.RelativePath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
    }
    private static string InferTitle(Uri source,string mediaType){var path=Uri.UnescapeDataString(source.AbsolutePath).TrimEnd('/');var candidate=IsVideo(source)?Path.GetFileNameWithoutExtension(path):Path.GetFileName(path);if(mediaType=="series"&&candidate.StartsWith("Season",StringComparison.OrdinalIgnoreCase))candidate=Path.GetFileName(Path.GetDirectoryName(path)??path);candidate=CleanName(candidate??"Untitled");var m=EpisodeRegex().Match(candidate);return mediaType=="series"&&m.Success&&m.Index>0?CleanName(candidate[..m.Index]):candidate;}
    private static string CleanName(string value){var x=DotRegex().Replace(value," ");x=QualityRegex().Replace(x,"");x=InvalidRegex().Replace(x," ");x=SpaceRegex().Replace(x," ").Trim(' ','.','-');return string.IsNullOrWhiteSpace(x)?"Untitled":x;}
    [GeneratedRegex("href\\s*=\\s*[\\\"']([^\\\"']+)[\\\"']",RegexOptions.IgnoreCase)]private static partial Regex HrefRegex();
    [GeneratedRegex(@"(?:^|[. _-])S?(\d{1,2})(?:[. _-]*E|x)(\d{1,3})(?:(?:[. _-]*E|\s*-\s*)(\d{1,3})(?!\d))?(?!\d)",RegexOptions.IgnoreCase)]private static partial Regex EpisodeRegex();
    [GeneratedRegex(@"/(?:Season[ ._-]*|S)(\d{1,2})(?=/)",RegexOptions.IgnoreCase)]private static partial Regex SeasonRegex();
    [GeneratedRegex(@"(?:^|\s+-\s+|\bEP(?:ISODE)?[ ._-]*)(\d{1,3})(?:v\d+)?(?=\s*(?:[\[(]|$))",RegexOptions.IgnoreCase)]private static partial Regex NumberedEpisodeRegex();
    [GeneratedRegex("[._]+") ]private static partial Regex DotRegex();
    [GeneratedRegex("\\b(2160p|1080p|720p|480p|web[- ]?dl|webrip|bluray|brrip|hdrip|dvdrip|x26[45]|hevc|avc|aac|esub|msubs?).*$",RegexOptions.IgnoreCase)]private static partial Regex QualityRegex();
    [GeneratedRegex("""[<>:"/\\|?*\x00-\x1f]""")]private static partial Regex InvalidRegex();
    [GeneratedRegex("\\s+")]private static partial Regex SpaceRegex();
    private static readonly HashSet<string> SidecarExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".nfo", ".srt", ".ass", ".ssa", ".sub", ".vtt", ".txt", ".sfv", ".xml", ".json" };
    [GeneratedRegex(@"^(?:\(?\d{4}\)?(?:\s*(?:[-–—&/,]|to)\s*\d{4})*|\d{4}\s+and\s+(?:before|earlier|older)|[a-z]\s*[-–]\s*[a-z]|(?:tv[- ]?series|anime(?:[- ]?tv)?|movies|shows))$", RegexOptions.IgnoreCase)]
    private static partial Regex StructuralRegex();
    [GeneratedRegex(@"^(?:samples?|trailers?|featurettes)(?:\.[^.]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtraRegex();
    [GeneratedRegex("\\.(?:jpe?g|png|webp)$",RegexOptions.IgnoreCase)]private static partial Regex PosterRegex();
    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}]+") ]private static partial Regex SearchPunctuationRegex();
    [GeneratedRegex(@"\s+") ]private static partial Regex SearchSpaceRegex();
}
