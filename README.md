# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content management.

## 🚀 Key Features of v1.8.0

* **Content Verification (NEW):** Compares the number of files in the download folder against the official episode count from TMDB, alerting the user to missing episodes.
* **Settings System (NEW):** Dedicated Preferences window (Edit -> Preferences...) to manage API keys (TMDB/Gemini) and set the default output folder and video quality.
* **Intelligent Naming:** Uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct folder names and standardized episode numbering.
* **Multi-Link Batch Processing:** Supports sequential processing of multiple URLs or search terms.
* **Robust Stability:** The 'Stop Download' function immediately terminates all processes without freezing the application.

## 🐛 Bug Fixes and Stability Patches (v1.6 - v1.8.0)

| Component | Issue | Resolution |
| :--- | :--- | :--- |
| **Download Stability** | App freezes or leaves 'ghost processes' after pressing Stop. | Resource cleanup logic was consolidated into the `Process.Exited` event (v1.6.1). |
| **Metadata Lookup** | Final compilation conflicts due to complex nullable type handling (`int?`). | Resolved using an explicit, brute-force cast workaround to satisfy the compiler (v1.8.0). |
| **File Naming** | Files saved to 'NA' folders; folder names were duplicated (`Show\Show`). | TMDB data is used to inject the official title, and output template syntax was corrected (v1.7.3). |
| **Tooling** | Invalid output template due to unsupported C\# format characters (`:`). | Syntax was simplified to use pure `yt-dlp` format tags (`%(...)02d`) (v1.7.4). |
| **UI/UX** | Menu dropdowns were unreadable and hotkey was missing. | Dark theme style was applied, and Ctrl+P hotkey logic was implemented (v1.8.0). |

## ⚠️ Known Issue in v1.8.0

| Component | Issue | Status |
| :--- | :--- | :--- |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some series pages (e.g., Tubi), even if more are available. | Deferred to v1.9 for dedicated `yt-dlp` flag research. |

## 🔮 Future Development (v1.9 Roadmap)

The next major release, **v1.9**, will focus on improving metadata coverage:

1.  **TVDB Fallback Integration:** Implement a cascading search using TVDB as a secondary source for better metadata on certain content (like Anime).
2.  **Playlist Limit Fix:** Deep research and implementation of the correct `yt-dlp` flag to overcome the 20-item playlist limitation.

