# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival,
utilizing the power of `yt-dlp`, `aria2c`, and multiple metadata APIs for seamless content management.

**Developer: Neo Gentrics | AI Development Partner: Gemini (Google)**

**Current Release: v1.10.0-beta**

---

## 🔮 Project Development Roadmap (v1.0 to v3.0)

This project is developed in phases. The current `v1.10.0-beta` work focuses on metadata and scraper improvements and important stability fixes.

### Phase1: Stability and Metadata Integration (v1.0 - v1.9)
**Focus:** Establishing a stable base, eliminating critical bugs, and integrating essential naming services.

| Version | Key Accomplishment | Architectural / Code Changes |
| :--- | :--- | :--- |
| v1.0 | Initial structure, basic logging, and singular download logic. | WPF UI; `Process.Start` used to call yt-dlp.exe; `ToolManagerService.cs` created. |
| v1.6 | Added multi-link support and fixed initial UI freezing. | Multi-Batch Logic: Implemented `StartDownloadButton_Click` orchestrator loop. |
| v1.7 | Integrated TMDB for intelligent naming and stabilized template syntax. | Metadata: Integrated `TMDbLib`. Template Fixes: Resolved yt-dlp format syntax errors. |
| v1.8.0 | Introduced Content Verification structure and User Settings. | Settings: Implemented `SettingsModel.cs` and `SettingsService.cs`. Validation: Added `PreferencesWindow.xaml`. |
| v1.8.1 | **CRITICAL FIX:** Fixed the hardcoded metadata search bug. | **CRITICAL FIX:** Implemented `ExtractShowNameFromUrl` helper to dynamically parse titles. |

### v1.10.0-beta (Completed)
Status: Completed (branch: `fix/url-parser-sanitization-playwright-ci`, commit: `6c1d75b`)

- Implemented native URL parsing (v2) that is context-aware and detects season segments (e.g., `season-2`, `s2`, `/season/2`) and uses the preceding segment as the show title. This resolves the critical URL parsing bugs that caused incorrect metadata lookups.
- Added safe filesystem name sanitization for show titles to avoid invalid-directory IO exceptions on Windows (removes/replaces illegal characters and trailing dots/spaces).
- Added Developer Log: timestamped, exportable developer log that captures verbose installer and scraper output for debugging.
- Implemented a robust Playwright installer flow with automated retry (reflection, create+launch check, `playwright.ps1` script execution using `pwsh`/`powershell`, and CLI fallback). Added a CI workflow to install Playwright browsers during Windows builds so distributed artifacts are ready-to-run.
- Wired tool and yt-dlp output into the Developer Log for post-mortem analysis.
- Made YtDlpService accept preferred video quality (via Settings) and use it as the `-f` argument when provided.

This work addresses and closes several tracked issues (see PR: `fix/url-parser-sanitization-playwright-ci`, commit `6c1d75b`).

### Phase2: User Experience & Scraper Reliability (v2.0 - v2.9)
**Focus:** Overhauling the UI, making the download process transparent, and fixing all known parsing and extraction bugs.

| Version | Feature / Enhancement | Impact |
| :--- | :--- | :--- |
| v2.0 | **UI Overhaul & Dynamic Progress** | Replaces the raw log with a status bar showing **Percentage, Speed, and ETA**. Converts the raw log to a toggleable **Developer Log** window. |
| v2.1 | **TVDB User-Selectable Scraper** | Fully implements the user-confirm pop-up for **TVDB** search, dramatically increasing metadata success for Anime/Plex users. |
| v2.2 | **Metadata Engine Completion** | **CRITICAL FIX:** Fixes all known URL parsing bugs (**Season2+ links**, **YouTube playlist titles**) by implementing **Smarter Page Scraping** (scanning HTML body for titles). |
| v2.3 | **Content Limit Fix** | **CRITICAL FIX:** Dedicates research to finding the correct `yt-dlp` flag (e.g., `--no-paged-list`) to resolve the **20-Item Playlist Limit** on sites like Tubi. |
| v2.5 | **Branding & Polish** | Designs and implements a final application icon and logo. Integrates a proper name (replacing "AutoDownloader"). |

### Phase3: Advanced Automation & Expansion (v3.0+)
**Focus:** Adding high-value, complex features like multi-source content syncing, external file processing, and advanced configuration.

| Version | Feature / Enhancement | Scope |
| :--- | :--- | :--- |
| v3.0 | **External Subtitle Integration** | Adds option to search external subtitle databases (e.g., OpenSubtitles) and download/rename subtitles when none are provided by the streaming source. |
| v3.1 | **Advanced Download Manager API** | Integrates API calls to popular external download managers (like IDM) for specific URLs. |
| v3.2 | **Metadata XML Save/Load** | Fully implements saving/loading metadata into a local `series_metadata.xml` file. This solves the **"already downloaded season"** bug by tracking local state. |

---

## How to open a PR
- Create a branch, commit changes, push to origin and open a PR referencing the related issue numbers. Example branch used: `fix/url-parser-sanitization-playwright-ci` (commit `6c1d75b`).

---

## How we closed critical issues in v1.10.0-beta
See the PR `fix/url-parser-sanitization-playwright-ci` for the precise commits and unit-test suggestions. The PR contains the implementation details and developer notes useful for maintainers and reviewers.

---

For additional developer guidance, see the Developer Log via View → Log in the app to export or inspect debug runs. If you want me to open the PR and post issue comments automatically I can do that if you provide a GitHub token or authenticate the `gh` CLI locally.
