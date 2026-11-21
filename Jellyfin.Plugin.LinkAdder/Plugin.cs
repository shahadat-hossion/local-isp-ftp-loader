using Jellyfin.Plugin.LinkAdder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LinkAdder;
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public override string Name => "Media Source Bridge";
    public override string Description => "Search private media servers, import links, and add movies, shows, or anime to Jellyfin as streams or local files.";
    public override Guid Id => Guid.Parse("9399b942-4b6d-46a7-9a5d-a01764f7ee6a");
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new()
        {
            Name = "localispplugins",
            EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.pluginsPage.html",
            EnableInMainMenu = true,
            DisplayName = "Media Source Bridge",
            MenuSection = "main",
            MenuIcon = "extension"
        },
        new() { Name = "localispdiscovery", EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.discoveryPage.html" },
        new() { Name = "localispsearch", EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.searchPage.html" },
        new() { Name = "localispmediaadd", EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.configPage.html" },
        new() { Name = "localispservers", EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.serverPage.html" },
        new() { Name = "localispimportlink", EmbeddedResourcePath = "Jellyfin.Plugin.LinkAdder.Configuration.importLinkPage.html" }
    ];
}
