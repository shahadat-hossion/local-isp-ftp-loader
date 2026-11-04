using Jellyfin.Plugin.LinkAdder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LinkAdder;
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public override string Name => "Sam Online FTP Loader for Jellyfin";
    public override string Description => "Admin-only Sam Online media search and STRM importer.";
    public override Guid Id => Guid.Parse("9399b942-4b6d-46a7-9a5d-a01764f7ee6a");
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public IEnumerable<PluginPageInfo> GetPages() => [new() { Name="ftploadersearch", EmbeddedResourcePath="Jellyfin.Plugin.LinkAdder.Configuration.configPage.html", EnableInMainMenu=true, DisplayName="Sam Online FTP Loader", MenuSection="admin", MenuIcon="search" }];
}
