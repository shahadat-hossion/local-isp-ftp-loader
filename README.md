# Media Source Bridge for Jellyfin

<p align="center">
  <img src="assets/github-preview.jpg" alt="Media Source Bridge for Jellyfin banner featuring a blue-lit cinema">
</p>

<p align="center">
  <a href="https://github.com/shahadat-hossion/media-source-bridge/releases/latest"><img src="https://img.shields.io/github/v/release/shahadat-hossion/media-source-bridge?label=latest%20release" alt="Latest release"></a>
  <a href="https://github.com/shahadat-hossion/media-source-bridge/actions/workflows/build.yml"><img src="https://github.com/shahadat-hossion/media-source-bridge/actions/workflows/build.yml/badge.svg" alt="Build status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/shahadat-hossion/media-source-bridge" alt="MIT license"></a>
  <img src="https://img.shields.io/badge/Jellyfin-12.0.0-00A4DC?logo=jellyfin" alt="Jellyfin 12.0.0">
</p>

**Search private FTP, HTTP, or HTTPS media servers and add movies, TV shows, web series, and anime to Jellyfin as `.strm` files or administrator-downloaded local media.**

Media Source Bridge is a Jellyfin 12 plugin for media indexes hosted by a local ISP, home server, NAS, or another private-network server. Signed-in Jellyfin users can search those configured sources from a PC, TV, or phone and add a title to an existing Jellyfin library without manually browsing directories or copying links.

By default, the plugin creates small `.strm` pointer files and playback continues to use the original private server. Administrators can instead explicitly download the actual video files into the selected Jellyfin library.

> [!IMPORTANT]
> Only loopback and private IPv4 sources are accepted (`10.x.x.x`, `172.16-31.x.x`, and `192.168.x.x`). The Jellyfin server must be able to route to the source. This is not a public internet search engine, torrent client, or general-purpose FTP browser.

## Features

- Search multiple private FTP, HTTP, and HTTPS directory indexes from Jellyfin.
- Paste a direct media file or folder link from a configured source and analyze it before adding.
- Keep title search and direct-link importing on separate, focused Jellyfin pages.
- Separate searches for **Movies**, **Shows**, and **Anime**.
- Configure a shared **Movies & TV Series (Both)** source.
- Browse poster-card results and preview the playable files found in a title folder.
- Create organized `.strm` files in any compatible, writable Jellyfin library.
- Let administrators download actual movies, series episodes, and anime episodes into the selected library folder.
- Preserve season subfolders and remote series structure where available.
- Skip existing destination files instead of overwriting them.
- Test one source or all configured sources from the plugin page.
- Export all configured sources to a portable JSON file and import them on another server. Credentials are excluded by default and can be included explicitly.
- Use optional FTP credentials stored in Jellyfin's plugin configuration.
- Let regular signed-in users search and add media while keeping source management administrator-only.
- Discover trending, new, and top-rated movies or series through TMDb.
- Discover trending anime through AniList.
- Preview exact private-server matches or add them automatically on a configurable schedule.
- Remember administrator-deleted automated titles so they are not added again.

Supported video extensions include `.mkv`, `.mp4`, `.avi`, `.mov`, `.m4v`, `.ts`, `.m2ts`, `.webm`, `.wmv`, `.mpg`, and `.mpeg`.

## Requirements

- Jellyfin Server **12.0.0** with plugin ABI `12.0.0.0`.
- An administrator for repository installation and private-source configuration.
- Network access from the Jellyfin server to each configured media source.
- An existing Movies or Shows library with a folder writable by the Jellyfin process.
- A signed-in Jellyfin account to search and add media.
- Permission to access and use the media supplied by the private server.

## Install through the Jellyfin plugin catalog

1. In Jellyfin, open **Dashboard > Plugins > Repositories**.
2. Select **Add Repository**.
3. Use `Media Source Bridge` as the repository name.
4. Paste this manifest URL:

   ```text
   https://raw.githubusercontent.com/shahadat-hossion/media-source-bridge/main/manifest.json
   ```

5. Save, then open **Dashboard > Plugins > Catalog**.
6. Select **Media Source Bridge** and choose **Install**.
7. Restart Jellyfin.

If you previously installed Local ISP FTP Loader, update the existing repository entry to the manifest URL above. The plugin keeps the same ID.

If the plugin is missing from the catalog, open the manifest URL from the Jellyfin server and confirm the server is running Jellyfin `12.0.0`.

## Configure a private media source

An administrator performs this once for each server:

1. Open **Setup Media** from the Media Source Bridge landing page.
2. Enter a display name, media type, and complete `ftp://`, `http://`, or `https://` directory URL.
3. Add FTP credentials if the server requires them.
4. Select **Test connection**.
5. Select **Add server** after the test succeeds.

Choose **Movies & TV Series (Both)** when one server contains both categories. Source configuration, connection tests, saved credentials, and source removal are visible only to Jellyfin administrators.

The plugin does not include provider-specific addresses. Each administrator supplies the private server URLs available on their own network.

## Search and add media

1. Select the **Search & Add Media** icon in the Jellyfin web header or open **Search** from the Media Source Bridge landing page.
2. Enter at least two characters from a movie, show, episode, or anime title.
3. Review the single **Search Results** grid. Matching library items appear first, followed by titles available from configured media servers.
4. Duplicate titles are collapsed into one card. Library items show **Play** (or **Open** for a series); titles not yet in Jellyfin show **Add & Play**.
5. Select **Add & Play** to create the `.strm` in the configured default save location, refresh that library, and open the indexed title. No destination popup is shown.

### Default save locations

An administrator can open **Setup Media** and assign one default Jellyfin library for Movies, Shows, and Anime. These shared defaults are used by Discover, home Search, Search Media, Import Link, Add Media, STRM creation, and downloads. Changing a default there changes the destination for every workflow.
6. The plugin creates the lightweight `.strm` files, starts a Jellyfin scan, waits for the indexed item, and then plays the movie or opens the show. If indexing exceeds 90 seconds, it reports that the title was added and lets Jellyfin finish in the background.

Administrators can expand **Search button settings** on the unified search page to keep Jellyfin's standard search button or replace it with the plugin search. The plugin search icon is always labelled **Search & Add Media** for accessibility.

Jellyfin handles metadata, artwork, transcoding, and playback normally. The remote server must remain reachable whenever a stream is played.

## Automatic discovery

Open **Media Source Bridge** in Jellyfin's sidebar to reach the landing page and expand its links to **Discover**, **Search**, **Import Link**, and administrator-only **Setup Media**. Select the sidebar entry again to collapse the links. Discover displays responsive Movies, Shows, and Anime grids for signed-in users, with animated skeleton cards and progress indicators during API and private-server operations. Administrators can expand **Automation settings** on that page and configure:

- A [TMDb API key or read-access token](https://www.themoviedb.org/settings/api) for movie and series lists. The page includes a **Get TMDb API key** button that opens this official account page in a new tab.
- Any combination of trending, new, and top-rated movies or series.
- Trending anime from AniList; no AniList credential is required.
- A run interval, per-category limit, minimum rating, and minimum TMDb vote count. Discover uses the shared Default Save Locations from Setup Media.
- **Preview only** mode to inspect exact matches without creating files.

The discovery providers supply titles and metadata only. The plugin searches the configured private sources and creates `.strm` files only when one exact title/year match is found. Missing and ambiguous matches are skipped. Scheduled runs check every 15 minutes and execute when the configured interval is due.

Automatically added titles and their generated files are tracked. If an administrator deletes all tracked `.strm` files for a title, the next run places its provider ID in **Excluded titles** instead of restoring it. An administrator can select **Allow again** to remove that exclusion.

> [!NOTE]
> This product uses the TMDB API but is not endorsed or certified by TMDB. Anime discovery data is provided by AniList.

## Manual installation

Download the plugin ZIP from [GitHub Releases](https://github.com/shahadat-hossion/media-source-bridge/releases/latest). Extract it into a folder named `Media Source Bridge` inside Jellyfin's plugins directory, then restart Jellyfin.

Common plugin locations:

- Windows tray installation: `C:\ProgramData\Jellyfin\Server\plugins\Media Source Bridge`
- Linux package installation: `/var/lib/jellyfin/plugins/Media Source Bridge`

The exact Linux path can differ for Docker, Snap, or other installation methods.

## How it works

```text
Private FTP/HTTP index
        ↓ search and inspect
Media Source Bridge
        ↓ create .strm pointers or download actual files (admin)
Existing Jellyfin library
        ↓ normal library scan
Jellyfin metadata and playback
```

Movies are placed in a title folder. Series keep useful remote subfolders such as seasons when available. File and directory names are sanitized before local files are created, and an existing destination file is never overwritten.

## Troubleshooting

<details>
<summary><strong>Search returns no results</strong></summary>

- Ask an administrator to run **Test connection** or **Test all servers**.
- Try a shorter title without a release year or punctuation.
- Confirm that the correct Movies, Shows, or Anime tab is selected.
- Check whether the provider renamed, moved, or temporarily disabled the remote folder.
</details>

<details>
<summary><strong>No default save location appears</strong></summary>

- Create the Movies or Shows library in Jellyfin first.
- Confirm that it has a local folder path writable by the Jellyfin process.
- Refresh **Add Media** after changing Jellyfin libraries.
</details>

<details>
<summary><strong>Media was added but does not appear</strong></summary>

- Run **Dashboard > Scheduled Tasks > Scan Media Library**.
- Verify that Jellyfin can write to the selected library folder.
- Open the generated `.strm` file and confirm its URL is reachable from the Jellyfin server.
- Check the Jellyfin log for library scan or remote playback errors.
</details>

<details>
<summary><strong>A stream does not play</strong></summary>

The `.strm` file points directly to the configured private server. Confirm that the remote file still exists, the private server is online, and the Jellyfin server can reach it. The plugin cannot restore an unavailable remote file.
</details>

<details>
<summary><strong>An older interface is still visible after updating</strong></summary>

Restart Jellyfin, close and reopen **Add Media**, then hard-refresh the web client with **Ctrl + F5**. Version `2.4.1` or newer is required for the combined Movies and TV source type.
</details>

## Security and privacy

- All plugin endpoints require an authenticated Jellyfin user.
- Source-management operations require Jellyfin administrator elevation.
- Only loopback and private IPv4 source URLs are accepted.
- A source URL must remain inside a configured private root.
- Configure Movies, Shows, and Anime under **Setup Media → Default Save Locations**. Each destination must match a current compatible Jellyfin library.
- Existing local files are not overwritten.
- Actual media downloads require Jellyfin administrator elevation and can only target a current compatible library path.
- FTP credentials remain in Jellyfin's plugin configuration.

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), clone this repository, and run:

```powershell
.\build.ps1
```

The build creates a release ZIP and MD5 checksum in `dist`. The ZIP contains the plugin DLL, dependency manifest, and `meta.json`.

## Limitations

- No IMDb, TMDb, Google, torrent, or public-internet media search.
- Downloads run as background jobs on the Jellyfin server, so the administrator may leave or close the Add Media page after starting one. Restarting Jellyfin interrupts active transfers; partial files are discarded rather than imported.
- No automatic Jellyfin library creation or reconfiguration.
- No anonymous access or public FTP/HTTP roots.
- No ability to bypass ISP routing or make a private server reachable from another network.

## License

Media Source Bridge is released under the [MIT License](LICENSE).
