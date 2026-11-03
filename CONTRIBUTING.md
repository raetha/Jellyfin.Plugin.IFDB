# Contributing

Thanks for helping with the IFDB plugin. Bug reports and fixes are welcome.

- **Issues:** say which Jellyfin and plugin versions you run, and include the relevant lines from
  Jellyfin's log (they name the fanedit.org page the plugin tried to read).
- **Pull requests:** open them against the `dev` branch, not `main` (`main` only receives releases;
  `jellyfin-10.11` only takes fixes for Jellyfin 10.11). Before you do:
  - `dotnet build -c Release` and `dotnet test -c Release -p:CollectCoverage=true` pass (analyzer
    warnings are errors, and coverage must stay at 100%).
  - Add a line under `## [Unreleased]` in `CHANGELOG.md` describing the change for people running
    the plugin. Don't change the version or date a changelog section: that's the release step.

Building, testing, versioning and how releases work are in
[docs/development.md](docs/development.md).
