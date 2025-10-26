AutoDownloader v1.0

This is a WPF desktop application for finding and downloading video content. It acts as a powerful, user-friendly frontend for the yt-dlp command-line tool, and adds a Gemini-powered "Smart Search" to find content across the web.

Core Features (v1.0)

Smart Search: Type a show name (e.g., "Knight Rider") and the app uses the Gemini API to find a valid download URL from a list of high-quality, legal sources.

Direct URL Download: Paste any URL (e.g., from YouTube) and the app will download it.

Self-Updating Engine: The app automatically downloads the latest nightly build of yt-dlp.exe on first run (or if it's older than 24 hours), ensuring it's always up-to-date.

Plex-Friendly Naming: Automatically sorts downloads into a clean, Plex-compliant folder structure: Category/Show Name/Season XX/Show - sXXeXX - Title.mp4.

Modern UI: A clean, styled WPF interface with a real-time log output.

Setup & Running

This is a Visual Studio 2022 Solution with two projects: AutoDownloader.Core (the "engine") and AutoDownloader.UI (the "dashboard").

1. External Dependencies

This project relies on FFmpeg for merging video and audio files (which most modern web videos require).

You must download FFmpeg (get the ffmpeg-release-full.zip) and add ffmpeg.exe to your system's PATH.

The application will not function without FFmpeg being accessible.

2. Running from Visual Studio

Clone this repository.

Open AutoDownloader.sln in Visual Studio 2022.

Right-click the AutoDownloader.UI project and select Manage NuGet Packages....

Browse for and install Microsoft.WindowsAPICodePack-Shell.

Set AutoDownloader.UI as the Startup Project.

Press Start (F5).

On first run, the app will automatically download yt-dlp.exe to the output directory (bin/Debug/...).