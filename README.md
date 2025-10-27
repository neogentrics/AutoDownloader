# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content management.

## 🚀 Key Features of v1.8.1

* **Content Verification (v1.8):** Compares the number of downloaded files against the official episode count from TMDB, logging missing episodes.
* **Settings System (v1.8):** Dedicated Preferences window (Edit -> Preferences...) to manage API keys and set user defaults (e.g., output folder, quality).
* **Intelligent Naming:** Uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct file and folder naming.
* **Robust Stability:** Critical bug fixes for freezing and file naming conflicts.

## 🐛 Bug Fixes in v1.8.1

| Component | Issue | Resolution |
| :--- | :--- | :--- |
| **CRITICAL BUG** | Direct URLs (e.g., Tubi links) incorrectly use a hardcoded search term ('How It's Made') for metadata lookup. | **Fixed:** Implemented `ExtractShowNameFromUrl` helper to dynamically parse the show title from the URL path, ensuring the correct metadata is retrieved. |
| **Metadata Lookup** | Final compilation conflicts related to nullable type handling (`int?`). | Resolved using explicit type casting workarounds (v1.8.0 fix). |

## ⚠️ Known Issue in v1.8.1

| Component | Issue | Status |
| :--- | :--- | :--- |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some series pages (e.g., Tubi), even if more are available. | Deferred to v1.9/v2.0 for dedicated `yt-dlp` flag research. |

## 🔮 Future Development: Jumping to v2.0

The next major cycle will be a **Version 2.0 Overhaul** focused on the user experience.

1.  **UI Overhaul (v2.0):** Implement dynamic download progress indicators (Percentage, Speed, ETA) in the main window and convert the current raw output to an optional, toggleable **Developer Log**.
2.  **TVDB Fallback (v2.0):** Integrate TVDB as a secondary metadata source for improved Anime/Plex support.

## 🛠️ Requirements

* **Windows 10/11**
* **.NET 9.0 (or later)**
* **TMDB API Key** (Required in Preferences)
* **Gemini API Key** (Optional for Smart Search)

For a complete, constantly updated list of supported sites, please visit the official [yt-dlp Supported Sites List.](https://ytdl-org.github.io/youtube-dl/supportedsites.html)
