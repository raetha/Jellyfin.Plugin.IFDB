# Jellyfin.Plugin.Ifdb
A Jellyfin movie metadata provider for the [Internet Fanedit Database (IFDB)](https://fanedit.org/).

### Installing
- In Jellyfin, go to Dashboard → Plugins → Repositories and add:
  `https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest.json`
- Install IFDB from the catalog and restart Jellyfin. Jellyfin picks the newest version built for your server.
- Keep fanedits in their own Movies library, and in that library's settings enable IFDB as a metadata downloader and image fetcher (move it to the top)
- Scan the library, or use **Identify** on a movie

| Jellyfin | Plugin | Notes |
|---|---|---|
| 12.x | 2.x | Current, built from `main` |
| 10.11 | 1.x | Maintenance only, from the `jellyfin-10.11` branch |

#### Development builds
Every change to the `dev` branch is published as a test build, numbered from the latest release plus a build number (`2.0.0.57` is build 57 since 2.0.0). They're in a separate repository:
`https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest-dev.json`

You can add it alongside the release repository. Jellyfin installs the highest version it finds in any repository, and the numbering keeps that in order: each dev build is higher than the release before it, and the next release (`2.1.0.0`) is higher than every dev build that led up to it. So a server with both gets every dev build and then the release, and moves on to dev builds again after that. To stop getting dev builds, remove the development repository; you'll move back to releases with the next one.

What changed in each version is in [CHANGELOG.md](CHANGELOG.md).

### ✨ Features
- **Automatic matching** during library scans. A movie is matched only when exactly one IFDB result is a close enough title match (fuzzy scoring that ignores punctuation, accents and word order, and tells "Episode I" from "Episode II")
- **Identify** lists IFDB results ranked by how well they match. You can also paste any fanedit.org address for an entry (including its reviews page), or its id (the part after `fanedit.org/`), into the IFDB field
- Metadata: title, synopsis, the fanedit's changes (editing details, cuts and additions), fan editor, original film titles, genres, fanedit type and franchise (as tags), fanedit release date, runtime, Trusted Reviewer and user ratings, YouTube trailers, and an IFDB link
- Images: the main listing image and cover art. Wide banners become backdrops and thumbs rather than posters
- Optionally stores the original film's IMDb id
- Polite to fanedit.org: identifies itself, spaces out requests and caches pages
- Careful with what it scrapes:
  - Images are only downloaded from http(s) addresses on public hosts (never IP addresses or local-network names), must really be JPEG, PNG, GIF or WebP files (checked by content, so an SVG or web page can't pose as one), and are limited to 20 MB
  - Text has control characters, hidden text-direction characters and HTML tags removed, and is length-limited; pages are limited to 5 MB
  - Trailers are always plain YouTube links

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
| User-Agent | (plugin default) | How the plugin identifies itself to fanedit.org. Leave empty unless fanedit.org asks for something specific |
| Delay between requests | 1000 ms | |
| Cache pages for | 30 minutes | |

### 🩺 Troubleshooting
- **"fanedit.org blocked the request" in the log:** fanedit.org is behind Cloudflare bot protection. As of October 2026 it challenges every automated request, from any network and with any User-Agent, so the plugin can't currently reach the site. Neither can the IFDB agents for Plex, which make the same kind of request. No plugin setting can fix this: it needs fanedit.org to let metadata tools through (for example with a Cloudflare rule for the `Jellyfin-IFDB/` User-Agent). The plugin logs this error at most every 15 minutes during a scan.
- **A movie wasn't matched automatically:** the log says how many results were checked. Use **Identify**, or lower the automatic match score.
- **More detail in the log:** create `logging.json` in Jellyfin's configuration folder (`/config/config/` in the official Docker image) with the content below. Jellyfin picks it up without a restart. The plugin then logs each fanedit.org request, each search's results, and every blocked request. Delete the file to go back to normal logging.
  ```json
  { "Serilog": { "MinimumLevel": { "Override": { "Jellyfin.Plugin.Ifdb": "Debug" } } } }
  ```
- **Items identified by version 1.10.11** stored a numeric IFDB id. They still work, and switch to the new id format on their next metadata refresh.

## Development

Building, testing, versioning, branches and releasing are in [docs/development.md](docs/development.md); contributing is in [CONTRIBUTING.md](CONTRIBUTING.md).

### 🙏 Credits
Scraping and title matching are based on [circulon/fanedit-provider](https://github.com/circulon/fanedit-provider) (and its predecessor [IFDB.bundle](https://github.com/circulon/IFDB.bundle)) for Plex. The plugin image is fanedit-provider's logo, used under the MIT License; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
