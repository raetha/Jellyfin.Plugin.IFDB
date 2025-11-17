# Jellyfin.Plugin.Ifdb
A Jellyfin metadata provider plugin for [Internet Fanedit Database (IFDB)](https://fanedit.org/).

### ✨ Features
- Searches IFDB for fanedits by title.
- Fetches metadata: faneditor, genres, rating, year, summary, and poster.
- Ideal for a dedicated "Fanedits" library.

### 🧰 Build
```bash
dotnet restore
dotnet publish -c Release
