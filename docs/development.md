# Developing the IFDB plugin

The canonical guide for working on this repository. Reviews are recorded in [audits.md](audits.md).

## Build and test
```bash
dotnet build -c Release
dotnet test -c Release -p:CollectCoverage=true   # fails below 100% line and branch coverage
(cd scripts && npm ci && npm test)               # release tooling tests
```

Tests use saved fanedit.org pages in `Jellyfin.Plugin.Ifdb.Tests/Fixtures`, so they never touch the network. `LiveSiteTests` checks known searches and entries against the real site and only runs when asked:
```bash
IFDB_LIVE_TESTS=1 dotnet test --filter LiveSiteTests   # IFDB_USER_AGENT=... to try another User-Agent
```

## Versioning
Plugin versions are [SemVer](https://semver.org/) (`MAJOR.MINOR.PATCH`), written as Jellyfin's four-part version with `.0` on the end (`2.1.0` → `2.1.0.0`). Which Jellyfin a build runs on is set separately by `targetAbi` in `build.yaml`. A new plugin major version accompanies a new minimum Jellyfin version.

Development builds use the latest release's version with the CI run number as the fourth part (`2.0.0.57`), so they sort above that release and below the next one. They're only listed in `manifest-dev.json`, never in `manifest.json`. Each release from `main` also replaces the entry in `manifest-dev.json` until the next dev build, so servers using both repositories see the release as the newest version.

**`dev` holds the expected next version.** `build.yaml` and `Directory.Build.props` hold the version the next release will have (`2.1.0.0`); with the first change after a release, Claude moves them to what that change calls for, and a later change that calls for more raises them. Release reviews re-check it. (Dev builds are still numbered from the latest release, as above, because Jellyfin can only order four plain numbers.)

Release notes live in [CHANGELOG.md](../CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format: Added, Changed, Deprecated, Removed, Fixed, Security, plus `### Internal` for changes users don't see). Notes for the next release go under `## [Unreleased]`; the release pull request renames it to the dated version heading. The release workflow uses a version's section as the GitHub release notes, the changelog in Jellyfin's catalog, and the changelog inside the plugin package (dev builds carry `[Unreleased]`). CI fails without notes for the version, and a pull request into `main` fails unless the version has a dated section.

## Supported Jellyfin versions
The current Jellyfin major is supported on `main`. When a new Jellyfin major ships, the previous line moves to a `jellyfin-<version>` branch for fixes only, and its last release stays installable for servers that haven't upgraded. A weekly check builds and tests against the newest Jellyfin packages so API changes are caught early.

## Branches and workflows
| Branch | Purpose | Workflows |
|---|---|---|
| `dev` | Day-to-day work; Claude commits here directly or merges its `try/<topic>` pull requests once CI is green, and keeps it releasable | CI; every push publishes a dev build |
| `main` | Released code: one squash-merged pull request from `dev` per release | CI; each merge releases build.yaml's version if it isn't released yet, then resets `dev` to `main` |
| `jellyfin-10.11` | Fixes for Jellyfin 10.11 (plugin 1.x) | CI; releases by running the Release workflow on this branch |

- **CI** (`ci.yaml`): build with analyzers as errors, tests with the coverage gate, release-script tests, the changelog check, packaging, and `CI passed`, a gate job that passes only when the others did (the one status check the `main` ruleset requires). Runs on every push and pull request. Every checkout uses `persist-credentials: false` except in the jobs that push with git (publishing to the `manifest` branch, sync-dev).
- **Release** (`release.yaml`): runs CI, then publishes. Dev pushes replace the `dev-build` pre-release and `manifest-dev.json`. A push to `main` with a version that has no `X.Y.Z` tag yet (no leading "v"; releases up to 2.0.0 are tagged `vX.Y.Z`) creates that release, with notes from `CHANGELOG.md` and the files attached, and adds it to `manifest.json`. It then resets `dev` to `main` if they hold the same files.
- **Weekly checks** (`compatibility.yaml`): newest Jellyfin packages, and the live fanedit.org tests. The live tests also run on every push to `dev`. Scheduled and manual runs only work once the file is on `main`.
- Code scanning uses GitHub's default CodeQL setup (repository settings); Dependabot runs weekly against `dev`.

## Releasing
While working, add notes under `## [Unreleased]` in `CHANGELOG.md`, with `build.yaml` and `Directory.Build.props` set to the expected next version (`2.1.0.0`).

1. On `dev` (only when the owner asks), rename `## [Unreleased]` to `## [2.1.0] - YYYY-MM-DD` with a fresh empty `## [Unreleased]` above it, and update the compare links at the bottom (tags have no leading "v" from 2.1.0: `compare/v2.0.0...2.1.0`).
2. Open a pull request from `dev` into `main`; the owner squash-merges it.
3. That's it. The merge releases `2.1.0`: the workflow checks the changelog heading is dated, builds and tests, creates the GitHub release with the changelog section as its notes, attaches the files and adds the version to `manifest.json`. A version that's already tagged isn't released again, and an undated heading fails the run instead of releasing.
4. `dev` is then reset to `main` automatically, so the next cycle starts from the released code. If something landed on `dev` while the pull request was open, `dev` is left alone; merge `main` into it by hand.
5. With the first change of the next cycle, Claude sets `build.yaml` and `Directory.Build.props` to the next expected version.

For a Jellyfin 10.11 fix, commit to `jellyfin-10.11` (with its changelog heading dated), then run the **Release** workflow on that branch from the Actions tab. Its releases aren't marked as GitHub's "latest" release.

## Plugin repository files
The repository manifests Jellyfin reads (`manifest.json` for releases, `manifest-dev.json` for dev builds, plus the catalog image) live on their own `manifest` branch. Every release and dev build updates them, so keeping them off `main` and `dev` avoids a stream of bot commits (and CI runs) on the working branches, avoids merge conflicts between them, and gives both release lines (`main` and `jellyfin-10.11`) one shared manifest.

The plugin's image is `image.png` in the repository root: it's packaged with the plugin (shown on its page once installed) and copied to the `manifest` branch as the catalog image.

## Repository settings

The owner sets these (sessions can't read most of them):

- **Settings → Rules → Rulesets:** one for `main` (the default branch), enforcement **Active**: restrict deletions, block force pushes, require a pull request before merging with squash as the only allowed method, no required approvals. **Require status checks:** `CI passed`. **Require code scanning results:** CodeQL.
- **Settings → General → Pull Requests:** squash merging only; **Automatically delete head branches** on.
- **Settings → Branches:** a classic branch protection rule for `dev`: nothing required, deletions not allowed, **force pushes allowed** (sync-dev). `jellyfin-10.11` is a maintenance branch: keep it from being deleted.
- **Code security:** CodeQL default setup; Dependabot (`.github/dependabot.yml`) weekly into `dev`.

## Audits

A "full review" follows the `repo-review` skill and the checklist at the top of [audits.md](audits.md), and is recorded there.
