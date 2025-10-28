# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content management.

**Current Stable Release: v1.8.1**

---

## 🚀 Experimental Alpha Branch (v1.9.0-alpha)

Work has begun on the next version, which focuses on multi-scraper support. This is an unstable development branch.

* **Branch:** `v1.9.0-alpha`
* **Goal:** Integrate **The TV Database (TVDB)** as a fallback metadata scraper to improve results for niche content like Anime.
* **Status:** In-progress, contains known compilation bugs.

---

## 🚀 Key Features of v1.8.1 (Stable)

* **Content Verification (v1.8):** Compares the number of files in the download folder against the official episode count from TMDB, logging missing episodes.
* **Settings System (v1.8):** Dedicated Preferences window (Edit -> Preferences...) to manage API keys and set the default output folder and video quality.
* **Intelligent Naming:** Uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct folder names and standardized episode numbering.
* **Multi-Link Batch Processing:** Supports sequential processing of multiple URLs or search terms.
* **Robust Stability:** The 'Stop Download' function immediately terminates all processes without freezing the application.

## 🐛 Bug Fixes in v1.8.1

| Component | Issue | Resolution |
| :--- | :--- | :--- |
| **CRITICAL BUG** | Direct URLs (e.g., Tubi links) incorrectly use a hardcoded search term ('How It's Made') for metadata lookup. | **Fixed:** Implemented `ExtractShowNameFromUrl` helper to dynamically parse the show title from the URL path. |
| **Download Stability** | App freezes or leaves 'ghost processes' after pressing Stop. | Resource cleanup logic was consolidated into the `Process.Exited` event. |
| **Metadata Lookup** | Final compilation conflicts due to complex nullable type handling (`int?`). | Resolved using an explicit type casting workaround. |
| **File Naming** | Files saved to 'NA' folders; folder names were duplicated. | TMDB data is used to inject the official title, and output template syntax was corrected. |
| **UI/UX** | Menu dropdowns were unreadable and hotkey was missing. | Dark theme style was applied, and Ctrl+P hotkey logic was implemented. |

## ⚠️ Known Issue in v1.8.1

| Component | Issue | Status |
| :--- | :--- | :--- |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some series pages (e.g., Tubi). | **Deferred to v2.0** for dedicated `yt-dlp` flag research. |

## 🔮 Future Development (v2.0 Roadmap)

The next major cycle will be a **Version 2.0 Overhaul** focused on the user experience.

1.  **UI Overhaul (v2.0):** Implement dynamic download progress indicators (Percentage, Speed, ETA) in the main window and convert the current raw output to an optional, toggleable **Developer Log**.
2.  **TVDB Fallback (v1.9):** Complete the integration of TVDB as a secondary metadata source.

## 🛠️ Requirements

* **Windows 10/11**
* **.NET 9.0 (or later)**
* **TMDB API Key** (Required in Preferences)
* **Gemini API Key** (Optional for Smart Search)
