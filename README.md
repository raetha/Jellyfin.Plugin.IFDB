# Jellyfin.Plugin.Ifdb
A Jellyfin Movie metadata provider plugin for [Internet Fanedit Database (IFDB)](https://fanedit.org/).

### Installing
- Add this repository to Jellyfin Plugins: https://raw.githubusercontent.com/raetha/Jellyfin.Plugin.IFDB/refs/heads/manifest/manifest.json
- Add and enable the IFDB plugin
- Restart Jellyfin
- Enable the IFDB metadata plugin on the Movies library that contains Fanedits
- Searching for a match should now show IFDB results

### ✨ Features
- Searches IFDB for fanedits by title.
- Fetches metadata: faneditor, genres, rating, year, summary, and poster.
- Ideal for a dedicated "Fanedits" Movie library.

### 🧰 Build
```bash
dotnet restore
dotnet publish -c Release
