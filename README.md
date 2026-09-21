# AutoDownloader

A Windows desktop application for building a clean, Plex-ready personal media library from
sources you are entitled to download. It wraps `yt-dlp` and `aria2c`, identifies the series
against TMDB and TVDB, and files each episode under a correct, consistent name.

**Platform:** Windows 10/11 · **Framework:** .NET 9 (WPF) · **Status:** beta

> **Use responsibly.** This is an automation front-end for `yt-dlp`. Point it only at content
> you have the right to download. It deliberately contains no DRM circumvention.

---

## What it does

Give it a series URL, or just a show name, and it will:

1. **Resolve** — a plain show name is turned into a source page via a Gemini web search;
   a URL is used directly, with the show name and season parsed out of the path.
2. **Identify** — the series is looked up in **both** TMDB and TVDB, and the two episode
   lists are merged. Neither database is complete on its own: TVDB tends to be better for
   anime and irregular season splits, TMDB for mainstream television.
3. **Enumerate** — `yt-dlp` is asked what the page contains. If it recognises a playlist,
   that is used. If not, the page is indexed for episode links, falling back to a headless
   browser when the listing is built by JavaScript.
4. **Download** — each episode is fetched individually through `yt-dlp` with `aria2c` as the
   transfer engine, and written to a filename decided **before** the download starts.
5. **Verify** — the season folder is counted against the expected episode count and the
   result is reported.

### Why the naming is reliable

The show title and season come from the metadata databases, and the episode number comes from
the page — parsed from the link text, or failing that from its position in the listing. None
of it depends on the source site publishing usable metadata, which most do not:

```
TV Shows/
└── Phi Brain - Puzzle of God/
    ├── series_metadata.xml
    ├── downloaded.txt
    └── Season 01/
        ├── Phi Brain - Puzzle of God - S01E01 - The Puzzle Solver.mp4
        └── Phi Brain - Puzzle of God - S01E02 - Rook's Challenge.mp4
```

`downloaded.txt` is a `yt-dlp` archive, so re-running a season skips what is already present.

---

## Getting started

### Requirements

- Windows 10 or 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (to build from source)

`yt-dlp`, `aria2c` and `ffmpeg` are **downloaded automatically on first run**. If `ffmpeg` is
already on your `PATH`, that copy is used instead of fetching another ~85 MB.

### Build and run

```bash
git clone https://github.com/neogentrics/AutoDownloader.git
cd AutoDownloader
dotnet run --project AutoDownloader.UI
```

The repository includes a `NuGet.config` registering `vendor-packages/`, which holds a private
build of `TvDbSharper` that is not on nuget.org. No extra setup is needed.

### API keys

Set these under **Edit → Preferences** (`Ctrl+P`). All three are optional, but metadata naming
needs at least one of TMDB or TVDB.

| Key | Used for | Where to get one |
| :--- | :--- | :--- |
| TMDB | Series identification and episode titles | [themoviedb.org](https://www.themoviedb.org/settings/api) |
| TVDB | The same, merged with TMDB; better for anime | [thetvdb.com](https://thetvdb.com/api-information) |
| Gemini | "Smart search" from a plain show name | [aistudio.google.com](https://aistudio.google.com/app/apikey) |

Keys are stored in `%APPDATA%\AutoDownloader\settings.json` and are never committed.

### Settings worth knowing

| Setting | Default | Notes |
| :--- | :--- | :--- |
| `PreferredVideoQuality` | `bestvideo+bestaudio/best` | Any `yt-dlp` format string. Merging requires ffmpeg. |
| `CookieSource` | *(empty)* | A browser name (`firefox`, `chrome`, …) or a path to `cookies.txt`. **Leave empty unless needed** — `yt-dlp` treats an unreadable cookie source as fatal. |
| `AutoDownloadFfmpeg` | `true` | Fetch ffmpeg if none is found. |
| `UseDownloadArchive` | `true` | Skip episodes already recorded in `downloaded.txt`. |

---

## Unattended use

The same pipeline is available as a command-line tool, `autodl`, published alongside the
desktop app. It shares `settings.json`, so API keys configured in Preferences apply here too,
and it never waits for input.

```bash
autodl "https://example.com/series/some-show/season-2"
autodl "https://example.com/series/some-show" --season 3 --out "D:\Media"
autodl "The Mandalorian" --json --quiet
```

| Exit code | Meaning |
| :--- | :--- |
| 0 | Completed |
| 1 | Failed |
| 2 | Bad arguments |
| 3 | Nothing was downloaded |
| 130 | Cancelled (Ctrl+C) |

`--json` prints a machine-readable summary on stdout with logs kept on stderr, which is what
makes it usable from a scheduler or a webhook. Run `autodl --help` for the full option list.

In multi-link mode every item is identified first - page read, databases searched, and
any "which show is this?" asked - before a single byte is downloaded. That way the
questions happen while you are still at the keyboard instead of arriving forty minutes
into a run, and the downloads can be left alone. Answers last the whole batch, including
"keep all" and "replace all".

Episodes already on disk are kept, not re-downloaded. The desktop app asks what to do about
each one, with "keep all" and "replace all" so a season is one decision rather than twelve;
the CLI never asks, since nobody is there to answer. Pass `--overwrite` when you do want
existing files replaced — for a truncated download, or a re-run at a higher quality.

## Architecture

```
AutoDownloader.Core       Data models only (no logic, no dependencies)
AutoDownloader.Services   All logic. References no UI framework.
  Orchestration/          DownloadOrchestrator, IUserPrompt, UrlMetadataParser
  Scrapers/               SeriesIndexer, MediaUrlExtractor, per-site scrapers
AutoDownloader.UI         WPF. Wiring and presentation only.
AutoDownloader.Cli        Headless entry point (autodl). No UI dependency.
AutoDownloader.Tests      Pure-logic unit tests (no network, no browser)
```

`DownloadOrchestrator` owns the whole pipeline and references no WPF. Its single interactive
step goes through `IUserPrompt`, which has a WPF implementation and an `AutoConfirmPrompt` for
unattended use — so the same pipeline can be driven from a CLI or a scheduler without
duplicating anything.

### On extraction

The app does **not** try to find video URLs by pattern-matching page HTML. That only works when
the URL is written in the markup, which is true of most lecture and course pages but not of
streaming players, where the stream URL is assembled by JavaScript inside an iframe and is
usually an HLS manifest rather than a file.

So the order is: let `yt-dlp` resolve the page (it has extractors for well over a thousand
sites); if that fails, load the page in a headless browser and observe which media requests it
makes. A player cannot play without fetching the media, so that works regardless of how the URL
was constructed. See `MediaUrlExtractor`.

---

## Contributing

Issues use an `AD-###` identifier — see [docs/ISSUE-CONVENTIONS.md](docs/ISSUE-CONVENTIONS.md)
for the format, labels and branch naming, and [docs/ISSUE-REGISTER.md](docs/ISSUE-REGISTER.md)
for the allocation table.

```bash
dotnet build AutoDownloader.sln -c Release   # must be warning-free
dotnet test  AutoDownloader.sln -c Release
```

---

## Roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md) for what has shipped and what is planned.

## Licence and credits

Developer: **Neo Gentrics**. Built on [yt-dlp](https://github.com/yt-dlp/yt-dlp),
[aria2](https://aria2.github.io/), [FFmpeg](https://ffmpeg.org/),
[AngleSharp](https://anglesharp.github.io/), [Playwright](https://playwright.dev/),
[TMDbLib](https://github.com/LordMike/TMDbLib) and
[TvDbSharper](https://github.com/HristoKolev/TvDbSharper).

This product uses the TMDB and TVDB APIs but is not endorsed or certified by either.
