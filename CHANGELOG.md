# Changelog

All notable changes to the IFDB plugin. Each version's section here becomes its release notes on GitHub and in Jellyfin's plugin catalog.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). Jellyfin shows them with a fourth `.0` (2.0.0 is `2.0.0.0`). Development builds use the fourth number as a build number (`2.0.0.12` is build 12 of the upcoming 2.0.0) and are published in a separate plugin repository.

## [2.0.0] - 2026-10-30

Requires Jellyfin 12. Servers still on Jellyfin 10.11 are offered version 1.11.0, which has the same fixes and features.

### Known issue
- **fanedit.org currently blocks the plugin.** Since October 2026 the site's Cloudflare bot protection refuses every automated request, whatever the network or User-Agent, and that affects the IFDB agents for Plex too. Until fanedit.org lets metadata tools through, searches and metadata refreshes fail with a "blocked the request" message in the log. This release brings the plugin up to date with the current site, so it will work as soon as access is allowed again.

### Fixed
- **Updated for fanedit.org's current site** ([#2](https://github.com/raetha/Jellyfin.Plugin.IFDB/issues/2)). fanedit.org's pages had changed and the plugin could no longer read them, so searches and scans found nothing. It now reads the current layout, handles searches that jump straight to a single match, and identifies itself to the site (requests without one are often refused).
- Unidentified fanedits are now matched automatically during library scans. Before, they only got metadata through **Identify**.
- Trusted Reviewer ratings showed as a tiny critic score (9.4 out of 10 appeared as 9%). They now show as 94%.
- A rating with no reviews behind it is no longer shown as 0.

### Added
- Smarter title matching that ignores punctuation, accents and word order, and tells "Episode I" from "Episode II". A fanedit is only matched automatically when there is one clear match; otherwise **Identify** lists the candidates, best first.
- You can paste a fanedit.org address (or the entry's id) into the IFDB field in **Identify** to pick an exact entry.
- More metadata: the fanedit's changes (editing details, cuts and additions) in the overview, fan editors credited as directors (or editors), original film titles, fanedit type and franchise as tags, YouTube trailers, and an IFDB link on the item's page.
- More images: cover art alongside the main image. Wide banners are offered as backdrops and thumbnails instead of being squeezed into posters.
- Optionally store the original film's IMDb id.
- A settings page (replacing placeholder options) for matching, metadata choices, and how the plugin talks to fanedit.org.
- A clear log message when fanedit.org's bot protection refuses requests, logged at most every 15 minutes during a scan. More detail (each request and search) is available at Debug log level; see the README's troubleshooting section.

### Changed
- Fanedits are now identified by their fanedit.org address (for example `m4s-the-hobbit-book-edit`) instead of a number. Items identified by version 1.10.11 keep working and switch over on their next metadata refresh.
- The plugin is gentler on fanedit.org: one request at a time, a second apart by default, with pages cached for 30 minutes.
- The overview keeps its line breaks in Jellyfin's web client.
- The plugin has an image in Jellyfin's catalog and plugin pages.

### Security
- Images are only downloaded from public web addresses, must really be JPEG, PNG, GIF or WebP files (checked by their content), and are limited to 20 MB. Anything else, such as an SVG or a web page posing as an image, is skipped.
- Text taken from fanedit.org is cleaned of control characters, hidden text-direction characters and HTML before it's saved. Pages are limited to 5 MB.

### Removed
- The ImageSharp library. Images are no longer downloaded twice just to measure them.

### Behind the scenes
- Over 200 automated tests covering every line of the plugin, run on every change.
- Automated releases: each release is built, tested and published from this changelog. Development builds are published from every change to the `dev` branch.
- Weekly checks against the newest Jellyfin release and the live fanedit.org site.

## [1.11.0] - 2026-10-30

The last version for Jellyfin 10.11, with the same fixes and features as 2.0.0 above. Jellyfin 12 servers are offered 2.0.0 instead.

## [1.10.11] - 2025-11-17

- First release, for Jellyfin 10.11: searches fanedit.org by title and fetches the faneditor, genres, rating, year, summary and poster.

[2.0.0]: https://github.com/raetha/Jellyfin.Plugin.IFDB/compare/v1.10.11.0...v2.0.0
[1.11.0]: https://github.com/raetha/Jellyfin.Plugin.IFDB/compare/v1.10.11.0...v1.11.0
[1.10.11]: https://github.com/raetha/Jellyfin.Plugin.IFDB/releases/tag/v1.10.11.0
