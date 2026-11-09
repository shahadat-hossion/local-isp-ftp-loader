using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.LinkAdder.Configuration;
using Jellyfin.Plugin.LinkAdder.Models;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.LinkAdder.Services;

public sealed partial class AutomationService
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);
    private static readonly object ConfigurationLock = new();
    private readonly IHttpClientFactory _clients;
    private readonly ILibraryManager _library;
    private readonly DiscoveryService _discovery;

    public AutomationService(IHttpClientFactory clients, ILibraryManager library, DiscoveryService discovery)
    {
        _clients = clients;
        _library = library;
        _discovery = discovery;
    }

    public async Task WarmSearchIndexesAsync(CancellationToken ct)
    {
        var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
        var service = new LinkAdderService(_clients.CreateClient(), configuration.CustomSources);
        await service.WarmSearchIndexesAsync(ct).ConfigureAwait(false);
    }

    public async Task<AddDiscoveryTitleResult> AddTitleAsync(DiscoveryTitle title, string? selectedUrl, CancellationToken ct)
    {
        if (!await RunLock.WaitAsync(0, ct).ConfigureAwait(false))
            throw new InvalidOperationException("An automation or library-add operation is already in progress.");
        try
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            var service = new LinkAdderService(_clients.CreateClient(), configuration.CustomSources);
            var matches = await FindExactMatchesAsync(service, title, ct).ConfigureAwait(false);
            if (matches.Count == 0)
                return new(ToCandidate(title, "unmatched", "No title match was found on configured private servers."), []);

            RemoteSearchResult match;
            if (string.IsNullOrWhiteSpace(selectedUrl))
            {
                if (matches.Count > 1)
                    return new(ToCandidate(title, "ambiguous", "Select the correct private-server title."), matches);
                match = matches[0];
            }
            else
            {
                match = matches.FirstOrDefault(x => x.Url.Equals(selectedUrl, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The selected title is no longer available in the search results.");
            }

            var path = LibraryPath(configuration.Automation, title.MediaType);
            if (!IsCompatibleLibraryPath(path, title.MediaType))
                return new(ToCandidate(title, "failed", "No compatible destination library is configured."), []);

            var preview = await service.PreviewAsync(match.Url, title.MediaType, title.Title, ct).ConfigureAwait(false);
            var write = service.Write(preview, path);
            if (write.Created.Count == 0)
                return new(ToCandidate(title, "skipped", "Matching STRM files already exist.", match.Url, match.Source), []);

            lock (ConfigurationLock)
            {
                configuration.AutomationAddedItems.RemoveAll(x => x.ContentId.Equals(title.ContentId, StringComparison.OrdinalIgnoreCase));
                configuration.AutomationAddedItems.Add(new AutomationAddedItem { ContentId = title.ContentId, Title = title.Title, MediaType = title.MediaType, Files = write.Created.ToList() });
                configuration.AutomationExcludedIds.RemoveAll(x => x.Equals(title.ContentId, StringComparison.OrdinalIgnoreCase));
                AddHistory(configuration, title, "added", $"Created {write.Created.Count} STRM file(s) from {match.Source}.");
                TrimHistory(configuration);
                Plugin.Instance!.UpdateConfiguration(configuration);
            }
            _ = Task.Run(() => _library.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None));
            return new(ToCandidate(title, "added", $"Created {write.Created.Count} STRM file(s). Jellyfin library scan started.", match.Url, match.Source), []);
        }
        finally
        {
            RunLock.Release();
        }
    }

    public async Task<AutomationRunResult> RunAsync(bool forcePreview, CancellationToken ct)
    {
        if (!await RunLock.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("An automation run is already in progress.");
        try
        {
            var configuration = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");
            ReconcileDeletedItems(configuration);
            var options = configuration.Automation;
            var previewOnly = forcePreview || options.PreviewOnly;
            var discovered = await _discovery.DiscoverAsync(options, ct).ConfigureAwait(false);
            var excluded = configuration.AutomationExcludedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var alreadyAdded = configuration.AutomationAddedItems.Select(x => x.ContentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var service = new LinkAdderService(_clients.CreateClient(), configuration.CustomSources);
            var results = new List<AutomationCandidate>();
            var addedAny = false;

            foreach (var title in discovered)
            {
                ct.ThrowIfCancellationRequested();
                if (excluded.Contains(title.ContentId))
                {
                    results.Add(ToCandidate(title, "excluded", "Excluded by an administrator."));
                    continue;
                }
                if (alreadyAdded.Contains(title.ContentId))
                {
                    results.Add(ToCandidate(title, "skipped", "Already added by automation."));
                    continue;
                }

                try
                {
                    var matches = await FindExactMatchesAsync(service, title, ct).ConfigureAwait(false);
                    if (matches.Count == 0)
                    {
                        results.Add(ToCandidate(title, "unmatched", "No exact title and year match was found on configured private servers."));
                        continue;
                    }
                    if (matches.Count > 1)
                    {
                        results.Add(ToCandidate(title, "ambiguous", "More than one exact private-server match was found; skipped for safety."));
                        continue;
                    }

                    var match = matches[0];
                    if (previewOnly)
                    {
                        results.Add(ToCandidate(title, "matched", "Exact match found; preview mode did not add files.", match.Url, match.Source));
                        continue;
                    }

                    var path = LibraryPath(options, title.MediaType);
                    if (!IsCompatibleLibraryPath(path, title.MediaType))
                    {
                        results.Add(ToCandidate(title, "failed", "No compatible destination library is configured."));
                        continue;
                    }
                    var displayTitle = title.Year is null ? title.Title : $"{title.Title} ({title.Year})";
                    var preview = await service.PreviewAsync(match.Url, title.MediaType, displayTitle, ct).ConfigureAwait(false);
                    var write = service.Write(preview, path);
                    if (write.Created.Count == 0)
                    {
                        results.Add(ToCandidate(title, "skipped", "Matching STRM files already exist.", match.Url, match.Source));
                        continue;
                    }
                    lock (ConfigurationLock)
                    {
                        configuration.AutomationAddedItems.Add(new AutomationAddedItem { ContentId = title.ContentId, Title = displayTitle, MediaType = title.MediaType, Files = write.Created.ToList() });
                        AddHistory(configuration, title, "added", $"Created {write.Created.Count} STRM file(s) from {match.Source}.");
                        Plugin.Instance!.UpdateConfiguration(configuration);
                    }
                    alreadyAdded.Add(title.ContentId);
                    addedAny = true;
                    results.Add(ToCandidate(title, "added", $"Created {write.Created.Count} STRM file(s).", match.Url, match.Source));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    results.Add(ToCandidate(title, "failed", ex.Message));
                }
            }

            lock (ConfigurationLock)
            {
                options.LastRunUtc = DateTime.UtcNow;
                foreach (var result in results.Where(x => x.Status is "failed" or "unmatched" or "ambiguous"))
                    AddHistory(configuration, discovered.First(x => x.ContentId == result.ContentId), result.Status, result.Message);
                TrimHistory(configuration);
                Plugin.Instance!.UpdateConfiguration(configuration);
            }
            if (addedAny) _ = Task.Run(() => _library.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None));
            return new(discovered.Count, results.Count(x => x.Status is "matched" or "added"), results.Count(x => x.Status == "added"), results.Count(x => x.Status is "skipped" or "excluded" or "unmatched" or "ambiguous"), results.Count(x => x.Status == "failed"), previewOnly, results);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private static async Task<IReadOnlyList<RemoteSearchResult>> FindExactMatchesAsync(LinkAdderService service, DiscoveryTitle title, CancellationToken ct)
    {
        var aliases = new[] { title.Title, title.OriginalTitle }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => new[] { x.Trim(), SimplifyTitle(x) })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var searches = aliases.Select(alias => service.SearchRemoteAsync(title.MediaType, alias, ct, includePosters: false));
        var results = await Task.WhenAll(searches).ConfigureAwait(false);
        return results.SelectMany(x => x).GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
            .Where(x => IsTitleMatch(x.Name, aliases))
            .OrderByDescending(x => QualityRank(x.Name + " " + x.Url))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsTitleMatch(string remoteName, IReadOnlyList<string> aliases)
    {
        var normalizedRemote = Normalize(remoteName);
        return aliases.Any(alias =>
        {
            var normalizedAlias = Normalize(alias);
            return normalizedAlias.Length > 0 &&
                (normalizedRemote.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                 normalizedRemote.StartsWith(normalizedAlias + " ", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string SimplifyTitle(string value)
    {
        var title = SeasonSuffixRegex().Replace(value, string.Empty).Trim();
        var separator = title.IndexOf(':');
        return (separator > 0 ? title[..separator] : title).Trim();
    }

    private static int QualityRank(string value)
    {
        if (Regex.IsMatch(value, @"(?:2160p|\b4k\b|ultra[ ._-]?hd)", RegexOptions.IgnoreCase)) return 4;
        if (Regex.IsMatch(value, @"(?:1080p|full[ ._-]?hd|\bfhd\b)", RegexOptions.IgnoreCase)) return 3;
        if (Regex.IsMatch(value, @"\b720p\b", RegexOptions.IgnoreCase)) return 2;
        if (Regex.IsMatch(value, @"(?:\b480p\b|\bsd\b)", RegexOptions.IgnoreCase)) return 1;
        return 0;
    }

    private static string Normalize(string value)
    {
        var text = YearRegex().Replace(value, " ");
        text = ReleaseNoiseRegex().Replace(text, " ");
        text = NonAlphaNumericRegex().Replace(text, " ");
        return SpaceRegex().Replace(text, " ").Trim().ToLowerInvariant();
    }

    private bool IsCompatibleLibraryPath(string path, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = Path.GetFullPath(path);
        return _library.GetVirtualFolders().Any(v =>
        {
            if (!v.Locations.Any(x => Path.GetFullPath(x).Equals(full, StringComparison.OrdinalIgnoreCase))) return false;
            var collectionType = v.CollectionType?.ToString();
            return mediaType switch
            {
                "movie" => collectionType?.Equals("movies", StringComparison.OrdinalIgnoreCase) == true,
                "series" => collectionType?.Equals("tvshows", StringComparison.OrdinalIgnoreCase) == true,
                "anime" => v.Name?.Contains("Anime", StringComparison.OrdinalIgnoreCase) == true ||
                    collectionType?.Equals("tvshows", StringComparison.OrdinalIgnoreCase) == true,
                _ => false
            };
        });
    }

    private static string LibraryPath(AutomationOptions options, string mediaType) => mediaType switch
    {
        "movie" => options.MovieLibraryPath,
        "series" => options.SeriesLibraryPath,
        "anime" => options.AnimeLibraryPath,
        _ => string.Empty
    };

    private static AutomationCandidate ToCandidate(DiscoveryTitle x, string status, string message, string? sourceUrl = null, string? sourceName = null) =>
        new(x.ContentId, x.Category, x.MediaType, x.Title, x.Year, x.Rating, x.Votes, x.PosterUrl, status, message, sourceUrl, sourceName);

    private static void AddHistory(PluginConfiguration configuration, DiscoveryTitle title, string status, string message) =>
        configuration.AutomationHistory.Add(new AutomationHistoryItem { ContentId = title.ContentId, Title = title.Title, MediaType = title.MediaType, Status = status, Message = message });

    private static void TrimHistory(PluginConfiguration configuration)
    {
        if (configuration.AutomationHistory.Count > 500)
            configuration.AutomationHistory = configuration.AutomationHistory.OrderByDescending(x => x.TimestampUtc).Take(500).OrderBy(x => x.TimestampUtc).ToList();
    }

    private static void ReconcileDeletedItems(PluginConfiguration configuration)
    {
        var removed = configuration.AutomationAddedItems.Where(x => x.Files.Count > 0 && x.Files.All(file => !File.Exists(file))).ToArray();
        if (removed.Length == 0) return;
        foreach (var item in removed)
            if (!configuration.AutomationExcludedIds.Contains(item.ContentId, StringComparer.OrdinalIgnoreCase)) configuration.AutomationExcludedIds.Add(item.ContentId);
        configuration.AutomationAddedItems.RemoveAll(x => removed.Contains(x));
        Plugin.Instance!.UpdateConfiguration(configuration);
    }

    [GeneratedRegex(@"(?:\(|\[)?((?:19|20)\d{2})(?:\)|\])?")]
    private static partial Regex YearRegex();
    [GeneratedRegex(@"\b(?:2160p|1080p|720p|480p|web[ ._-]?dl|webrip|bluray|brrip|hdrip|dvdrip|x26[45]|hevc|avc|aac|esub|multi|dual audio).*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseNoiseRegex();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphaNumericRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
    [GeneratedRegex(@"\s+(?:season|series)\s+\d+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonSuffixRegex();
}
