# AutoDownloader Alpha Release: v1.9.0-alpha

🚨 **This is an unstable, non-functional development build. 🚨**

This build is intended for development and debugging only. It contains numerous critical bugs, and core features are broken. **Do not use this for production or reliable downloading.**

### Overview of This Branch

This development branch was intended to integrate **The TV Database (TVDB)** as a new metadata provider alongside TMDB.

However, this integration **has failed**. The `TvDbSharper` (v4.0.10) NuGet package is outdated (last updated in 2022) and is not compatible with the project's **.NET 9.0 framework**, as it targets .NET 6.

As a result, this alpha is **highly unstable**. The new UI elements are present, but the underlying logic is non-functional.

---

## 🐛 Critical Bugs & Known Issues in v1.9.0-alpha

The following table lists the major issues identified in this build. Most of these bugs will cause the application to **crash, freeze, or produce incorrect output.**

| Status | Bug Description |
| :--- | :--- |
| 💥 **CRITICAL** | **TVDB Integration Failure:** The TVDB search functionality is completely non-functional. The `TvDbSharper` library is incompatible. The "Search TVDB" button/option is disabled or will fail if enabled. |
| 💥 **CRITICAL** | **Season Detection Failure:** The app cannot determine the correct season for a show. It defaults to Season 1. Attempting to download a different season (e.g., Season 2) will cause it to either re-download Season 1, place files in an "NA" folder, or crash the application. |
| 💥 **CRITICAL** | **Incorrect YouTube Playlist Parsing:** When given a YouTube playlist URL, the app incorrectly tries to parse the *URL* for the show name instead of the *on-page playlist title*. This results in a metadata lookup failure, and videos are downloaded to an "NA" folder with incorrect file names. |
| 🐛 **BUG** | **Faulty URL Parsing (Show Name):** The app is supposed to parse the *web page content* to find the series name. It fails to do this. Instead, it only parses the URL string itself. This breaks metadata lookups for any URL that doesn't have the show's name in the URL. |
| 🐛 **BUG** | **Faulty URL Parsing (Season Number):** The app is unable to read URLs to determine if the link is for a specific season (e.g., a "Season 2" page). This contributes to the Season Detection Failure. |

---

## 🏗️ New (But Broken) Features

* **Scraper Selection Pop-up:** A new UI pop-up was added. When a URL is entered, this pop-up asks the user to confirm the show name.
    * This pop-up *attempts* to auto-fill the name by parsing the URL, which (as noted above) often fails.
    * It also provides buttons to select either the TMDB or TVDB scraper. **Only the TMDB button works.**

## 🛣️ Future Roadmap (Post-Alpha)

The immediate priority is stabilizing the application. The following issues must be addressed before moving forward:

1.  **Fix Critical Bugs:** Address the app-breaking crashes related to season detection and URL parsing.
2.  **Replace or Fork TVDB Library:** The `TvDbSharper` library must be either replaced with a modern alternative or forked and upgraded to support .NET 9 and the current TVDB API.
3.  **Implement Page Content Scraping:** Rework the parsing logic to correctly scrape *page content* (HTML) for show titles and season information, rather than relying only on URL strings.
