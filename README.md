# Jellyfin plugin repository for IFDB

This branch holds the files Jellyfin reads to install and update the [IFDB plugin](https://github.com/raetha/Jellyfin.Plugin.IFDB). They're written by the release workflow; don't edit them by hand.

- **`manifest.json`**: the plugin repository to add in Jellyfin (Dashboard, then Plugins, then Repositories):
  `https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest.json`
  It lists every release: 2.x for Jellyfin 12, 1.x for Jellyfin 10.11.
- **`manifest-dev.json`**: the development builds repository, holding only the latest build of the `dev` branch. Use it *instead of* `manifest.json` to test (Jellyfin updates each plugin to the highest version in any added repository, so having both would mix release and test builds):
  `https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest-dev.json`
- **`image.png`**: the image shown in Jellyfin's plugin catalog.

These files live on their own branch so release and build updates don't add commits to the code branches, and so both release lines share one repository. See the main [README](https://github.com/raetha/Jellyfin.Plugin.IFDB/blob/main/README.md) and [CHANGELOG](https://github.com/raetha/Jellyfin.Plugin.IFDB/blob/dev/CHANGELOG.md).
