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
Every change to the `dev` branch is published as a test build. To try them, add this repository **instead of** the one above:
`https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest-dev.json`

They're kept in a separate repository on purpose: Jellyfin updates plugins to the highest version in any repository you've added, so mixing them would move everyone onto test builds.

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
- **A movie wasn't matched automatically:** the log says how many results were checked. Use **Identify**, or lower the automatic match score.
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

Development builds use the upcoming version from `build.yaml` with the CI run number as the fourth part (`2.1.0.57`).

### Supported Jellyfin versions
The current Jellyfin major is supported on `main`. When a new Jellyfin major ships, the previous line moves to a `jellyfin-<version>` branch for fixes only, and its last release stays installable for servers that haven't upgraded. A weekly check builds and tests against the newest Jellyfin packages so API changes are caught early.

### Branches and workflows
| Branch | Purpose | Workflows |
|---|---|---|
| `dev` | Day-to-day work | CI; every push publishes a dev build |
| `main` | Released code | CI; stable releases are tagged from here |
| `jellyfin-10.11` | Fixes for Jellyfin 10.11 (plugin 1.x) | CI; releases tagged from here |

- **CI** (`ci.yaml`): build with analyzers as errors, tests with the coverage gate, release-script tests, packaging. Runs on every push and pull request.
- **Release** (`release.yaml`): runs CI, then publishes. Dev pushes replace the `dev-build` pre-release and `manifest-dev.json`. Stable releases are attached to their GitHub release and added to `manifest.json`.
- **Weekly checks** (`compatibility.yaml`): newest Jellyfin packages, and the live fanedit.org tests.
- **CodeQL** and Dependabot (weekly, against `dev`).

### Releasing
1. On `dev`, set the version in `build.yaml` and `Directory.Build.props` (e.g. `2.1.0.0`) and update the changelog in `build.yaml`.
2. Merge `dev` into `main`.
3. Publish a GitHub release from `main` tagged `v2.1.0` (not marked pre-release). The workflow checks the tag matches `build.yaml`, builds, attaches the files and updates `manifest.json`.

For a Jellyfin 10.11 fix, do the same on `jellyfin-10.11` and target the release at that branch.

### 🙏 Credits
Scraping and title matching are based on [circulon/fanedit-provider](https://github.com/circulon/fanedit-provider) (and its predecessor [IFDB.bundle](https://github.com/circulon/IFDB.bundle)) for Plex.
