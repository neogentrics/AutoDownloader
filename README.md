# AutoDownloader (v1.9.0-alpha Branch)

**This is an unstable development branch. Do not use for production.**

This branch contains the work-in-progress implementation of the **Multi-Scraper Engine**, which introduces **The TV Database (TVDB)** as a fallback metadata provider.

## 🎯 Current Goal: TVDB Fallback Strategy

The strategy for this branch has been updated:
1.  Search TMDB for metadata.
2.  If the search fails, a **pop-up** will ask the user: "Search TVDB instead? (Recommended for Anime)".
3.  If "Yes," the app will query TVDB.
4.  If "No" or TVDB fails, the download will terminate gracefully (fixing the v1.8.1 crash bug).

## ⚠️ Current Build Status (Known Issues)

**This branch WILL NOT COMPILE.** The `MetadataService.cs` file contains compilation errors as we work to integrate the `TvDbSharper` v4 API.

* **CS0234:** The type or namespace name 'Clients' does not exist in the namespace 'TvDbSharper'.
* **CS1061:** `TvDbClient` does not contain a definition for `AuthenticateAsync`, `SearchAsync`, etc.

These errors are known and will be fixed in the next commit on this branch.
