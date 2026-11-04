# Sam Online FTP Loader for Jellyfin

Sam Online FTP Loader for Jellyfin is an administrator-only Jellyfin 12 plugin built specifically for the private media indexes available to **Sam Online** network users. It lets a Jellyfin administrator search for a movie, TV/web series, or anime title without manually browsing the FTP server or copying a folder link.

The plugin searches the predefined Sam Online HTTP directory indexes, displays matching titles in a poster grid, discovers the playable files inside the selected folder, and creates correctly organized `.strm` files in an existing Jellyfin library. Jellyfin reads each `.strm` file as a pointer to the original remote video; the plugin does not download or duplicate the video itself.

> [!IMPORTANT]
> This plugin only works with the configured Sam Online private FTP/HTTP media servers at `172.16.50.x`. These private IP addresses normally work only while connected through Sam Online or another network that can route to those servers. It is not a universal FTP browser, torrent downloader, or internet media search plugin.

## Requirements

- Jellyfin Server `12.0.0` with the matching plugin ABI `12.0.0.0`.
- A Jellyfin administrator account. The page and every API endpoint require administrator elevation.
- Network access from the Jellyfin server to the Sam Online `172.16.50.x` media servers.
- At least one compatible Movies or Shows library whose folder is writable by the Jellyfin server process.
- Permission to use the remote media made available by your network provider.

## Install from the Jellyfin catalog

1. Sign in to Jellyfin as an administrator.
2. Open **Dashboard > Plugins > Repositories**.
3. Select **Add Repository**.
4. Enter the repository name `Sam Online FTP Loader for Jellyfin`.
5. Enter this repository URL:

   ```text
   https://raw.githubusercontent.com/shahadat-hossion/sam-online-ftp-loader-for-jellyfin/main/manifest.json
   ```

6. Save the repository.
7. Open **Dashboard > Plugins > Catalog**.
8. Find **Sam Online FTP Loader for Jellyfin**, open it, and select **Install**.
9. Restart Jellyfin after installation.
10. Open **Dashboard > Sam Online FTP Loader** from the administrator menu.

If the plugin is not visible in Catalog, confirm that the repository URL opens from the Jellyfin server and that `manifest.json` is valid. If installation reports an incompatible version, confirm that the server is Jellyfin `12.0.0`.

## Search and add media

1. Open **Dashboard > Sam Online FTP Loader**.
2. Select a media type:
   - **Movies** searches the mapped movie indexes.
   - **Shows** searches TV, web, Korean, documentary, tutorial, wrestling, awards, and special-program indexes.
   - **Anime** searches the anime and cartoon TV-series index.
3. Enter part or all of the title. A more specific title produces a shorter result list.
4. Select **Search**.
5. Review the poster cards and source-category labels.
6. Select the correct title. The plugin inspects that remote folder and lists the supported video files it found.
7. Select the destination Jellyfin library. Only compatible current libraries are shown.
8. Select **Add to Library**.
9. Wait for Jellyfin to scan the new `.strm` files. Metadata and artwork are then handled by Jellyfin's normal library providers.

Existing destination files are skipped instead of overwritten. Movies and series are placed in a title folder, while series files retain the remote subfolder structure when seasons are available.

## Sam Online source mapping

The source list is built into the plugin; there is no media-output-root or source-server settings page.

### Movies

- Animation Movies — `172.16.50.14/DHAKA-FLIX-14/Animation Movies`
- Animation Movies 1080p — `172.16.50.14/DHAKA-FLIX-14/Animation Movies (1080p)`
- English Movies 1080p — `172.16.50.14/DHAKA-FLIX-14/English Movies (1080p)`
- Hindi Movies — `172.16.50.14/DHAKA-FLIX-14/Hindi Movies`
- IMDb Top 250 Movies — `172.16.50.14/DHAKA-FLIX-14/IMDb Top-250 Movies`
- Hindi Dubbed South Indian Movies — `172.16.50.14/DHAKA-FLIX-14/SOUTH INDIAN MOVIES/Hindi Dubbed`
- South Movies — `172.16.50.14/DHAKA-FLIX-14/SOUTH INDIAN MOVIES/South Movies`

### Shows

- TV and Web Series — `172.16.50.12/DHAKA-FLIX-12/TV-WEB-Series`
- Korean TV and Web Series — `172.16.50.14/DHAKA-FLIX-14/KOREAN TV & WEB Series`
- Awards, FIFA World Cup, TV specials, documentary, tutorial, WWE, and AEW indexes — `172.16.50.10`

### Anime

- Anime and Cartoon TV Series — `172.16.50.10/DHAKA-FLIX-10/Anime & Cartoon TV Series`

The directory names still contain the provider's existing `DHAKA-FLIX` path labels. That is expected and does not mean the plugin supports unrelated public servers.

## What the plugin does not do

- It does not search IMDb, TMDb, Google, torrents, or the public internet for downloadable media.
- It does not download video files to the Jellyfin server.
- It does not use or scan `D:\Server\Jellyfin Remote`.
- It does not add or reconfigure Jellyfin libraries automatically.
- It does not expose the search page to normal Jellyfin users.
- It does not accept arbitrary public FTP or HTTP roots.
- It cannot make the Sam Online private servers reachable from another ISP.

## Troubleshooting

### Search returns no results

- Confirm the Jellyfin server can open the relevant `http://172.16.50.x/` address.
- Try a shorter title without a release year or punctuation.
- Confirm the correct media type is selected.
- The remote folder may have been renamed, moved, or temporarily taken offline by the provider.

### Posters do not appear

The plugin uses the JPG, JPEG, PNG, or WebP image found in each remote media folder. A folder without an image shows the fallback card. Poster loading also fails when the Jellyfin browser device cannot reach the private source address.

### No Jellyfin library appears

- Create the destination library in Jellyfin first.
- Choose Movies for a movie library and Shows or Anime for a shows-compatible library.
- Confirm the library has a local folder path and that Jellyfin can write to it.
- Refresh the plugin page after changing Jellyfin libraries.

### Media was added but does not appear

- Run **Dashboard > Scheduled Tasks > Scan Media Library**.
- Verify that the Jellyfin process can write to the selected library folder.
- Open the generated `.strm` file and verify that its URL is reachable from the Jellyfin server.
- Check the Jellyfin log for library-scan or remote-playback errors.

### A stream does not play

The `.strm` file points directly to the Sam Online server. Playback depends on the remote file still existing, the private server being reachable, and Jellyfin being able to use the remote format. The plugin cannot repair unavailable remote files.

## Manual installation

Download `Sam-Online-FTP-Loader-2.1.1.zip` from GitHub Releases. Extract it into a folder named `Sam Online FTP Loader` inside Jellyfin's plugins directory, then restart Jellyfin.

For a Windows tray installation, the usual destination is:

```text
C:\ProgramData\Jellyfin\Server\plugins\Sam Online FTP Loader
```

On Linux, the plugins directory is commonly under `/var/lib/jellyfin/plugins/`, but the exact location depends on the installation method.

## Build from source

Install the .NET 10 SDK, clone this repository, and run:

```powershell
.\build.ps1
```

The script publishes the project and creates the release ZIP plus an MD5 checksum in `dist`. The ZIP contains the plugin DLL, dependency manifest, and `meta.json`.

## Security model

- All controller endpoints use Jellyfin's `RequiresElevation` authorization policy.
- The service accepts only the built-in private/local source roots.
- A destination must match a current compatible Jellyfin library.
- File and folder names are sanitized before local output is created.
- Existing local files are not overwritten.

## License

Released under the [MIT License](LICENSE).
