using System.Net;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.LinkAdder.Models;
using System.Net.Sockets;
using Jellyfin.Plugin.LinkAdder.Configuration;

namespace Jellyfin.Plugin.LinkAdder.Services;
public sealed partial class LinkAdderService
{
    private sealed record LinkCacheEntry(DateTime ExpiresUtc, Lazy<Task<IReadOnlyList<Uri>>> Loader);
    private sealed record IndexedTitle(string Name, string Url, string Source, RemoteRoot Root);
    private sealed record SearchIndexEntry(DateTime ExpiresUtc, Lazy<Task<IReadOnlyList<IndexedTitle>>> Loader);
    private static readonly ConcurrentDictionary<string, LinkCacheEntry> SearchLinkCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SearchIndexEntry> SearchIndexes = new(StringComparer.OrdinalIgnoreCase);
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
        var queryTokens=normalizedQuery.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var index=await GetSearchIndexAsync(mediaType,roots,ct).ConfigureAwait(false);
        var matches=index.Select(item=>new { Item=item, Score=SearchScore(item.Name,normalizedQuery,queryTokens) })
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
        var created=new List<string>(); var skipped=new List<string>();
        foreach(var item in preview.Files) { var parts=item.RelativePath.Split(Path.DirectorySeparatorChar,2); if(parts.Length!=2)throw new InvalidOperationException("Invalid planned path."); var root=Path.GetFullPath(outputRoot); var destination=Path.GetFullPath(Path.Combine(root,parts[1])); if(!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe output path rejected."); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); if(File.Exists(destination)){skipped.Add(destination);continue;} File.WriteAllText(destination,item.Url.Trim()+Environment.NewLine); created.Add(destination); }
        return new(created,skipped,"Library scan requested.");
    }

    private async Task<IReadOnlyList<IndexedTitle>> GetSearchIndexAsync(string mediaType,RemoteRoot[] roots,CancellationToken ct)
    {
        var signature=string.Join('|',roots.OrderBy(x=>x.Url.AbsoluteUri,StringComparer.OrdinalIgnoreCase).Select(x=>$"{x.Url.AbsoluteUri}\u001f{x.Username}"));
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
                var groups=await Task.WhenAll(roots.Select(root=>IndexRootWithTimeoutAsync(root,CancellationToken.None))).ConfigureAwait(false);
                return groups.SelectMany(x=>x).GroupBy(x=>x.Url,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).ToArray();
            },LazyThreadSafetyMode.ExecutionAndPublication));
            var installed=current is null?SearchIndexes.TryAdd(key,replacement):SearchIndexes.TryUpdate(key,replacement,current);
            if(!installed)continue;
            try{return await replacement.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
            catch when(!ct.IsCancellationRequested){SearchIndexes.TryRemove(new KeyValuePair<string,SearchIndexEntry>(key,replacement));throw;}
        }
    }

    private async Task<IReadOnlyList<IndexedTitle>> IndexRootWithTimeoutAsync(RemoteRoot root,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try{return await IndexRootAsync(root,timeout.Token).ConfigureAwait(false);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return [];}
    }

    private async Task<IReadOnlyList<IndexedTitle>> IndexRootAsync(RemoteRoot root,CancellationToken ct)
    {
        var results=new List<IndexedTitle>();
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages=new[]{root.Url};
        using var concurrency=new SemaphoreSlim(8,8);
        for(var depth=0;depth<=2&&pages.Length>0&&visited.Count<300;depth++)
        {
            var remaining=300-visited.Count;
            var current=pages.Where(page=>visited.Add(page.AbsoluteUri)).Take(remaining).ToArray();
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
                    if(!IsWithin(root.Url,link)||!link.AbsolutePath.EndsWith('/'))continue;
                    var name=WebUtility.UrlDecode(Path.GetFileName(link.AbsolutePath.TrimEnd('/')));
                    if(name is "" or "." or "..")continue;
                    var structural=IsStructuralFolder(name);
                    if(!structural)results.Add(new(name,link.AbsoluteUri,root.Name,root));
                    if(depth<2&&structural)next.Add(link);
                }
            }
            pages=next.DistinctBy(x=>x.AbsoluteUri,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return results;
    }

    private async Task<IReadOnlyList<RemoteSearchResult>> AddPostersAsync(IReadOnlyList<IndexedTitle> matches,CancellationToken ct)
    {
        using var concurrency=new SemaphoreSlim(8,8);
        var tasks=matches.Select(async (item,index)=>
        {
            if(index>=24)return new RemoteSearchResult(item.Name,item.Url,item.Source,string.Empty);
            await concurrency.WaitAsync(ct).ConfigureAwait(false);
            try{return new RemoteSearchResult(item.Name,item.Url,item.Source,await FindPosterAsync(new Uri(item.Url),item.Root,ct).ConfigureAwait(false));}
            finally{concurrency.Release();}
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static int SearchScore(string name,string normalizedQuery,IReadOnlyList<string> queryTokens)
    {
        var normalizedName=NormalizeSearchText(name);
        if(normalizedName.Equals(normalizedQuery,StringComparison.OrdinalIgnoreCase))return 1000;
        if(normalizedName.StartsWith(normalizedQuery+" ",StringComparison.OrdinalIgnoreCase))return 900;
        if(normalizedName.Contains(" "+normalizedQuery+" ",StringComparison.OrdinalIgnoreCase)||normalizedName.EndsWith(" "+normalizedQuery,StringComparison.OrdinalIgnoreCase))return 800;
        var nameTokens=normalizedName.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(queryTokens.Count>0&&queryTokens.All(nameTokens.Contains))return 600+queryTokens.Count;
        if(normalizedName.Contains(normalizedQuery,StringComparison.OrdinalIgnoreCase))return 400;
        return 0;
    }

    private static string NormalizeSearchText(string value)
    {
        var text=WebUtility.UrlDecode(value).ToLowerInvariant();
        text=SearchPunctuationRegex().Replace(text," ");
        return SearchSpaceRegex().Replace(text," ").Trim();
    }

    private async Task<string> FindPosterAsync(Uri folder,RemoteRoot root,CancellationToken ct)
    {
        try{var image=(await ListSearchLinksAsync(folder,root,ct).ConfigureAwait(false)).FirstOrDefault(link=>!link.AbsolutePath.StartsWith("/_h5ai/",StringComparison.OrdinalIgnoreCase)&&PosterRegex().IsMatch(link.AbsolutePath));return image?.Scheme is "http" or "https"?image.AbsoluteUri:string.Empty;}catch(WebException){return string.Empty;}catch(HttpRequestException){return string.Empty;}catch(TaskCanceledException) when(!ct.IsCancellationRequested){return string.Empty;}
    }

    private static bool IsStructuralFolder(string name)=>StructuralRegex().IsMatch(name)||name.Contains('—')||name.Contains("Before",StringComparison.OrdinalIgnoreCase)||name.Contains("Series",StringComparison.OrdinalIgnoreCase)&&name.Length<40;

    private RemoteRoot[] RemoteRoots(string mediaType)
    {
        if(mediaType is not("movie" or "series" or "anime"))throw new InvalidOperationException("Choose Movies, Shows, or Anime.");
        return _customSources.Where(x=>x.MediaType==mediaType||(x.MediaType=="both"&&mediaType is "movie" or "series")).Select(x=>new RemoteRoot(x.Name,new Uri(x.Url),x.Username,x.Password)).ToArray();
    }

    private async Task<IReadOnlyList<Uri>> CrawlVideosAsync(Uri root,CancellationToken ct)
    {
        var videos=new HashSet<Uri>(); var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var queue=new Queue<(Uri,int)>(); queue.Enqueue((root,0));
        var source=FindRoot(root);while(queue.Count>0) { if(visited.Count>=500) throw new InvalidOperationException("This folder is too large. Select a more specific movie or series folder."); var(page,depth)=queue.Dequeue(); if(!visited.Add(page.AbsoluteUri))continue; foreach(var link in await ListLinksAsync(page,source,ct).ConfigureAwait(false)){if(!IsWithin(root,link))continue;if(IsVideo(link))videos.Add(link);else if(depth<6&&link.AbsolutePath.EndsWith('/'))queue.Enqueue((link,depth+1));}}
        return videos.OrderBy(x=>x.AbsoluteUri,StringComparer.OrdinalIgnoreCase).ToArray();
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
        if(folder.Scheme!="ftp"){var body=await _http.GetStringAsync(folder,ct).ConfigureAwait(false);return ExtractLinks(folder,body).ToArray();}
#pragma warning disable SYSLIB0014
        var request=(FtpWebRequest)WebRequest.Create(folder);request.Method=WebRequestMethods.Ftp.ListDirectory;request.UsePassive=true;request.UseBinary=true;request.KeepAlive=false;if(root.Username.Length>0)request.Credentials=new NetworkCredential(root.Username,root.Password);else request.Credentials=new NetworkCredential("anonymous","anonymous@");
        using var response=(FtpWebResponse)await request.GetResponseAsync().WaitAsync(ct).ConfigureAwait(false);using var reader=new StreamReader(response.GetResponseStream());var links=new List<Uri>();while(await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } raw){var name=raw.Trim().Trim('/');if(name is "" or "." or "..")continue;name=Path.GetFileName(name.Replace('\\','/'));var isFile=VideoExtensions.Contains(Path.GetExtension(name))||PosterRegex().IsMatch(name);var escaped=Uri.EscapeDataString(name).Replace("%2F","/",StringComparison.OrdinalIgnoreCase);links.Add(new Uri(folder,escaped+(isFile?string.Empty:"/")));}return links;
#pragma warning restore SYSLIB0014
    }
    private async Task<IReadOnlyList<Uri>> ListSearchLinksAsync(Uri folder,RemoteRoot root,CancellationToken ct)
    {
        var key=$"{root.Username}\n{folder.AbsoluteUri}";
        while(true)
        {
            var now=DateTime.UtcNow;
            if(SearchLinkCache.TryGetValue(key,out var current)&&current.ExpiresUtc>now)
            {
                try{return await current.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
                catch when(!ct.IsCancellationRequested){SearchLinkCache.TryRemove(new KeyValuePair<string,LinkCacheEntry>(key,current));throw;}
            }
            var replacement=new LinkCacheEntry(now.Add(SearchCacheDuration),new Lazy<Task<IReadOnlyList<Uri>>>(()=>ListLinksAsync(folder,root,CancellationToken.None),LazyThreadSafetyMode.ExecutionAndPublication));
            var installed=current is null?SearchLinkCache.TryAdd(key,replacement):SearchLinkCache.TryUpdate(key,replacement,current);
            if(!installed)continue;
            try{return await replacement.Loader.Value.WaitAsync(ct).ConfigureAwait(false);}
            catch when(!ct.IsCancellationRequested){SearchLinkCache.TryRemove(new KeyValuePair<string,LinkCacheEntry>(key,replacement));throw;}
        }
    }
    private static bool IsPrivate(IPAddress address){if(IPAddress.IsLoopback(address))return true;if(address.AddressFamily!=AddressFamily.InterNetwork)return false;var b=address.GetAddressBytes();return b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168);}
    private static IEnumerable<Uri> ExtractLinks(Uri page,string body){foreach(Match m in HrefRegex().Matches(body)){var href=WebUtility.HtmlDecode(m.Groups[1].Value);if(Uri.TryCreate(page,href,out var link)&&(link.Scheme=="http"||link.Scheme=="https"))yield return new UriBuilder(link){Fragment=string.Empty}.Uri;}}
    private static bool IsWithin(Uri root,Uri value)=>value.Scheme==root.Scheme&&value.Host.Equals(root.Host,StringComparison.OrdinalIgnoreCase)&&value.Port==root.Port&&value.AbsolutePath.StartsWith(root.AbsolutePath.EndsWith('/')?root.AbsolutePath:root.AbsolutePath+'/',StringComparison.OrdinalIgnoreCase);
    private static bool IsVideo(Uri uri)=>VideoExtensions.Contains(Path.GetExtension(uri.AbsolutePath));
    private static IReadOnlyList<PlannedFile> MakePlan(IReadOnlyList<Uri> videos,string mediaType,string title)
    {
        if(mediaType is not("movie" or "series"))throw new InvalidOperationException("Choose Movie or Series.");var result=new List<PlannedFile>();foreach(var video in videos){var raw=Path.GetFileNameWithoutExtension(WebUtility.UrlDecode(video.AbsolutePath));if(mediaType=="movie"){var name=videos.Count==1?title:CleanName(raw);result.Add(new(video.AbsoluteUri,Path.Combine("Movies",title,name+".strm")));continue;}var match=EpisodeRegex().Match(raw);var season=1;string code;if(match.Success){season=int.Parse(match.Groups[1].Value);code=$"S{season:00}E{int.Parse(match.Groups[2].Value):00}"+(match.Groups[3].Success?$"-E{int.Parse(match.Groups[3].Value):00}":"");}else{var sm=SeasonRegex().Match(WebUtility.UrlDecode(video.AbsolutePath));if(sm.Success)season=int.Parse(sm.Groups[1].Value);code=CleanName(raw);}result.Add(new(video.AbsoluteUri,Path.Combine("Shows",title,$"Season {season:00}",$"{title} - {code}.strm")));}return result;
    }
    private static string InferTitle(Uri source,string mediaType){var path=WebUtility.UrlDecode(source.AbsolutePath).TrimEnd('/');var candidate=IsVideo(source)?Path.GetFileNameWithoutExtension(path):Path.GetFileName(path);if(mediaType=="series"&&candidate.StartsWith("Season",StringComparison.OrdinalIgnoreCase))candidate=Path.GetFileName(Path.GetDirectoryName(path)??path);candidate=CleanName(candidate??"Untitled");var m=EpisodeRegex().Match(candidate);return mediaType=="series"&&m.Success?candidate[..m.Index].Trim(' ','.','-','_'):candidate;}
    private static string CleanName(string value){var x=DotRegex().Replace(WebUtility.UrlDecode(value)," ");x=QualityRegex().Replace(x,"");x=InvalidRegex().Replace(x," ");x=SpaceRegex().Replace(x," ").Trim(' ','.','-');return string.IsNullOrWhiteSpace(x)?"Untitled":x;}
    [GeneratedRegex("href\\s*=\\s*[\\\"']([^\\\"']+)[\\\"']",RegexOptions.IgnoreCase)]private static partial Regex HrefRegex();
    [GeneratedRegex("(?:^|[. _-])S(\\d{1,2})[. _-]*E(\\d{1,3})(?:[. _-]*E?(\\d{1,3}))?",RegexOptions.IgnoreCase)]private static partial Regex EpisodeRegex();
    [GeneratedRegex("/Season[ ._-]*(\\d{1,2})/",RegexOptions.IgnoreCase)]private static partial Regex SeasonRegex();
    [GeneratedRegex("[._]+") ]private static partial Regex DotRegex();
    [GeneratedRegex("\\b(2160p|1080p|720p|480p|web[- ]?dl|webrip|bluray|brrip|hdrip|dvdrip|x26[45]|hevc|avc|aac|esub|msubs?).*$",RegexOptions.IgnoreCase)]private static partial Regex QualityRegex();
    [GeneratedRegex("""[<>:"/\\|?*]""")]private static partial Regex InvalidRegex();
    [GeneratedRegex("\\s+")]private static partial Regex SpaceRegex();
    [GeneratedRegex("^(?:\\(?\\d{4}\\)?|\\d{4}\\s*&|TV[- ]?Series|Anime[- ]?TV)",RegexOptions.IgnoreCase)]private static partial Regex StructuralRegex();
    [GeneratedRegex("\\.(?:jpe?g|png|webp)$",RegexOptions.IgnoreCase)]private static partial Regex PosterRegex();
    [GeneratedRegex(@"[^\p{L}\p{N}]+") ]private static partial Regex SearchPunctuationRegex();
    [GeneratedRegex(@"\s+") ]private static partial Regex SearchSpaceRegex();
}
