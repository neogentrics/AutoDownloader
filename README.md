# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and multiple metadata APIs for seamless content management.

**Current Stable Release: v1.8.1**

---

## 🚀 Experimental Alpha Branch Available!

Development has begun on **v1.9.0-alpha**, which focuses on integrating multiple metadata scrapers. This is an unstable development branch and is not recommended for production use.

Developers interested in testing or contributing to the new TVDB integration can find the work on the alpha branch:
* **View Alpha Branch:** [https://github.com/neogentrics/AutoDownloader/tree/v1.9.0-alpha](https://github.com/neogentrics/AutoDownloader/tree/v1.9.0-alpha)
* **Alpha README:** [View the v1.9.0-alpha README](https://github.com/neogentrics/AutoDownloader/blob/v1.9.0-alpha/README.md)

---

## 🚀 Key Features of v1.8.1 (Stable)

* ✅ **CRITICAL FIX: Dynamic URL Parsing (v1.8.1):** The flagship feature of this release. Fixes a critical v1.8.0 bug by dynamically parsing the show name from direct URLs (like Tubi). This ensures metadata lookups succeed even when not using the Smart Search.

* ✅ **Content Verification (v1.8):** Automatically compares downloaded files against official TMDB episode counts to warn about missing episodes, ensuring season completion.

* ✅ **Centralized Settings System (v1.8):** A dedicated "Preferences" window (Ctrl+P) to securely manage your TMDB & Gemini API keys, default output folder, and video quality settings.

* ✅ **Intelligent Naming (v1.7):** Uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct Plex-friendly folder names (e.g., `/TV Shows/Official Title/Season 01/`).

* ✅ **Smart Search:** Can't find a link? Just type in a show's name (e.g., "The Mandalorian") and the Gemini AI will search the web for a compatible download source.

* ✅ **Batch Downloading:** Use the "Toggle Multi-Link" button to paste a list of URLs or search terms for sequential, unattended downloading.

## ⚠️ Known Issues in v1.8.1 (To Be Fixed in v2.0)

This stable release still contains several critical bugs that will be the focus of the next major version:

| Component | Issue | Status |
| :--- | :--- | :--- |
| **CRITICAL: App Crash** | The application crashes if a metadata lookup fails (e.g., from a Season 2 URL or an un-parsable link). | **Bug - Slated for v2.0** |
| **CRITICAL: URL Parsing** | The app incorrectly parses "Season 2" as the show name from Season 2+ URLs. | **Bug - Slated for v2.0** |
| **CRITICAL: Playlist Parsing**| When given a YouTube playlist, the app incorrectly parses the URL instead of the on-page playlist title. | **Bug - Slated for v2.0** |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some series pages (e.g., Tubi). | **Bug - Slated for v2.0** |

## ✅ Supported Sites (v1.8.1)

While `yt-dlp` supports over 1,000 sites, the "Smart Search" and "Metadata Lookup" features are primarily tested for:
* Tubi (tubitv.com)
* YouTube (youtube.com)

## 🔮 Future Development: v2.0 Roadmap

The next major release, **v2.0**, will be a complete overhaul focusing on reliability and user experience.

1.  **UI Overhaul:** Implement dynamic download progress bars (Percentage, Speed, ETA) and move the verbose output to an optional, toggleable **Developer Log**.
2.  **Scraper Engine Refactor:**
    * Fix all URL and playlist title parsing bugs.
    * Implement smarter page scraping (scan page text if URL parsing fails).
3.  **Multi-Scraper Integration:**
    * Implement **TVDB** as a user-selectable pop-up option (v1.9).
    * Integrate external subtitle database support.
4.  **Branding:** Design a new application icon and official name.
