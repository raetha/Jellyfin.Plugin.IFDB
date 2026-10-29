# Jellyfin.Plugin.Ifdb
A Jellyfin movie metadata provider for the [Internet Fanedit Database (IFDB)](https://fanedit.org/).

Requires Jellyfin 10.11.

### Installing
- In Jellyfin, go to Dashboard → Plugins → Repositories and add:
  `https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest.json`
- Install the IFDB plugin from the catalog and restart Jellyfin
- Keep fanedits in their own Movies library, and in that library's settings enable IFDB as a metadata downloader and image fetcher (move it to the top)
- Scan the library, or use **Identify** on a movie

To try development builds from the `dev` branch, add this repository instead:
`https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest-dev.json`

### ✨ Features
- **Automatic matching** during library scans. A movie is matched only when exactly one IFDB result is a close enough title match (fuzzy scoring that ignores punctuation, accents and word order, and tells "Episode I" from "Episode II")
- **Identify** lists IFDB results ranked by how well they match. You can also paste an entry's fanedit.org address, or its id (the last part of the address), into the IFDB field
- Metadata: title, synopsis, the fanedit's changes (editing details, cuts and additions), fan editor, original film titles, genres, fanedit type and franchise (as tags), fanedit release date, runtime, Trusted Reviewer and user ratings, YouTube trailers, and an IFDB link
- Images: the main listing image and cover art. Wide banners become backdrops and thumbs rather than posters
- Optionally stores the original film's IMDb id
- Polite to fanedit.org: identifies itself, spaces out requests and caches pages

### ⚙️ Settings
Dashboard → Plugins → IFDB:

| Setting | Default | |
|---|---|---|
| Add the fanedit's changes to the overview | On | Appends the Changes tab after the synopsis |
| Add fanedit type / franchise as tags | On | |
| Store the original film's IMDb id | Off | Leave off if other providers are enabled on the library, or they'll fill in the original film's details |
| Credit fan editors as | Director | Or Editor |
| Search keyword matching | All words | How fanedit.org matches the searched title |
| Automatic match score | 90 | Lower to match more files automatically, at the risk of wrong matches |
| Identify dialog score | 60 | Hides weaker results in Identify |
| Site address | https://fanedit.org | `https://ifdb.fanedit.org` also works |
| User-Agent | (plugin default) | See troubleshooting |
| Delay between requests | 1000 ms | |
| Cache pages for | 30 minutes | |

### 🩺 Troubleshooting
- **"fanedit.org blocked the request" in the log:** fanedit.org sits behind Cloudflare bot protection, which sometimes refuses requests from servers. Wait and try again later. If it persists, set a different User-Agent in the plugin settings.
- **A movie wasn't matched automatically:** the log says whether there was no close match or several equally good ones. Use **Identify**, or lower the automatic match score.
- **Items identified by version 1.x** stored a numeric IFDB id. They still work, and switch to the new id format on their next metadata refresh.

### 🧰 Build
```bash
dotnet restore
dotnet build -c Release
dotnet test
```

Pushes to `dev` are tested, built and published as the rolling `dev-build` pre-release, listed in `manifest-dev.json`. Stable releases are published from GitHub releases into `manifest.json`.

### 🙏 Credits
Scraping and title matching are based on [circulon/fanedit-provider](https://github.com/circulon/fanedit-provider) (and its predecessor [IFDB.bundle](https://github.com/circulon/IFDB.bundle)) for Plex.
