using MediaBrowser.Model.Plugins;
namespace Jellyfin.Plugin.LinkAdder.Configuration;
public sealed class PluginConfiguration : BasePluginConfiguration
{
    public List<CustomSource> CustomSources { get; set; } = [];
    public List<MediaAddRequest> MediaRequests { get; set; } = [];
    public AutomationOptions Automation { get; set; } = new();
    public List<AutomationHistoryItem> AutomationHistory { get; set; } = [];
    public List<AutomationAddedItem> AutomationAddedItems { get; set; } = [];
    public List<string> AutomationExcludedIds { get; set; } = [];
}

public sealed class CustomSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MediaType { get; set; } = "movie";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class MediaAddRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestedBy { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MediaType { get; set; } = "movie";
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AutomationOptions
{
    public bool Enabled { get; set; }
    public bool PreviewOnly { get; set; } = true;
    public string TmdbCredential { get; set; } = string.Empty;
    public bool TrendingMovies { get; set; } = true;
    public bool NewMovies { get; set; } = true;
    public bool TopMovies { get; set; }
    public bool TrendingSeries { get; set; } = true;
    public bool NewSeries { get; set; } = true;
    public bool TopSeries { get; set; }
    public bool TrendingAnime { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    public int MaxPerCategory { get; set; } = 5;
    public double MinimumRating { get; set; } = 7;
    public int MinimumVotes { get; set; } = 100;
    public string MovieLibraryPath { get; set; } = string.Empty;
    public string SeriesLibraryPath { get; set; } = string.Empty;
    public string AnimeLibraryPath { get; set; } = string.Empty;
    public DateTime? LastRunUtc { get; set; }
}

public sealed class AutomationHistoryItem
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string ContentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class AutomationAddedItem
{
    public string ContentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public List<string> Files { get; set; } = [];
}
