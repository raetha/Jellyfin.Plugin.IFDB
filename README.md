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
Every change to the `dev` branch is published as a test build, numbered from the upcoming version plus a build number (`2.1.0.57` is build 57 of the upcoming 2.1.0). They're in a separate repository:
`https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest-dev.json`

Use it **instead of** the release repository, not alongside it. Jellyfin combines all your repositories and updates each plugin to the highest version it finds in any of them, so with both added, release and dev builds would replace each other. To go back to releases, swap the repositories back and install the release from the catalog. Between development cycles the latest dev build has the same code as the latest release, so staying on the dev repository is also fine.

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

### Build and test
```bash
dotnet build -c Release
dotnet test -c Release -p:CollectCoverage=true   # fails below 100% line and branch coverage
(cd scripts && npm ci && npm test)               # release tooling tests
```

Tests use saved fanedit.org pages in `Jellyfin.Plugin.Ifdb.Tests/Fixtures`, so they never touch the network. `LiveSiteTests` checks known searches and entries against the real site and only runs when asked:
```bash
IFDB_LIVE_TESTS=1 dotnet test --filter LiveSiteTests   # IFDB_USER_AGENT=... to try another User-Agent
```

### Versioning
Plugin versions are [SemVer](https://semver.org/) (`MAJOR.MINOR.PATCH`), written as Jellyfin's four-part version with `.0` on the end (`2.1.0` → `2.1.0.0`). Which Jellyfin a build runs on is set separately by `targetAbi` in `build.yaml`. A new plugin major version accompanies a new minimum Jellyfin version.

Development builds use the upcoming version from `build.yaml` with the CI run number as the fourth part (`2.1.0.57`). They're only listed in `manifest-dev.json`, never in `manifest.json`.

Release notes live in [CHANGELOG.md](CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format). The release workflow uses a version's section as the GitHub release notes, the changelog in Jellyfin's catalog, and the changelog inside the plugin package. CI fails if the version in `build.yaml` has no section.

### Supported Jellyfin versions
The current Jellyfin major is supported on `main`. When a new Jellyfin major ships, the previous line moves to a `jellyfin-<version>` branch for fixes only, and its last release stays installable for servers that haven't upgraded. A weekly check builds and tests against the newest Jellyfin packages so API changes are caught early.

### Branches and workflows
| Branch | Purpose | Workflows |
|---|---|---|
| `dev` | Day-to-day work | CI; every push publishes a dev build |
| `main` | Released code | CI; stable releases are tagged from here |
| `jellyfin-10.11` | Fixes for Jellyfin 10.11 (plugin 1.x) | CI; releases tagged from here |

- **CI** (`ci.yaml`): build with analyzers as errors, tests with the coverage gate, release-script tests, packaging. Runs on every push and pull request.
- **Release** (`release.yaml`): runs CI, then publishes. Dev pushes replace the `dev-build` pre-release and `manifest-dev.json`. Stable releases get their notes from `CHANGELOG.md`, their files attached, and are added to `manifest.json`.
- **Weekly checks** (`compatibility.yaml`): newest Jellyfin packages, and the live fanedit.org tests. The live tests also run on every push to `dev`. Scheduled and manual runs only work once the file is on `main`.
- Code scanning uses GitHub's default CodeQL setup (repository settings); Dependabot runs weekly against `dev`.

### Releasing
While working, add notes under the upcoming version's heading in `CHANGELOG.md` (for example `## [2.1.0] - Unreleased`), with `build.yaml` and `Directory.Build.props` set to that version (`2.1.0.0`).

1. On `dev`, replace `Unreleased` in that heading with the release date.
2. Merge `dev` into `main`.
3. Publish a GitHub release from `main` tagged `v2.1.0` (not marked pre-release). The notes can be left empty: the workflow fills them in from `CHANGELOG.md`, checks the tag matches `build.yaml`, builds, attaches the files and adds the version to `manifest.json`.
4. On `dev`, start the next version: bump `build.yaml` and `Directory.Build.props`, and add its heading to `CHANGELOG.md`.

For a Jellyfin 10.11 fix, do the same on `jellyfin-10.11` and target the release at that branch.

### Plugin repository files
The repository manifests Jellyfin reads (`manifest.json` for releases, `manifest-dev.json` for dev builds, plus the catalog image) live on their own `manifest` branch. Every release and dev build updates them, so keeping them off `main` and `dev` avoids a stream of bot commits (and CI runs) on the working branches, avoids merge conflicts between them, and gives both release lines (`main` and `jellyfin-10.11`) one shared manifest.

The plugin's image is `image.png` in the repository root: it's packaged with the plugin (shown on its page once installed) and copied to the `manifest` branch as the catalog image.

### 🙏 Credits
Scraping and title matching are based on [circulon/fanedit-provider](https://github.com/circulon/fanedit-provider) (and its predecessor [IFDB.bundle](https://github.com/circulon/IFDB.bundle)) for Plex. The plugin image is fanedit-provider's logo, used under the MIT License; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
