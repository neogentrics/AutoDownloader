# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival,
utilizing the power of `yt-dlp`, `aria2c`, and multiple metadata APIs for seamless content management.

**Developer: Neo Gentrics | [cite_start]AI Development Partner: Gemini (Google)** [cite:110]

[cite_start]**Current Stable Release: v1.8.1** 
[cite_start]**Current Experimental Branch: v1.10.0-beta** [cite:111]

---

## 🚀 Experimental v1.10.0-beta Branch

Development is active on the `v1.10.0-beta` branch, which focuses on metadata and scraper improvements.

* **TVDB Micro-Client:** The broken and outdated `TvDbSharper` dependency has been **removed**. It is replaced by a lightweight, custom `HttpClient` micro-client that communicates directly with the modern TVDB v4 API.
* **Playwright Fallback:** Added Playwright-based page rendering as a fallback for JS/Cloudflare-protected sites.
* **New Pop-up Strategy:** This branch introduces a new user-driven UI flow. When a download starts, the app presents pop-up windows (with timeouts) to:
1. Confirm the auto-detected show name.
2. [cite_start]Allow the user to manually select the metadata source (TMDB or TVDB).

---

## 🚀 Key Features of v1.8.1 (Stable)

* [cite_start]**Content Verification (v1.8):** Compares the number of files in the download folder against the official episode count from TMDB, logging missing episodes.
* **Settings System (v1.8):** Dedicated Preferences window (Edit -> Preferences...) to manage API keys and set the default output folder.
* [cite_start]**Intelligent Naming (v1.7):** Uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct folder names for Plex.
* [cite_start]**Dynamic URL Parsing (v1.8.1):** Dynamically parses show names from direct URLs (like Tubi) to fix metadata lookups and resolve the critical "hardcoded search" bug.
* **Multi-Batch Support (v1.6):** Added support for pasting multiple links and fixed UI freezing by implementing batched logging.

---

## 🔮 Project Development Roadmap (v1.0 to v3.0)

This project is developed in phases. The current `v1.10.0-beta` work completes Phase1, and Phase2 will begin with v2.0.

### Phase1: Stability and Metadata Integration (v1.0 - v1.9)
**Focus:** Establishing a stable base, eliminating critical bugs, and integrating essential naming services[cite:114].

| Version | Key Accomplishment | Architectural / Code Changes |
| :--- | :--- | :--- |
| v1.0 | Initial structure, basic logging, and singular download logic. | WPF UI; `Process.Start` used to call yt-dlp.exe; `ToolManagerService.cs` created. |
| v1.6 | Added multi-link support and fixed initial UI freezing. | Multi-Batch Logic: Implemented `StartDownloadButton_Click` orchestrator loop. |
| v1.7 | Integrated TMDB for intelligent naming and stabilized template syntax. | Metadata: Integrated `TMDbLib`. Template Fixes: Resolved yt-dlp format syntax errors. |
| v1.8.0 | Introduced Content Verification structure and User Settings. | Settings: Implemented `SettingsModel.cs` and `SettingsService.cs`. Validation: Added `PreferencesWindow.xaml`. |
| v1.8.1 | **CRITICAL FIX:** Fixed the hardcoded metadata search bug. | **CRITICAL FIX:** Implemented `ExtractShowNameFromUrl` helper to dynamically parse titles. |
| v1.10.0-beta | **Integration Rework:** Implemented Pop-up Strategy, TVDB episode extraction, Playwright fallback. | **Architecture Rework:** Added scrapers and Playwright fallback; TVDB reflection-based extraction; improved XML metadata persistence. |

### Phase2: User Experience & Scraper Reliability (v2.0 - v2.9)
**Focus:** Overhauling the UI, making the download process transparent, and fixing all known parsing and extraction bugs[cite:117].

| Version | Feature / Enhancement | Impact |
| :--- | :--- | :--- |
| v2.0 | **UI Overhaul & Dynamic Progress** | Replaces the raw log with a status bar showing **Percentage, Speed, and ETA**. Converts the raw log to a toggleable **Developer Log** window. |
| v2.1 | **TVDB User-Selectable Scraper** | Fully implements the user-confirm pop-up for **TVDB** search, dramatically increasing metadata success for Anime/Plex users. |
| v2.2 | **Metadata Engine Completion** | **CRITICAL FIX:** Fixes all known URL parsing bugs (**Season2+ links**, **YouTube playlist titles**) by implementing **Smarter Page Scraping** (scanning HTML body for titles). |
| v2.3 | **Content Limit Fix** | **CRITICAL FIX:** Dedicates research to finding the correct `yt-dlp` flag (e.g., `--no-paged-list`) to resolve the **20-Item Playlist Limit** on sites like Tubi. |
| v2.5 | **Branding & Polish** | Designs and implements a final application icon and logo. Integrates a proper name (replacing "AutoDownloader"). |

### Phase3: Advanced Automation & Expansion (v3.0+)
**Focus:** Adding high-value, complex features like multi-source content syncing, external file processing, and advanced configuration[cite:120].

| Version | Feature / Enhancement | Scope |
| :--- | :--- | :--- |
| v3.0 | **External Subtitle Integration** | Adds option to search external subtitle databases (e.g., OpenSubtitles) and download/rename subtitles when none are provided by the streaming source. [cite:121] |
| v3.1 | **Advanced Download Manager API** | Integrates API calls to popular external download managers (like IDM) for specific URLs. [cite:121] |
| v3.2 | **Metadata XML Save/Load** | **CRITICAL FIX:** Fully implements saving/loading metadata into a local `series.xml` file. This solves the **"already downloaded season"** bug by tracking local state. [cite:121] |
| v3.5 | **Profile Management** | Allows users to save and load different configurations (e.g., "Standard Quality Profile," "4K Max Profile") in the Preferences window. [cite:121] |
