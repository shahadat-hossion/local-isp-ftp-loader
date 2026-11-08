using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.LinkAdder.Configuration;
using Jellyfin.Plugin.LinkAdder.Models;
using Jellyfin.Plugin.LinkAdder.Services;
using MediaBrowser.Common.Api;
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
    private readonly AutomationService _automation;
    private readonly DiscoveryService _discovery;

    public LinkAdderController(IHttpClientFactory clients, ILibraryManager library, AutomationService automation, DiscoveryService discovery)
    {
        _service = new LinkAdderService(clients.CreateClient(), Plugin.Instance?.Configuration.CustomSources ?? []);
        _library = library;
        _automation = automation;
        _discovery = discovery;
    }

    [HttpGet("CanManage")]
    [Authorize(Policy = Policies.RequiresElevation)]
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
    [Authorize(Policy = Policies.RequiresElevation)]
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
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IReadOnlyList<CustomSourceView>> Sources()
    {
        var items = (Plugin.Instance?.Configuration.CustomSources ?? [])
            .Select(x => new CustomSourceView(x.Id, x.Name, x.Url, x.MediaType, !string.IsNullOrEmpty(x.Username)))
            .OrderBy(x => x.Name)
            .ToArray();
        return Ok(items);
    }

    [HttpPost("Sources/TestAll")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<IReadOnlyList<SavedSourceTestResult>>> TestAllSources(CancellationToken ct)
    {
        var sources = (Plugin.Instance?.Configuration.CustomSources ?? []).ToArray();
        var tests = sources.Select(async source =>
        {
            try
            {
                var entries = await _service.TestConnectionAsync(source.Url, source.Username, source.Password, ct).ConfigureAwait(false);
                return new SavedSourceTestResult(source.Id, true, entries, $"Connection successful. Found {entries} entries.");
            }
            catch (Exception ex) when (ex is HttpRequestException or WebException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                return new SavedSourceTestResult(source.Id, false, 0, $"Connection failed: {ex.Message}");
            }
        });
        return Ok(await Task.WhenAll(tests).ConfigureAwait(false));
    }

    [HttpPost("Sources")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<CustomSourceView> AddSource([FromBody] CustomSourceRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var mediaType = NormalizeSourceMediaType(request.MediaType);
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
    [Authorize(Policy = Policies.RequiresElevation)]
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

    [HttpGet("Automation/Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<AutomationSettingsView> AutomationSettings()
    {
        var x = Plugin.Instance?.Configuration.Automation ?? new AutomationOptions();
        return Ok(ToAutomationView(x));
    }

    [HttpPost("Automation/Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<AutomationSettingsView> SaveAutomationSettings([FromBody] AutomationSettingsRequest request)
    {
        if (request.IntervalHours is < 1 or > 168) return BadRequest(new { error = "Run interval must be between 1 and 168 hours." });
        if (request.MaxPerCategory is < 1 or > 25) return BadRequest(new { error = "Category limit must be between 1 and 25." });
        if (request.MinimumRating is < 0 or > 10) return BadRequest(new { error = "Minimum rating must be between 0 and 10." });
        if (request.MinimumVotes is < 0 or > 10000000) return BadRequest(new { error = "Minimum votes is invalid." });
        var moviePath = ValidateOptionalLibraryPath(request.MovieLibraryPath, "movie");
        var seriesPath = ValidateOptionalLibraryPath(request.SeriesLibraryPath, "series");
        var animePath = ValidateOptionalLibraryPath(request.AnimeLibraryPath, "anime");
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            var previous = configuration.Automation;
            configuration.Automation = new AutomationOptions
            {
                Enabled = request.Enabled,
                PreviewOnly = request.PreviewOnly,
                TmdbCredential = string.IsNullOrWhiteSpace(request.TmdbCredential) ? previous.TmdbCredential : request.TmdbCredential.Trim(),
                TrendingMovies = request.TrendingMovies,
                NewMovies = request.NewMovies,
                TopMovies = request.TopMovies,
                TrendingSeries = request.TrendingSeries,
                NewSeries = request.NewSeries,
                TopSeries = request.TopSeries,
                TrendingAnime = request.TrendingAnime,
                IntervalHours = request.IntervalHours,
                MaxPerCategory = request.MaxPerCategory,
                MinimumRating = request.MinimumRating,
                MinimumVotes = request.MinimumVotes,
                MovieLibraryPath = moviePath,
                SeriesLibraryPath = seriesPath,
                AnimeLibraryPath = animePath,
                LastRunUtc = previous.LastRunUtc
            };
            Plugin.Instance!.UpdateConfiguration(configuration);
            return Ok(ToAutomationView(configuration.Automation));
        }
    }

    [HttpPost("Automation/Preview")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<AutomationRunResult>> PreviewAutomation(CancellationToken ct) => Ok(await _automation.RunAsync(true, ct).ConfigureAwait(false));

    [HttpPost("Automation/Run")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<AutomationRunResult>> RunAutomation(CancellationToken ct) => Ok(await _automation.RunAsync(false, ct).ConfigureAwait(false));

    [HttpPost("Automation/AddTitle")]
    public async Task<ActionResult<AddDiscoveryTitleResult>> AddDiscoveryTitle([FromBody] AddDiscoveryTitleRequest request, CancellationToken ct)
    {
        var mediaType = NormalizeMediaType(request.MediaType);
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length < 2 || string.IsNullOrWhiteSpace(request.ContentId))
            return BadRequest(new { error = "The discovery title is invalid." });
        var item = new DiscoveryTitle(request.ContentId.Trim(), request.Provider?.Trim() ?? string.Empty, request.Category?.Trim() ?? string.Empty,
            mediaType, title, request.OriginalTitle?.Trim() ?? string.Empty, request.Year, request.Rating, request.Votes, request.PosterUrl?.Trim() ?? string.Empty);
        return Ok(await _automation.AddTitleAsync(item, request.SelectedUrl, ct).ConfigureAwait(false));
    }

    [HttpGet("Automation/Catalog")]
    public async Task<ActionResult<IReadOnlyList<DiscoveryTitle>>> AutomationCatalog(CancellationToken ct)
    {
        var saved = Plugin.Instance?.Configuration.Automation ?? new AutomationOptions();
        var catalog = new AutomationOptions
        {
            TmdbCredential = saved.TmdbCredential,
            TrendingMovies = true,
            NewMovies = true,
            TopMovies = true,
            TrendingSeries = true,
            NewSeries = true,
            TopSeries = true,
            TrendingAnime = true,
            MaxPerCategory = Math.Clamp(saved.MaxPerCategory, 5, 25),
            MinimumRating = saved.MinimumRating,
            MinimumVotes = saved.MinimumVotes
        };
        var items = await _discovery.DiscoverAsync(catalog, ct).ConfigureAwait(false);
        var activeAddedIds = (Plugin.Instance?.Configuration.AutomationAddedItems ?? [])
            .Where(x => x.Files.Any(System.IO.File.Exists))
            .Select(x => x.ContentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Ok(items.Where(x => !activeAddedIds.Contains(x.ContentId)).ToArray());
    }

    [HttpGet("Automation/History")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IReadOnlyList<AutomationHistoryView>> AutomationHistory() => Ok((Plugin.Instance?.Configuration.AutomationHistory ?? [])
        .OrderByDescending(x => x.TimestampUtc).Take(100).Select(x => new AutomationHistoryView(x.TimestampUtc, x.ContentId, x.Title, x.MediaType, x.Status, x.Message)).ToArray());

    [HttpGet("Automation/Exclusions")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IReadOnlyList<AutomationExclusionView>> AutomationExclusions()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(configuration.AutomationExcludedIds.Select(id =>
        {
            var item = configuration.AutomationHistory.LastOrDefault(x => x.ContentId.Equals(id, StringComparison.OrdinalIgnoreCase));
            return new AutomationExclusionView(id, item?.Title ?? id, item?.MediaType ?? string.Empty, item?.TimestampUtc);
        }).ToArray());
    }

    [HttpDelete("Automation/Exclusions/{*contentId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult DeleteAutomationExclusion(string contentId)
    {
        lock (ConfigurationLock)
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            var removed = configuration.AutomationExcludedIds.RemoveAll(x => x.Equals(contentId, StringComparison.OrdinalIgnoreCase));
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
    private static AutomationSettingsView ToAutomationView(AutomationOptions x) => new(x.Enabled, x.PreviewOnly, !string.IsNullOrWhiteSpace(x.TmdbCredential), x.TrendingMovies, x.NewMovies, x.TopMovies, x.TrendingSeries, x.NewSeries, x.TopSeries, x.TrendingAnime, x.IntervalHours, x.MaxPerCategory, x.MinimumRating, x.MinimumVotes, x.MovieLibraryPath, x.SeriesLibraryPath, x.AnimeLibraryPath, x.LastRunUtc, x.LastRunUtc?.AddHours(Math.Clamp(x.IntervalHours, 1, 168)));
    private string ValidateOptionalLibraryPath(string? value, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var full = Path.GetFullPath(value);
        if (!LibrariesForValidation().Any(x => x.MediaType == mediaType && x.Path.Equals(full, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Select a compatible {mediaType} library.");
        return full;
    }
    private static string NormalizeMediaType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "movie" => "movie",
        "series" => "series",
        "anime" => "anime",
        _ => throw new InvalidOperationException("Choose Movies, Shows, or Anime.")
    };
    private static string NormalizeSourceMediaType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "movie" => "movie",
        "series" => "series",
        "both" => "both",
        "anime" => "anime",
        _ => throw new InvalidOperationException("Choose Movies, TV Series, Both, or Anime.")
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
