namespace Jellyfin.Plugin.LinkAdder.Models;
public sealed record PreviewRequest(string Url, string MediaType, string? Title);
public sealed record AddRequest(string Url, string LibraryPath, string MediaType, string? Title);
public sealed record RemoteSearchRequest(string MediaType, string Query);
public sealed record RemoteSearchResult(string Name, string Url, string Source, string PosterUrl);
public sealed record LibraryOption(string Name, string Path, string MediaType);
public sealed record PlannedFile(string Url, string RelativePath);
public sealed record PreviewResult(string Title, IReadOnlyList<PlannedFile> Files);
public sealed record AddResult(IReadOnlyList<string> Created, IReadOnlyList<string> Skipped, string Refresh);
