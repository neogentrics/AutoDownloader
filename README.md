# AutoDownloader Release: v1.9.0.1-alpha

**This is an unstable alpha (testing) release. Do not use for production.**

### Overview

This is the first alpha build from the `v1.9.0` development branch. It's ready for testing of the new **Multi-Scraper Engine**, which introduces **The TV Database (TVDB)** as a fallback metadata provider. It also bundles other recent stability improvements and bug fixes.

## ❗️ Important: Installation

To ensure full functionality, please use the **Installer .zip package**.

> **Note:** The standalone `.exe` file will **not** function correctly on its own, as it depends on accompanying files that are only included in the `.zip` archive.

## What's New in This Release

* **New Multi-Scraper Engine (TVDB Fallback):** This is the primary feature for testing. It's designed to fix the `v1.8.1` crash bug and improve metadata results, especially for Anime. The new strategy is:
    1.  The app first searches TMDB for metadata.
    2.  If the search fails, a **pop-up** will ask: "Search TVDB instead? (Recommended for Anime)".
    3.  If "Yes," the app queries TVDB.
    4.  If "No" or TVDB fails, the download terminates gracefully.

* **Metadata Naming Standardization:** Introduces a new, consistent naming convention for metadata.

* **Stability & UI Fixes:** Includes numerous background fixes to improve application stability and correct minor user interface issues.

## Known Issues

* The new metadata naming convention may not apply to files downloaded with a previous version.
* The "Search TVDB" pop-up may occasionally appear behind the main window.
* The UI may flicker when resizing the window on some systems.

## Feedback

Your feedback on this alpha release is essential! Please report any bugs or issues you encounter at:

[https://githubprojects.neogentrics.com/](https://githubprojects.neogentrics.com/)
