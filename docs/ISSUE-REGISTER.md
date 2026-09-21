# Issue Register

The allocation table for `AD-` identifiers. See [ISSUE-CONVENTIONS.md](ISSUE-CONVENTIONS.md)
for the format and labelling rules.

**Next free ID: `AD-031`**

| ID | Title | Type | Priority | Status |
| :--- | :--- | :--- | :--- | :--- |
| AD-001 | Output template collapses every episode to one filename on sites without series metadata | bug | critical | Fixed |
| AD-002 | Parsed season number is discarded; metadata always looked up season 1 | bug | critical | Fixed |
| AD-003 | Only the first scraped link is downloaded; the rest of the season is discarded | bug | critical | Fixed |
| AD-004 | Playwright is used as a blanket fallback for every unknown host | bug | high | Fixed |
| AD-005 | Hardcoded `--cookies-from-browser firefox` aborts every download without Firefox | bug | critical | Fixed |
| AD-006 | `SanitizeFileName` is never called; a title containing `:` throws on folder creation | bug | high | Fixed |
| AD-007 | ffmpeg is never provisioned although the default format requires merging | bug | critical | Fixed |
| AD-008 | Verification counts a season folder yt-dlp never wrote | bug | high | Fixed |
| AD-009 | First run with no API key crashes: `ShowDialog` on a window closed in its constructor | bug | critical | Fixed |
| AD-010 | Batch loop aborts remaining items and misreports them as user cancellation | bug | high | Fixed |
| AD-011 | `XmlService` discards edits when the existing file has a different root element | bug | medium | Fixed |
| AD-012 | `SearchService` retry reuses consumed `HttpContent`, so the 429 backoff never succeeds | bug | medium | Fixed |
| AD-013 | `InitializeAsyncServices` is unguarded; the UI locks forever if tool download fails | bug | high | Fixed |
| AD-014 | `ScraperFactory` matches `wcoanimesub` but not `wcoanimedub` | bug | medium | Fixed |
| AD-015 | The solution cannot restore on any machine except the original developer's | bug | critical | Fixed |
| AD-016 | `ci-playwright-install.yml` is not valid YAML and has never run | bug | high | Fixed |
| AD-017 | `dotnet test` runs no tests; the test project is absent from the solution | bug | medium | Fixed |
| AD-018 | Subtitles are never converted (`--sub-format` expresses a preference, not a conversion) | bug | medium | Fixed |
| AD-019 | Blocking `Dispatcher.Invoke` per log line stalls the UI during a download | bug | medium | Fixed |
| AD-020 | AngleSharp advisory and .NET Framework packages shimmed into net9 | task | medium | Fixed |
| AD-021 | Download pipeline is trapped in the window code-behind; no headless path | task | high | Fixed |
| AD-022 | `DeveloperLogger` queue grows without bound; `DrainAll` is never called | bug | medium | Open |
| AD-023 | TVDB access is ~200 lines of reflection that swallows every failure | task | high | Open |
| AD-024 | `MaxConcurrentDownloads` setting is never read | bug | low | Open |
| AD-025 | Orphaned `PlaywrightTests` stub project containing one empty test | task | low | Open |
| AD-026 | Preferences window exposes none of the new download settings | feature | high | Open |
| AD-027 | No progress bar, percentage, speed or ETA during downloads | feature | high | Open |
| AD-028 | GitHub Pages publishes the entire repository root | task | low | Open |
| AD-029 | README and project page describe features that do not match the code | task | medium | Fixed |
| AD-030 | No headless CLI entry point for unattended runs | feature | high | Open |
