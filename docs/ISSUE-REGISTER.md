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
| AD-094 | - | Episode numbers ran straight through the series instead of restarting each season | bug | high |
| AD-095 | - | An episode listed under two seasons was planned and downloaded twice | bug | medium |
| AD-096 | - | The download archive skipped episodes whose files were gone, and counted them as successes | bug | high |
| AD-097 | - | Verification counted one season folder against every season's episodes | bug | medium |
| AD-098 | - | The source-is-short check compared the whole plan against one season's count | bug | medium |
| AD-099 | - | Titles assigned by position, so an episode the source lacks shifted every title after it | bug | high |
| AD-100 | - | Strict title matching rejected differently-spelled titles and cut-heavy listings | bug | high |
| AD-101 | - | A season tab listing another season's episodes took them for good | bug | high |
| AD-102 | - | A link with no database title took a URL path as its name | bug | medium |

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

### The five-season run that reported nineteen successes and left four files

A live run of Alex vs America stopped with `19 succeeded, 0 failed` while the folder held
four new episodes. Three separate defects, plus one thing that was never in the build:

- **AD-094.** The listing numbers its tiles straight through the whole series - 1 to 65
  across five seasons - so season two was filed as S02E11 to S02E20. Reading those numbers
  was the right fix for AD-077 and the wrong one here. Numbering now restarts at one in each
  season, but only where the numbering really is continuous: a page that already numbers per
  season is left alone, since renumbering it would be the thing that broke it.
- **AD-095.** The season-two listing repeats Alex vs Shellfish, which season one also
  carried, so the same video was planned under two numbers. Links are now deduplicated by
  URL before anything is filtered or numbered, earliest season keeping it.
- **AD-096.** `downloaded.txt` records what was downloaded, not what is still on disk. Six
  episodes deleted between runs were skipped as "already recorded" and counted as
  successes - which is how the totals and the content verification disagreed. The archive is
  now bypassed whenever the expected file is absent.

- **AD-097.** The verification counted video files in the target season's folder but
  measured them against every link in the plan, so a five-season run compared one folder
  against sixty-five episodes. Against the real folders this reported 4 where 12 were
  present - the exact number the live log showed. It now counts every season folder the run
  writes into.

The run also produced 3 GB files at roughly 10 Mbps. That was not a defect: the binary was
built at 01:14:51 and the quality picker landed at 01:20:01, so it was never in that build,
and the saved `FormatPreference` was still `best` from the day before. Nothing had been
asked to make the files smaller. The picker's failure path wrote only to `OnDiagnostic`,
which never reaches the session log, so a probe that failed would have been invisible; it
now logs.

### Season 3 was not short - it was mislabelled

Chasing why season 3 of Alex vs America held 9 files where the databases list 10 turned up
something worse than a missing episode.

Food Network does not carry Alex vs Southern Comfort, season 3 episode 4. Episode numbers
were taken from each link's position in the listing, so every episode after the gap moved
up one: Alex vs Salmon was written as E04 wearing the title "Alex vs Southern Comfort", and
Alex vs California - the real E10 - was filed as E09 under the name "Alex vs Potatoes". Six
of the nine files carried the wrong episode's name.

- **AD-099.** The links say what they are; the slug `alex-vs-california` is the episode
  title. `EpisodeTitleMatcher` already existed to use that, but it ran only when *no* link
  carried a number - and Food Network numbers its tiles, so it never ran. Numbers are now
  matched to the database episode whose title the link carries, season by season, and a gap
  in the source stays a gap. Links matching no database episode - alternate cuts, which the
  databases do not list - are numbered after the last real episode so they cannot displace
  one. The log names the missing episodes outright.
- **AD-098.** The source-is-short check compared every link in the plan against a single
  season's expected count, which on a five-season run printed "this source lists 61
  episode(s) but the databases expect 5 for season 1". It now reports season by season, and
  the season that really was short is named.

**AD-100, found while checking the fix against the other four seasons.** Matching on the
title alone was too strict in two ways, and would have made seasons 4 and 5 worse than
position had:

- A database spells an episode differently from the site: `alex-vs-ultimate-fruits` against
  "Alex vs Fruit", `alex-vs-toc-winners` against "Alex vs Tournament of Champions Winners".
  Neither matched, and both would have been pushed past the real episodes. Where such a link
  sits between two episodes that did match and exactly one number is free between them, that
  number is what it is - the listing itself is the evidence. Two free numbers is a guess, so
  it is not taken, and a link past the last recognised episode has nothing anchoring it
  above, which is exactly where a listing keeps its extras.
- The trust threshold was measured against the link count. Season 5 carries eight episodes
  and eight alternate cuts, so it can never match more than half its links however well it
  is read - six of sixteen is 37%, and the season went back to positional numbering. It is
  now measured against whichever is smaller, the links or the database episodes.

Checked against all five seasons of the real run afterwards: every season resolves
correctly, and the two episodes Food Network genuinely does not carry - S03E04 Alex vs
Southern Comfort and S05E08 Alex vs Pastry - are reported rather than papered over.

Worth remembering: the first answer here - "the source is short, not a defect" - was wrong,
and looked right because the file count matched the link count. Both numbers were consistent
and both described the wrong thing.

### A season tab that carries another season

Be My Guest with Ina Garten, seven seasons. The run read 40 links, dropped 10 repeats, and
then looked up titles for seasons 1 to 6 and never mentioned season 7.

Food Network lists season 7's four episodes under the season 1 tab as well. Deduplication
kept the earliest season, so Allison Janney, Jon Batiste, Hoda Kotb and Michael Barbaro
became season 1 episodes 5 to 8 - and season 7, having nothing left, was never looked up at
all. The numbers account for themselves exactly: one tile repeated into all seven tabs (six
dropped) plus those four appearing twice (four dropped) is the ten, and season 1 holding
eight links for a four-episode season is the rest of it.

**AD-101.** Keeping the latest season instead would break Alex vs America, where a
promotional tile really is repeated into every tab and belongs to season 1. Neither position
rule is right. The repeat is still dropped, but every season that carried it is remembered,
those seasons' titles are looked up, and the link goes to the one season whose episode list
names it. Exactly one, or it stays where it was: two would be a guess, and none means there
is nothing better on offer.

That last case is not hypothetical here. The databases spell episode one "Julianna
Marguiles" against the site's `julianna-margulies`, so nothing recognises it and it stays
under season 1 - which is where it belongs anyway. AD-100's gap filling gave it E01.

### A URL path is not an episode title

The same Be My Guest run produced

    S01E05 - _video_be-my-guest-with-ina-garten-food-network-atve-us_jon-batiste.mp4

**AD-102.** When the databases do not cover a link - an extra, a special, an alternate cut -
its own link text is used instead, which is right. But the scraper does not always find
anchor text, and the link text is then the URL path; used whole it becomes the filename
above. The last segment of the path is the episode's name in every case seen so far, so it
is used and title-cased: "Jon Batiste". Taken from the path proper, so a bare host gives
nothing rather than "Example.com", and small words stay lower unless they open the name.

AD-101 happens to hide this one for this show - those four links move to season 7, where the
databases do name them - but any genuine extra would still have been named after a URL.

### Where later work overlaps

- **AD-035** (the original "NA" folder naming bug) and **AD-001** are the same symptom five
  versions apart, from different causes. Worth remembering that this failure mode recurs.
- **AD-045** is largely addressed by **AD-003** and the yt-dlp probe step, but is left open
  until confirmed end to end.
- **AD-047** is satisfied in substance by **AD-015**: the package is vendored into the
  repository rather than published to a private feed.
- **AD-048** is superseded by **AD-023**.
- **AD-049** is substantially delivered by **AD-021**; what remains is **AD-027** and **AD-030**.
