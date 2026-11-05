using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.LinkAdder.Configuration;
using Jellyfin.Plugin.LinkAdder.Models;
using Jellyfin.Plugin.LinkAdder.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LinkAdder.Api;

[ApiController]
[Authorize]
[Route("Plugins/LinkAdder")]
public sealed class LinkAdderController : ControllerBase
{
    private static readonly object ConfigurationLock = new();
    private readonly LinkAdderService _service;
    private readonly ILibraryManager _library;

    public LinkAdderController(IHttpClientFactory clients, ILibraryManager library)
    {
        _service = new LinkAdderService(clients.CreateClient(), Plugin.Instance?.Configuration.CustomSources ?? []);
        _library = library;
    }

    [HttpGet("CanManage")]
    public ActionResult CanManage() => Ok(new { canManage = true });

    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibraryOption>> Libraries() => Ok(LibrariesForValidation().OrderBy(x => x.Name).ToArray());

    [HttpPost("Remote/Search")]
    public async Task<ActionResult<IReadOnlyList<RemoteSearchResult>>> SearchRemote([FromBody] RemoteSearchRequest request, CancellationToken ct) =>
        Ok(await _service.SearchRemoteAsync(request.MediaType, request.Query, ct).ConfigureAwait(false));

    [HttpPost("Preview")]
    public async Task<ActionResult<PreviewResult>> Preview([FromBody] PreviewRequest request, CancellationToken ct) =>
        Ok(await _service.PreviewAsync(request.Url, request.MediaType, request.Title, ct).ConfigureAwait(false));

    [HttpPost("Add")]
    public async Task<ActionResult<AddResult>> Add([FromBody] AddRequest request, CancellationToken ct)
    {
        var mediaType = NormalizeMediaType(request.MediaType);
        var requestedPath = string.IsNullOrWhiteSpace(request.LibraryPath) ? string.Empty : Path.GetFullPath(request.LibraryPath);
        var option = LibrariesForValidation().FirstOrDefault(x => x.MediaType == mediaType && x.Path.Equals(requestedPath, StringComparison.OrdinalIgnoreCase));
        if (option is null) return BadRequest(new { error = "Select a compatible Jellyfin library." });
        var preview = await _service.PreviewAsync(request.Url, request.MediaType, request.Title, ct).ConfigureAwait(false);
        var result = _service.Write(preview, option.Path);
        _ = Task.Run(() => _library.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None));
        return Ok(result with { Refresh = "Jellyfin library scan started." });
    }

    [HttpPost("Sources/Test")]
    public async Task<ActionResult<TestSourceResult>> TestSource([FromBody] TestSourceRequest request, CancellationToken ct)
    {
        ValidateLocalSource(request.Url);
        try
        {
            var entries = await _service.TestConnectionAsync(request.Url, request.Username, request.Password, ct).ConfigureAwait(false);
            return Ok(new TestSourceResult(true, entries, $"Connection successful. Found {entries} entries."));
        }
        catch (Exception ex) when (ex is HttpRequestException or WebException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return BadRequest(new { error = $"Connection failed: {ex.Message}" });
        }
    }

    [HttpGet("Sources")]
    public ActionResult<IReadOnlyList<CustomSourceView>> Sources()
    {
        var items = (Plugin.Instance?.Configuration.CustomSources ?? [])
            .Select(x => new CustomSourceView(x.Id, x.Name, x.Url, x.MediaType, !string.IsNullOrEmpty(x.Username)))
            .OrderBy(x => x.Name)
            .ToArray();
        return Ok(items);
    }

    [HttpPost("Sources")]
    public ActionResult<CustomSourceView> AddSource([FromBody] CustomSourceRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var mediaType = NormalizeMediaType(request.MediaType);
        var uri = ValidateLocalSource(request.Url);
        if (name.Length < 2) return BadRequest(new { error = "Enter a source name." });
        var source = new CustomSource
        {
            Name = name,
            Url = EnsureDirectoryUrl(uri).AbsoluteUri,
            MediaType = mediaType,
            Username = (request.Username ?? string.Empty).Trim(),
            Password = request.Password ?? string.Empty
        };
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            if (configuration.CustomSources.Any(x => x.Url.Equals(source.Url, StringComparison.OrdinalIgnoreCase) && x.MediaType == source.MediaType))
                return Conflict(new { error = "That source is already configured for this media type." });
            configuration.CustomSources.Add(source);
            Plugin.Instance!.UpdateConfiguration(configuration);
        }
        return Ok(new CustomSourceView(source.Id, source.Name, source.Url, source.MediaType, source.Username.Length > 0));
    }

    [HttpDelete("Sources/{id}")]
    public ActionResult DeleteSource(string id)
    {
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            var removed = configuration.CustomSources.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return NotFound();
            Plugin.Instance!.UpdateConfiguration(configuration);
        }
        return NoContent();
    }

    [HttpPost("Requests")]
    public ActionResult<MediaRequestView> SubmitRequest([FromBody] SubmitMediaRequest request)
    {
        var source = _service.ValidateConfiguredSource(request.Url);
        var mediaType = NormalizeMediaType(request.MediaType);
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled" : request.Title.Trim();
        var item = new MediaAddRequest
        {
            RequestedBy = User.Identity?.Name ?? "Jellyfin user",
            Title = title,
            Url = source.AbsoluteUri,
            MediaType = mediaType
        };
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            if (configuration.MediaRequests.Any(x => x.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase) && x.MediaType == item.MediaType))
                return Conflict(new { error = "This title is already requested." });
            configuration.MediaRequests.Add(item);
            Plugin.Instance!.UpdateConfiguration(configuration);
        }
        return Ok(ToView(item));
    }

    [HttpGet("Requests")]
    public ActionResult<IReadOnlyList<MediaRequestView>> Requests() => Ok((Plugin.Instance?.Configuration.MediaRequests ?? [])
        .OrderByDescending(x => x.RequestedAtUtc).Select(ToView).ToArray());

    [HttpDelete("Requests/{id}")]
    public ActionResult DeleteRequest(string id)
    {
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            var removed = configuration.MediaRequests.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return NotFound();
            Plugin.Instance!.UpdateConfiguration(configuration);
        }
        return NoContent();
    }

    private LibraryOption[] LibrariesForValidation() => _library.GetVirtualFolders()
        .SelectMany(v => v.Locations.Select(path => new LibraryOption(
            v.Locations.Count() > 1 ? $"{v.Name} — {Path.GetFileName(path)}" : v.Name ?? "Library",
            Path.GetFullPath(path),
            CollectionType(v.Name, v.CollectionType?.ToString()))))
        .Where(x => x.MediaType.Length > 0)
        .ToArray();

    private static MediaRequestView ToView(MediaAddRequest item) => new(item.Id, item.RequestedBy, item.Title, item.Url, item.MediaType, item.RequestedAtUtc);
    private static string NormalizeMediaType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "movie" => "movie",
        "series" => "series",
        "anime" => "anime",
        _ => throw new InvalidOperationException("Choose Movies, Shows, or Anime.")
    };
    private static string CollectionType(string? name, string? value)
    {
        var type = value?.ToLowerInvariant();
        if (name?.Contains("Anime", StringComparison.OrdinalIgnoreCase) == true) return "anime";
        if (type == "movies") return "movie";
        if (type == "tvshows") return "series";
        return string.Empty;
    }
    private static Uri ValidateLocalSource(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "ftp"))
            throw new InvalidOperationException("Use a complete HTTP, HTTPS, or FTP URL.");
        if (!IPAddress.TryParse(uri.Host, out var address) || !IsPrivate(address))
            throw new InvalidOperationException("Only private/local IP source URLs are allowed.");
        return uri;
    }
    private static Uri EnsureDirectoryUrl(Uri uri) => uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }
}
