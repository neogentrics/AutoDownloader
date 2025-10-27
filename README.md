# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content management.

## 🚀 Current Focus: v1.9.0-alpha (Multi-Scraper Integration)

This is an **unstable alpha branch**. The focus is on integrating a secondary metadata provider (TVDB) to improve metadata quality for niche content like Anime.

### Key Features (In Progress)

* **TVDB Fallback (In Progress):** Implementing cascading metadata lookup (Try TMDB, then Try TVDB) for better Anime/Plex support.
* **Gemini Search Fix (Complete):** Resolved the bug where the Gemini API key was not being used, stabilizing the search feature.

---

## ⚠️ Known Issues (v1.9.0-alpha)

| Component | Issue | Status |
| :--- | :--- | :--- |
| **MetadataService** | **Will Not Compile.** Errors related to incorrect `TvDbSharper` v4 API calls (e.g., `AuthenticateAsync`, `SearchAsync`). | **Actively Fixing** |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some sites (e.g., Tubi). | **Deferred (Backlog)** |

## 🔮 Future Development (v2.0 Roadmap)

The next major cycle will be a **Version 2.0 Overhaul** focused on the user experience.

1.  **UI Overhaul (v2.0):** Implement dynamic download progress indicators (Percentage, Speed, ETA) in the main window and convert the current raw output to an optional, toggleable **Developer Log**.
2.  **Playlist Limit Fix:** Deep research and implementation of the correct `yt-dlp` flag to overcome the 20-item playlist limitation.

## 🛠️ Requirements

* **Windows 10/11**
* **.NET 9.0 (or later)**
* **TMDB API Key** (Required in Preferences)
* **TVDB API Key** (Required for v1.9)
* **Gemini API Key** (Optional for Smart Search)