# Issue Register

The allocation table for `AD-` identifiers. See [ISSUE-CONVENTIONS.md](ISSUE-CONVENTIONS.md)
for the format and labelling rules.

**Next free ID: `AD-050`**

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

## Earlier issues, brought onto the standard

These predate the `AD-` system. They were renamed and relabelled rather than closed, so the
project's history stays legible. IDs were assigned in GitHub issue order.

| ID | # | Title | Type | Status |
| :--- | :--- | :--- | :--- | :--- |
| AD-032 | [#1](https://github.com/neogentrics/AutoDownloader/issues/1) | Implement TMDB metadata integration | feature | Closed |
| AD-033 | [#2](https://github.com/neogentrics/AutoDownloader/issues/2) | Resolve critical app freezing bug | bug | Closed |
| AD-034 | [#3](https://github.com/neogentrics/AutoDownloader/issues/3) | Implement multi-link batch processing | feature | Closed |
| AD-035 | [#4](https://github.com/neogentrics/AutoDownloader/issues/4) | Fix "NA" folder naming | bug | Closed |
| AD-036 | [#5](https://github.com/neogentrics/AutoDownloader/issues/5) | UI polish: dark theme menu and welcome screen | task | Closed |
| AD-037 | [#6](https://github.com/neogentrics/AutoDownloader/issues/6) | Content verification: missing episode check | feature | Closed |
| AD-038 | [#7](https://github.com/neogentrics/AutoDownloader/issues/7) | Design and implement the Settings/Preferences UI | feature | Closed |
| AD-039 | [#8](https://github.com/neogentrics/AutoDownloader/issues/8) | Playlist limit: the 20-item cap on some sites | bug | **Open** |
| AD-040 | [#9](https://github.com/neogentrics/AutoDownloader/issues/9) | Refactor: implement native URL parsing | task | Closed |
| AD-041 | [#10](https://github.com/neogentrics/AutoDownloader/issues/10) | Support external download managers | feature | **Open** |
| AD-042 | [#11](https://github.com/neogentrics/AutoDownloader/issues/11) | Hardcoded search term for metadata lookup on a direct URL | bug | Closed |
| AD-043 | [#13](https://github.com/neogentrics/AutoDownloader/issues/13) | App crash | bug | Closed |
| AD-044 | [#14](https://github.com/neogentrics/AutoDownloader/issues/14) | URL parsing | bug | Closed |
| AD-045 | [#15](https://github.com/neogentrics/AutoDownloader/issues/15) | Playlist parsing | bug | **Open** |
| AD-046 | [#16](https://github.com/neogentrics/AutoDownloader/issues/16) | Wire up the scraper selection UI flow | feature | **Open** |
| AD-047 | [#17](https://github.com/neogentrics/AutoDownloader/issues/17) | Package the forked TVDB library as a private NuGet package | task | **Open** |
| AD-048 | [#18](https://github.com/neogentrics/AutoDownloader/issues/18) | Integrate a new TVDB NuGet package and replace TvDbSharper | task | **Open** |
| AD-049 | [#20](https://github.com/neogentrics/AutoDownloader/issues/20) | Major architectural refactor (v2.0) | task | **Open** |

### Where later work overlaps

- **AD-035** (the original "NA" folder naming bug) and **AD-001** are the same symptom five
  versions apart, from different causes. Worth remembering that this failure mode recurs.
- **AD-045** is largely addressed by **AD-003** and the yt-dlp probe step, but is left open
  until confirmed end to end.
- **AD-047** is satisfied in substance by **AD-015**: the package is vendored into the
  repository rather than published to a private feed.
- **AD-048** is superseded by **AD-023**.
- **AD-049** is substantially delivered by **AD-021**; what remains is **AD-027** and **AD-030**.
