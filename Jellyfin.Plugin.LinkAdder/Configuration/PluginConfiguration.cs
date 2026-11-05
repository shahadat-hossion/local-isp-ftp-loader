using MediaBrowser.Model.Plugins;
namespace Jellyfin.Plugin.LinkAdder.Configuration;
public sealed class PluginConfiguration : BasePluginConfiguration
{
    public List<CustomSource> CustomSources { get; set; } = [];
    public List<MediaAddRequest> MediaRequests { get; set; } = [];
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
