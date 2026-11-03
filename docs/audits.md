# Audits

Quality, security and housekeeping reviews of the plugin, its release tooling and CI. Repeat when
the owner asks for a full review (the `repo-review` skill); record each one below, newest first.

## Checklist for a full review

1. **Open pull requests**, Dependabot's especially
   (`gh api "repos/raetha/Jellyfin.Plugin.IFDB/pulls?state=open"`; sessions have the REST API, not
   GraphQL). Read each diff and its CI, check action pins are the tags they claim
   (`git ls-remote https://github.com/<owner>/<action> refs/tags/<tag>`), then apply them together in
   one pull request into `dev` and close the originals. Jellyfin package majors are a deliberate
   retarget, not a routine update.
2. **Security alerts.** Sessions can't read code scanning, Dependabot or secret scanning alerts
   (403): ask the owner for their titles. Run `dotnet list package --vulnerable --include-transitive`
   and `(cd scripts && npm audit)`, and CodeQL locally if needed (`csharp`, `javascript` and
   `actions` security-and-quality suites).
3. **Tests and build:** `dotnet build -c Release`, `dotnet test -c Release -p:CollectCoverage=true`
   (100% line and branch), `(cd scripts && npm test)`, and the live-site tests
   (`IFDB_LIVE_TESTS=1 dotnet test --filter LiveSiteTests`) to see whether fanedit.org still answers.
4. **Upstream:** new Jellyfin releases (the weekly compatibility workflow builds against the newest
   packages) and changes to fanedit.org's pages; update the fixtures and parsing when the site
   changes.
5. **Security of the design:** the plugin only reads public fanedit.org pages; nothing it parses is
   trusted (HTML parsing, URLs it follows, images it downloads); no credentials.
6. **Workflows:** `contents: read` at the top, write only on the jobs that publish or push;
   `persist-credentials: false` except in jobs that push with git; actions pinned to commit SHAs; no
   `${{ }}` inside `run:`; the `CI passed` gate job covers every check job.
7. **Leftovers:** compatibility nobody needs, unused code, stale docs (`CLAUDE.md`,
   `docs/development.md`, the README).
8. **Record it:** a dated section below, and changelog lines for anything users notice.

## 2026-11-02: repo workflow applied

- Added `CLAUDE.md`, `docs/development.md` (moved from the README's Development section),
  `CONTRIBUTING.md` and this file.
- `CHANGELOG.md` now keeps the next release's notes under `## [Unreleased]` (was
  `## [2.0.1] - Unreleased`) with `### Internal` for changes users don't see (was "Behind the
  scenes"); no entry text changed. `scripts/changelog.js pending` gives the expected version's
  section or `[Unreleased]`, for dev builds and packaging.
- CI requires a dated section only for pull requests into `main` (and on `main`), so an undated
  release can no longer merge, where before it failed only after the merge.
- Earlier the same day: the `CI passed` gate job and `persist-credentials: false`.
