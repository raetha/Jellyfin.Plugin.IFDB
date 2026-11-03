# Notes for Claude sessions

IFDB: a Jellyfin movie metadata provider (C#, .NET) for fanedits from the Internet Fanedit
Database (fanedit.org). Real users install it from the plugin repository on the `manifest` branch.
The canonical guide is `docs/development.md`; reviews are recorded in `docs/audits.md`. Read the
guide before changing anything. This file is the short version.

## How we work

- **Branches** (`docs/development.md` → "Branches and workflows"): never push to `main`; it gets one
  squash-merged pull request per release, which the owner merges. Claude runs `dev`: small, settled
  changes go straight to it, anything else through a `try/<topic>` branch and pull request that
  Claude squash-merges once CI is green. `dev` must stay releasable; a change that needs the owner's
  decision waits on its `try/` branch. No zip packages. `jellyfin-10.11` takes fixes for the 1.x
  line only, released by running the Release workflow on it.
- **`dev` holds the expected next version** in `build.yaml` and `Directory.Build.props` (four parts,
  `2.1.0.0`; CI checks they match): with the first change after a release, set it to what that
  change calls for under semver, and raise it when a later change calls for more; say what and why
  in a line. A new plugin major goes with a new minimum Jellyfin version.
- Every change adds a line under `## [Unreleased]` in `CHANGELOG.md` (Keep a Changelog; `### Internal`
  for changes users don't see), written for people running the plugin. Released sections are
  frozen. Never date the changelog or open the release pull request unless the owner asks: that
  pull request renames `[Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`; CI then releases it.
- Each push to `dev` publishes a dev build to `manifest-dev.json` (the separate development plugin
  repository), which the owner installs in Jellyfin to test.
- Push back when a requested change looks like a lot of complexity for little gain.

## Validate

- `dotnet build -c Release` (analyzer warnings are errors) and
  `dotnet test -c Release -p:CollectCoverage=true` (fails below 100% line and branch coverage).
- `(cd scripts && npm ci && npm test)` for the release tooling (`changelog.js`,
  `generate-manifest.js`).
- Tests use saved fanedit.org pages in `Jellyfin.Plugin.Ifdb.Tests/Fixtures`, never the network.
  `IFDB_LIVE_TESTS=1 dotnet test --filter LiveSiteTests` checks the real site, only when asked.

## Hard-won facts (details in docs/development.md)

- Jellyfin versions are four plain numbers and Jellyfin installs the highest version across all
  configured repositories. So dev builds are numbered from the latest release (`2.0.0.57` is build 57
  since 2.0.0), not from the expected next version: they sort above that release and below the next
  one, and a server can keep both repositories.
- The plugin repository manifests (`manifest.json`, `manifest-dev.json`, the catalog image) live on
  the `manifest` branch, written only by the Release workflow; dev builds never go into
  `manifest.json`.
- Since October 2026 fanedit.org's Cloudflare bot protection blocks every automated request
  (whatever the network or User-Agent); searches and refreshes fail with a "blocked the request"
  log message until the site lets metadata tools through. Don't try to work around it.
- Items identified by 1.10.11 stored a numeric IFDB id; they still work and switch to the new id
  format on their next refresh.
- Dependabot ignores Jellyfin package majors: moving to a new Jellyfin major is a deliberate
  retarget (`targetAbi`, `framework`), not a routine bump.
- Scraping and title matching are based on circulon/fanedit-provider (MIT; `THIRD_PARTY_NOTICES.md`).
- Sessions can't read Actions settings, secrets or security alerts through the proxy: ask the
  owner. A "full review" follows the checklist at the top of `docs/audits.md`.
