using Jellyfin.Plugin.LinkAdder.Models;
using Jellyfin.Plugin.LinkAdder.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Jellyfin.Plugin.LinkAdder.Api;
[ApiController][Authorize(Policy="RequiresElevation")][Route("Plugins/LinkAdder")]
public sealed class LinkAdderController : ControllerBase
{
    private readonly LinkAdderService _service; private readonly ILibraryManager _library;
    public LinkAdderController(IHttpClientFactory clients,ILibraryManager library){_service=new(clients.CreateClient());_library=library;}
    [HttpGet("Libraries")]public ActionResult<IReadOnlyList<LibraryOption>> Libraries(){var result=_library.GetVirtualFolders().SelectMany(v=>v.Locations.Select(path=>new LibraryOption(v.Locations.Count()>1?$"{v.Name} — {Path.GetFileName(path)}":v.Name??"Library",path,CollectionType(v.Name,v.CollectionType?.ToString())))).Where(x=>x.MediaType.Length>0).OrderBy(x=>x.Name).ToArray();return Ok(result);}
    [HttpPost("Remote/Search")]public async Task<ActionResult<IReadOnlyList<RemoteSearchResult>>> SearchRemote([FromBody]RemoteSearchRequest r,CancellationToken ct)=>Ok(await _service.SearchRemoteAsync(r.MediaType,r.Query,ct).ConfigureAwait(false));
    [HttpPost("Preview")]public async Task<ActionResult<PreviewResult>> Preview([FromBody]PreviewRequest r,CancellationToken ct)=>Ok(await _service.PreviewAsync(r.Url,r.MediaType,r.Title,ct).ConfigureAwait(false));
    [HttpPost("Add")]public async Task<ActionResult<AddResult>> Add([FromBody]AddRequest r,CancellationToken ct){var option=LibrariesForValidation().SingleOrDefault(x=>x.Path.Equals(Path.GetFullPath(r.LibraryPath),StringComparison.OrdinalIgnoreCase));if(option is null)return BadRequest(new{error="Choose a current Jellyfin library."});if(option.MediaType!=r.MediaType)return BadRequest(new{error="The selected library type changed. Fetch again."});var preview=await _service.PreviewAsync(r.Url,r.MediaType,r.Title,ct).ConfigureAwait(false);var result=_service.Write(preview,option.Path);_=Task.Run(()=>_library.ValidateMediaLibrary(new Progress<double>(),CancellationToken.None));return Ok(result with{Refresh="Jellyfin library scan started."});}
    private LibraryOption[] LibrariesForValidation()=>_library.GetVirtualFolders().SelectMany(v=>v.Locations.Select(path=>new LibraryOption(v.Name??"Library",Path.GetFullPath(path),CollectionType(v.Name,v.CollectionType?.ToString())))).Where(x=>x.MediaType.Length>0).ToArray();
    private static string CollectionType(string? name,string? value){var type=value?.ToLowerInvariant();if(string.Equals(name,"Anime",StringComparison.OrdinalIgnoreCase))return "anime";if(type=="movies")return "movie";if(type=="tvshows")return "series";return string.Empty;}
}
