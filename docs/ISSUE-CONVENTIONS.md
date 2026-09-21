# Issue and Tracking Conventions

This project uses a short, stable identifier for every tracked item so it can be referenced
from commits, branches, code comments, Notion and conversation without ambiguity.

## The ID format

```
AD-###
```

- `AD` — AutoDownloader. Fixed prefix for this repository.
- `###` — zero-padded sequential number, starting at `001`. Never reused, never renumbered.

The ID is **not** the GitHub issue number. GitHub issue numbers are assigned by GitHub and
already start from a different point in this repository's history; the `AD-` ID is ours and
travels with the item across GitHub, Notion and commit messages.

Every issue title therefore begins with its ID:

```
AD-014: ScraperFactory matches wcoanimesub but not wcoanimedub
```

## Allocating a new ID

Take the next unused number from [`ISSUE-REGISTER.md`](ISSUE-REGISTER.md) and add a row for it
in the same change that opens the issue. The register is the source of truth for which numbers
are taken.

## Labels

Every issue carries exactly one `type:`, one `priority:`, and one or more `area:` labels.

| Group | Labels | Meaning |
| :--- | :--- | :--- |
| Type | `type:bug` | Behaves incorrectly against its stated intent |
| | `type:feature` | New capability |
| | `type:task` | Chore, refactor, docs, infrastructure |
| | `type:research` | Unknown that needs investigating before it can be sized |
| Priority | `priority:critical` | Data loss, crash, or the app cannot do its core job |
| | `priority:high` | Core workflow is materially degraded |
| | `priority:medium` | Worth doing, has a workaround |
| | `priority:low` | Cosmetic or speculative |
| Area | `area:download` | yt-dlp, aria2c, ffmpeg, output templates |
| | `area:metadata` | TMDB, TVDB, episode lists, naming |
| | `area:scraping` | Indexing, extraction, Playwright |
| | `area:ui` | WPF windows, log, preferences |
| | `area:build` | NuGet, CI, packaging, release |
| | `area:docs` | README, project page, this folder |

## Referencing from commits

Put the ID in the commit subject when a commit closes or advances an item:

```
fix(download): stop the output template collapsing to a single filename (AD-001)
```

Use GitHub's own closing keywords in the body to link the issue itself, since the `AD-` ID is
not something GitHub resolves:

```
Closes #42
```

## Branch naming

```
<type>/AD-###-short-slug
```

For example: `fix/AD-001-output-template`, `feat/AD-030-headless-cli`.

Long-lived branches are avoided. A branch that has been merged should be deleted; a branch that
has not been merged in weeks should be closed or rebased rather than left to rot.

## Status

Status is tracked by GitHub issue state plus the Notion board, not by a label:

- **Open** — not started or in progress.
- **Closed** — merged and verified. The closing commit or PR is linked from the issue.

An item that turns out not to be a real problem is closed with a comment explaining why, not
deleted.
