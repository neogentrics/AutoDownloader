# Roadmap

Tracked items use `AD-###` identifiers. See [ISSUE-REGISTER.md](ISSUE-REGISTER.md) for the full
allocation table and current status.

---

## Shipped

### Phase 1 — Stability and metadata (v1.0 – v1.9)

| Version | Accomplishment |
| :--- | :--- |
| v1.0 | Initial WPF structure, logging, single-download logic, `ToolManagerService`. |
| v1.6 | Multi-link batch support; fixed the initial UI freeze. |
| v1.7 | TMDB integration via `TMDbLib` for intelligent naming. |
| v1.8.0 | `SettingsModel` / `SettingsService`, Preferences window, content-verification structure. |
| v1.8.1 | Replaced the hardcoded metadata search term with a URL parser. |
| v1.10.0-beta | Season-aware URL parsing, developer log, Playwright installer, scraper layer. |

### Phase 2 — Making it work beyond one site (current)

This phase came out of a full audit of the v1.10.0-beta code. The headline finding was that
several independently-correct pieces were working against each other, which reduced a season
download to a single badly-named file.

**Download correctness**

- **AD-001** Output template collapsed every episode to one filename on any site that did not
  publish series metadata. Spaces inside the `yt-dlp` alternate-field chain silently disabled
  the whole fallback, so files were named `NA - sseason2eepisode01 - NA.mp4` and overwrote each
  other.
- **AD-002** The parsed season number never reached the metadata lookup, which was hardcoded to
  season 1.
- **AD-003** Only the first scraped link was downloaded; the rest of the season was discarded.
- **AD-005** `--cookies-from-browser firefox` was hardcoded, and `yt-dlp` treats an unreadable
  cookie source as fatal — so the app downloaded nothing on any machine without Firefox.
- **AD-007** ffmpeg was never provisioned, although the default format cannot be merged without
  it.
- **AD-008** Verification counted files in a folder `yt-dlp` had never written to.

**Architecture**

- **AD-021** The pipeline moved out of the window code-behind into `DownloadOrchestrator`,
  which references no UI framework. `MainWindow` went from 1409 to 853 lines.
- Episode discovery is now layered: ask `yt-dlp` first, then index the page, then render it
  with a headless browser.
- **AD-004** Unknown hosts no longer route through a generic scraper before `yt-dlp` sees them.
- Metadata queries TMDB and TVDB together and merges the results, rather than forcing a choice.
- `MediaUrlExtractor` observes network traffic when `yt-dlp` cannot resolve a page.

**Build and infrastructure**

- **AD-015** The solution could not restore on any machine but the original developer's.
- **AD-016** The Playwright CI workflow was not valid YAML and had never run.
- **AD-017** `dotnet test` ran no tests; there are now 29.
- **AD-020** Cleared a dependency advisory and removed five .NET Framework packages.

---

## Planned

### Next — usability of what now exists

| ID | Item |
| :--- | :--- |
| **AD-026** | Surface the new download settings (cookies, ffmpeg, archive) in Preferences. They exist in `settings.json` but cannot be set from the UI. |
| **AD-027** | Replace the raw log with a progress bar showing percentage, speed and ETA. |
| **AD-023** | Replace the reflection-based TVDB access with the typed `TvDbSharper` API. Every failure is currently swallowed, so "no such show" and "the reflection guessed wrong" are indistinguishable. |
| **AD-022** | Bound the developer log queue. |

### Then — automation

| ID | Item |
| :--- | :--- |
| **AD-030** | A headless CLI entry point. Most of the work is already done: the orchestrator takes an `IUserPrompt`, and `AutoConfirmPrompt` needs no human. |
| — | A watch list: check a series periodically and fetch new episodes. `series_metadata.xml` and the `yt-dlp` archive already provide the state needed. |
| **AD-024** | Honour `MaxConcurrentDownloads`, which is currently never read. |

### Later

| Item |
| :--- |
| External subtitle sources (OpenSubtitles) when the source provides none. |
| Integration with external download managers (see issue #10). |
| Application icon and branding pass. |
