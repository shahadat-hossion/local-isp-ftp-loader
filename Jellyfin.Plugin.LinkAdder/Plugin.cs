using Jellyfin.Plugin.LinkAdder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LinkAdder;
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public override string Name => "Local ISP FTP Loader";
    public override string Description => "Search local media servers and add STRM entries from any signed-in Jellyfin device.";
    public override Guid Id => Guid.Parse("9399b942-4b6d-46a7-9a5d-a01764f7ee6a");
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new() { Name="localispdiscovery", EmbeddedResourcePath="Jellyfin.Plugin.LinkAdder.Configuration.discoveryPage.html", EnableInMainMenu=true, DisplayName="Discover", MenuSection="main", MenuIcon="explore" },
        new() { Name="localispmediaadd", EmbeddedResourcePath="Jellyfin.Plugin.LinkAdder.Configuration.configPage.html", EnableInMainMenu=true, DisplayName="Add Media", MenuSection="main", MenuIcon="add" }
    ];
}
