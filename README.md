AutoDownloader v1.4
This is a WPF desktop application for finding and downloading video content. It acts as a powerful, user-friendly frontend for the yt-dlp command-line tool, adds a Gemini-powered "Smart Search" to find content, and integrates aria2c for high-speed, multi-threaded downloads.
Core Features (v1.4)
* Smart Search: Type a show name (e.g., "Knight Rider") and the app uses the Gemini API to find a valid, free download URL from a curated list of high-quality sources (like Tubi, YouTube, etc.).
* Automatic Categorization: The Smart Search classifies content as "Anime," "TV Show," or "Movie" and saves it to a corresponding subfolder.
* Direct URL Download: Paste any URL (e.g., from YouTube) and the app will download it.
* High-Speed, Multi-Threaded Downloads: Uses aria2c to download files with up to 16 parallel connections, making downloads significantly faster than the default.
* Self-Updating Engine: The app automatically downloads and manages all required tools:
o yt-dlp.exe (nightly build, checks for updates every 24 hours)
o aria2c.exe (downloaded on first run)
* Plex-Friendly Naming: Automatically sorts downloads into a clean, Plex-compliant folder structure: Category/Show Name/Season XX/Show - sXXeXX - Title.mp4.
* Modern & Responsive UI:
o A clean, styled WPF interface with a larger default window (1280x880).
o Features a "Download" button that swaps to a "Stop" button during operation.
o Uses high-performance "log batching" so the UI never freezes, even during rapid-fire log updates.
Setup & Running
This is a Visual Studio 2022 Solution with a clean, refactored structure:
* AutoDownloader.Core (The "engine" containing YtDlpService, SearchService, and ToolManagerService)
* AutoDownloader.UI (The WPF "dashboard")
1. External Dependencies (Manual Setup)
This project relies on FFmpeg for merging video and audio files (which most modern web videos require).
* You must install FFmpeg yourself.
* The easiest way is to download it (get the ffmpeg-release-full.zip from Gyan.dev) and add the bin folder (which contains ffmpeg.exe) to your system's PATH environment variable.
* The application will not function without ffmpeg.exe being accessible.
2. Running from Visual Studio
1. Clone this repository.
2. Open AutoDownloader.sln in Visual Studio 2022.
3. Right-click the AutoDownloader.UI project and select Manage NuGet Packages...
4. Browse for and install Microsoft.WindowsAPICodePack-Shell.
5. Set AutoDownloader.UI as the Startup Project.
6. Press Start (F5).
On first run, the app will automatically download yt-dlp.exe and aria2c.exe to the output directory (bin/Debug/...) and will be ready to use.

