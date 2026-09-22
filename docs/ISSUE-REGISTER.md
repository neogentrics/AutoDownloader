# Issue Register

The allocation table for `AD-` identifiers. See [ISSUE-CONVENTIONS.md](ISSUE-CONVENTIONS.md)
for the format and labelling rules.

**Next free ID: `AD-094`**

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
| AD-027 | [#50](https://github.com/neogentrics/AutoDownloader/issues/50) | No progress bar, percentage, speed or ETA during downloads | feature | high |
| AD-030 | [#52](https://github.com/neogentrics/AutoDownloader/issues/52) | No headless CLI entry point for unattended runs | feature | high |
| AD-023 | [#46](https://github.com/neogentrics/AutoDownloader/issues/46) | TVDB access is ~200 lines of reflection that swallows every failure | task | high |
| AD-050 | - | Indexer collects other series' episodes from the sidebar and footer | bug | high |
| AD-051 | - | Cookie source is not checked before a run, and the version string is duplicated in four places | bug | medium |
| AD-052 | - | A show name matching more than one series is resolved silently, often to the wrong one | feature | high |
| AD-053 | - | A name yt-dlp cannot write aborts the download instead of being retried | bug | high |
| AD-054 | - | DRM-protected content is reported as a download failure | feature | medium |
| AD-055 | - | No way to get files into a format the target device can play | feature | high |
| AD-056 | - | An unsupported site fails with no indication that the site is the problem | feature | high |
| AD-057 | - | Verification counts an incomplete source as a failed download | bug | medium |
| AD-058 | - | No watch list, so a returning series has to be started by hand every time | feature | high |
| AD-059 | - | Episode links are found by keyword, so listings that do not use those words are missed | bug | high |
| AD-060 | - | A listing spanning several pages yields only the first page of episodes | bug | high |
| AD-061 | - | An episode already on disk is skipped silently, with no way to replace it | feature | high |
| AD-062 | - | Episode titles assigned by page position, so files are confidently misnamed | bug | critical |
| AD-063 | - | Session logs kept by count, so a day of use erased the run you wanted | bug | medium |
| AD-064 | - | Confirm-show dialog auto-answers in 20s, even while you are reading it | bug | high |
| AD-065 | - | Multi-link hint text is parsed as a search term | bug | high |
| AD-066 | - | A batch stops to ask which show each item is, scattered through the run | feature | high |
| AD-067 | - | A finished download is treated as a failure when a post-download step fails | bug | critical |
| AD-068 | - | Episode lists rendered from JSON are invisible to the indexer | bug | high |
| AD-069 | - | A season page defaulting to its newest season is numbered as season 1 | bug | high |
| AD-070 | - | Only two metadata databases, both needing keys, both weak on anime | feature | medium |
| AD-071 | - | Pressing Stop once breaks every later download for the whole session | bug | critical |
| AD-072 | - | A cookie source of "none" is passed to yt-dlp as a browser name | bug | low |
| AD-073 | - | A URL naming a show by UUID is searched for verbatim | bug | high |
| AD-074 | - | The page scanner browses signed out, so login-gated listings look empty | feature | medium |
| AD-075 | - | Episode numbers read out of UUIDs in the URL | bug | critical |
| AD-076 | - | Mined API paths outvote the page's own links, so nav wins over episodes | bug | high |
| AD-077 | - | A numbered listing is numbered by position instead of by its own numbers | bug | medium |
| AD-078 | - | One season is read and the rest are invisible; no way to choose | feature | high |
| AD-079 | - | Dropdown and menu text is unreadable: light popups inherit a white foreground | bug | high |
| AD-080 | - | No way to change theme, and colours are hardcoded in every window | feature | medium |
| AD-081 | - | Raw HTML full of dead route links skips rendering entirely | bug | critical |
| AD-082 | - | A bare route path is treated as an episode | bug | medium |
| AD-083 | - | ComboBox chrome ignores Background, so the closed box stays system-coloured | bug | high |
| AD-084 | - | An advert is downloaded and reported as the episode | bug | critical |
| AD-085 | - | The download archive keys every captured stream as "dash", skipping all later episodes | bug | critical |
| AD-086 | - | Streams with adverts stitched in cannot be downloaded whole | feature | high |
| AD-087 | - | Every season selected is filed under one season number with one season's titles | bug | critical |
| AD-088 | - | Choosing several seasons downloads only one; the rest are indexed then discarded | bug | critical |
| AD-089 | - | Only uncapped quality offered, so a 42-minute episode lands at 3 GB | feature | high |
| AD-090 | - | Dropdown items set a black foreground in code, which no theme can override | bug | medium |
| AD-091 | - | A second show stops the batch halfway to ask which seasons | bug | high |
| AD-092 | - | No application icon; it launches with the default | task | low |
| AD-093 | - | Quality is chosen from presets that cannot know what a source offers | feature | high |

Rows with `-` in the `#` column were found and fixed in the same session, so they were
recorded here and in the commit rather than filed on GitHub first. The `AD-` ID is still
the thing that travels.

## Open

| ID | # | Title | Type | Priority |
| :--- | :--- | :--- | :--- | :--- |
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
| AD-041 | [#10](https://github.com/neogentrics/AutoDownloader/issues/10) | Support external download managers | feature | Closed |
| AD-042 | [#11](https://github.com/neogentrics/AutoDownloader/issues/11) | Hardcoded search term for metadata lookup on a direct URL | bug | Closed |
| AD-043 | [#13](https://github.com/neogentrics/AutoDownloader/issues/13) | App crash | bug | Closed |
| AD-044 | [#14](https://github.com/neogentrics/AutoDownloader/issues/14) | URL parsing | bug | Closed |
| AD-045 | [#15](https://github.com/neogentrics/AutoDownloader/issues/15) | Playlist parsing | bug | **Open** |
| AD-046 | [#16](https://github.com/neogentrics/AutoDownloader/issues/16) | Wire up the scraper selection UI flow | feature | **Open** |
| AD-047 | [#17](https://github.com/neogentrics/AutoDownloader/issues/17) | Package the forked TVDB library as a private NuGet package | task | Closed |
| AD-048 | [#18](https://github.com/neogentrics/AutoDownloader/issues/18) | Integrate a new TVDB NuGet package and replace TvDbSharper | task | Closed |
| AD-049 | [#20](https://github.com/neogentrics/AutoDownloader/issues/20) | Major architectural refactor (v2.0) | task | **Open** |

### AD-086: what has already been ruled out

Streams with adverts stitched in are delivered as many DASH periods on one timeline. Five
routes were tried against a real Discovery+ episode and none of them work:

1. **yt-dlp, left alone.** Downloads a single period, and the first one it can use is an
   advert. This is what produced a 30-second car commercial named as episode one.
2. **yt-dlp, selecting formats by id.** Ids like `v0-1` and `v6-1` share a suffix while
   belonging to different periods; the suffix disambiguates a repeated representation and is
   not a period index. Fragment counts (27/27/27/18) do not scale with the period durations
   (442/304/266/248s), so the mapping is not recoverable this way.
3. **The advert-free manifest the site also serves.** `757b0d_fallback.mpd` really is the
   four content periods with no advert breaks - but yt-dlp extracts no formats from it at
   all, under every selector tried.
4. **ffmpeg against the manifest directly.** Reports 7.4 minutes: the first period only,
   the same limitation as yt-dlp.
5. **Grouping formats by the asset id in their fragment paths.** The asset directory
   (`v/1_91d3c4/v0/i.mp4`) does identify a period, but video and audio of the same period
   have *different* asset ids, so the two cannot be paired this way. The attempt found six
   single-media groups against four content periods.

Two further obstacles apply to any approach: the manifests are session-bound and
short-lived, and the advert insertion differs between fetches - the same episode reported
eleven advert periods on one request and twelve on the next - so pieces gathered across
separate requests are not guaranteed to line up.

What remains is a purpose-built multi-period DASH downloader: resolve each period's
SegmentTemplate from a single manifest fetch, pair video and audio by timeline position
rather than by id, fetch the segments directly, and join them. That is a substantial piece
of work with real failure modes, not an extension of the current path.

Sites without stitched adverts - Food Network, YouTube - are unaffected and work today.

### Where later work overlaps

- **AD-035** (the original "NA" folder naming bug) and **AD-001** are the same symptom five
  versions apart, from different causes. Worth remembering that this failure mode recurs.
- **AD-045** is largely addressed by **AD-003** and the yt-dlp probe step, but is left open
  until confirmed end to end.
- **AD-047** is satisfied in substance by **AD-015**: the package is vendored into the
  repository rather than published to a private feed.
- **AD-048** is superseded by **AD-023**.
- **AD-049** is substantially delivered by **AD-021**; what remains is **AD-027** and **AD-030**.
