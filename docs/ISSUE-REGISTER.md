# Issue Register

The allocation table for `AD-` identifiers. See [ISSUE-CONVENTIONS.md](ISSUE-CONVENTIONS.md)
for the format and labelling rules.

**Next free ID: `AD-032`**

`#` links to the GitHub issue. The `AD-` ID and the GitHub number are deliberately different:
the GitHub number is assigned by GitHub and this repository's numbering already started
elsewhere, whereas the `AD-` ID is ours and travels across GitHub, Notion and commit messages.

## Fixed

| ID | # | Title | Type | Priority |
| :--- | :--- | :--- | :--- | :--- |
| AD-001 | [#23](https://github.com/neogentrics/AutoDownloader/issues/23) | Output template collapses every episode to one filename | bug | critical |
| AD-002 | [#24](https://github.com/neogentrics/AutoDownloader/issues/24) | Parsed season number is discarded; metadata always looked up season 1 | bug | critical |
| AD-003 | [#25](https://github.com/neogentrics/AutoDownloader/issues/25) | Only the first scraped link is downloaded; the rest of the season is discarded | bug | critical |
| AD-004 | [#26](https://github.com/neogentrics/AutoDownloader/issues/26) | Playwright is used as a blanket fallback for every unknown host | bug | high |
| AD-005 | [#27](https://github.com/neogentrics/AutoDownloader/issues/27) | Hardcoded `--cookies-from-browser firefox` aborts every download without Firefox | bug | critical |
| AD-006 | [#28](https://github.com/neogentrics/AutoDownloader/issues/28) | `SanitizeFileName` is never called; a title containing `:` throws on folder creation | bug | high |
| AD-007 | [#29](https://github.com/neogentrics/AutoDownloader/issues/29) | ffmpeg is never provisioned although the default format requires merging | bug | critical |
| AD-008 | [#30](https://github.com/neogentrics/AutoDownloader/issues/30) | Verification counts a season folder yt-dlp never wrote | bug | high |
| AD-009 | [#31](https://github.com/neogentrics/AutoDownloader/issues/31) | First run with no API key crashes the application | bug | critical |
| AD-010 | [#32](https://github.com/neogentrics/AutoDownloader/issues/32) | Batch loop aborts remaining items and misreports them as user cancellation | bug | high |
| AD-011 | [#33](https://github.com/neogentrics/AutoDownloader/issues/33) | `XmlService` discards edits when the existing file has a different root element | bug | medium |
| AD-012 | [#34](https://github.com/neogentrics/AutoDownloader/issues/34) | `SearchService` retry reuses consumed `HttpContent`, so the 429 backoff never succeeds | bug | medium |
| AD-013 | [#35](https://github.com/neogentrics/AutoDownloader/issues/35) | `InitializeAsyncServices` is unguarded; the UI locks forever if tool download fails | bug | high |
| AD-014 | [#36](https://github.com/neogentrics/AutoDownloader/issues/36) | `ScraperFactory` matches `wcoanimesub` but not `wcoanimedub` | bug | medium |
| AD-015 | [#37](https://github.com/neogentrics/AutoDownloader/issues/37) | The solution cannot restore on any machine except the original developer's | bug | critical |
| AD-016 | [#38](https://github.com/neogentrics/AutoDownloader/issues/38) | `ci-playwright-install.yml` is not valid YAML and has never run | bug | high |
| AD-017 | [#39](https://github.com/neogentrics/AutoDownloader/issues/39) | `dotnet test` runs no tests; the test project is absent from the solution | bug | medium |
| AD-018 | [#40](https://github.com/neogentrics/AutoDownloader/issues/40) | Subtitles are never converted | bug | medium |
| AD-019 | [#41](https://github.com/neogentrics/AutoDownloader/issues/41) | Blocking `Dispatcher.Invoke` per log line stalls the UI during a download | bug | medium |
| AD-020 | [#42](https://github.com/neogentrics/AutoDownloader/issues/42) | Dependency advisory and .NET Framework packages shimmed into net9 | task | medium |
| AD-021 | [#43](https://github.com/neogentrics/AutoDownloader/issues/43) | Download pipeline is trapped in the window code-behind | task | high |
| AD-029 | [#44](https://github.com/neogentrics/AutoDownloader/issues/44) | README and project page describe features that do not match the code | task | medium |
| AD-026 | [#49](https://github.com/neogentrics/AutoDownloader/issues/49) | Preferences window exposes none of the new download settings | feature | high |

## Open

| ID | # | Title | Type | Priority |
| :--- | :--- | :--- | :--- | :--- |
| AD-023 | [#46](https://github.com/neogentrics/AutoDownloader/issues/46) | TVDB access is ~200 lines of reflection that swallows every failure | task | high |
| AD-027 | [#50](https://github.com/neogentrics/AutoDownloader/issues/50) | No progress bar, percentage, speed or ETA during downloads | feature | high |
| AD-030 | [#52](https://github.com/neogentrics/AutoDownloader/issues/52) | No headless CLI entry point for unattended runs | feature | high |
| AD-022 | [#45](https://github.com/neogentrics/AutoDownloader/issues/45) | `DeveloperLogger` queue grows without bound | bug | medium |
| AD-024 | [#47](https://github.com/neogentrics/AutoDownloader/issues/47) | `MaxConcurrentDownloads` setting is never read | bug | low |
| AD-025 | [#48](https://github.com/neogentrics/AutoDownloader/issues/48) | Orphaned `PlaywrightTests` stub project | task | low |

## Resolved by retiring GitHub Pages

The project page now lives on the Recon Towers site, generated from
`recontowers/src/lib/projects.ts`, so the GitHub Pages site was retired rather than
repaired. The deploy workflow has been removed; the Pages site itself is unpublished from
**Settings -> Pages**.

| ID | # | Title | Outcome |
| :--- | :--- | :--- | :--- |
| AD-028 | [#51](https://github.com/neogentrics/AutoDownloader/issues/51) | GitHub Pages publishes the entire repository root | Moot - Pages retired |
| AD-031 | [#53](https://github.com/neogentrics/AutoDownloader/issues/53) | Custom domain does not resolve to GitHub Pages | Retired in favour of recontowers.com |

## Pre-existing issues

These predate the `AD-` system and are left with their original numbering. Where one overlaps
a fixed item it is noted.

| # | Title | Note |
| :--- | :--- | :--- |
| [#20](https://github.com/neogentrics/AutoDownloader/issues/20) | FEATURE: Major Architectural Refactor (v2.0) | Largely delivered by AD-021; remaining UI work is AD-027. |
| [#18](https://github.com/neogentrics/AutoDownloader/issues/18) | FEATURE: Integrate new TVDB NuGet package | Superseded by AD-023. |
| [#17](https://github.com/neogentrics/AutoDownloader/issues/17) | TASK: Package forked TVDB library into a private NuGet package | Addressed by AD-015 (vendored into the repository instead). |
| [#16](https://github.com/neogentrics/AutoDownloader/issues/16) | FEATURE: Wire up new Scraper Selection UI Flow | Reconsider: the pipeline now selects automatically. |
| [#15](https://github.com/neogentrics/AutoDownloader/issues/15) | CRITICAL: Playlist Parsing | Addressed by AD-003 and the yt-dlp probe step. |
| [#10](https://github.com/neogentrics/AutoDownloader/issues/10) | Feature: Support External Download Managers | Still open, unchanged. |
