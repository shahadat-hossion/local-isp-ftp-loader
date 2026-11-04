using MediaBrowser.Model.Plugins;
namespace Jellyfin.Plugin.LinkAdder.Configuration;
public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string MovieOutputRoot { get; set; } = @"D:\Server\Movies\Sam Online FTP Loader";
    public string SeriesOutputRoot { get; set; } = @"D:\Server\TV-Series\English\Sam Online FTP Loader";
    public string SourceRoots { get; set; } = "http://172.16.50.7/\nhttp://172.16.50.14/";
    public int MaxFoldersPerSearch { get; set; } = 500;
    public int MaxDepth { get; set; } = 6;
}
