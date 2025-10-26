# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows from supported streaming platforms. AutoDownloader uses the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content retrieval.

## 🚀 Key Features of v1.5

* **Smart Search:** Enter a show title or movie name instead of a URL. [cite_start]The Gemini-powered search service finds the best available streaming link (prioritizing high-quality, free sources like Tubi, Pluto TV, etc.)[cite: 5].
* [cite_start]**High-Speed Downloads:** Utilizes **aria2c** for segmented, high-speed, parallel downloads, maximizing your bandwidth[cite: 4, 5].
* **Intelligent Organization:** Automatically organizes content into `[Output Folder]/[Category]/[Show Name]/Season [Number]` based on extracted metadata.
* **Robust Naming:** Ensures correct show and episode naming conventions are followed, eliminating generic "NA" folders (Fixed in v1.5!).
* [cite_start]**Real-Time Logging:** Prevents UI freezing by using a high-performance batching system to display live download progress and status[cite: 3].
* **Reliable Termination:** The "Stop Download" function immediately and reliably terminates all download processes (Fixed in v1.5!).

## 🛠️ Requirements

* **Windows 10/11:** This is a WPF application targeting Windows.
* **.NET Framework 7.0 or later**
* **Gemini API Key:** Required for the Smart Search feature. (This is configured at runtime).

## 📥 Getting Started

### Installation

1.  **Clone the Repository:**
    ```bash
    git clone [https://github.com/neogentrics/AutoDownloader.git](https://github.com/neogentrics/AutoDownloader.git)
    ```
2.  **Open in Visual Studio:** Open the `AutoDownloader.sln` solution file.
3.  **Build:** Build the solution to automatically restore all NuGet packages.
4.  **Run:** Launch the `AutoDownloader.UI` project.

### How to Use

1.  **Enter Search Term or URL:** In the main text box, enter either a direct video URL or the name of a Movie/TV Show.
2.  **Select Output Folder:** Use the **Browse...** button to set your desired download location (e.g., `E:\Downloads`). [cite_start]The program will automatically create categorized subfolders (e.g., `TV Shows`, `Movies`)[cite: 3].
3.  **Click Download:**
    * **If a Search Term is entered:** The app uses the Gemini service to find the best URL, updates the output folder, and begins the download.
    * **If a URL is entered:** The download begins immediately.
4.  **Stop:** Click the **Stop Download** button to immediately terminate the process.

## ⚙️ Development Notes (v1.5)

This version is a stability patch. The `ToolManagerService` automatically ensures that the necessary command-line tools (`yt-dlp.exe` and `aria2c.exe`) are downloaded and kept up-to-date every 24 hours.

**Thank you for your feedback!** Future versions (v1.6+) will focus on implementing multi-link batch downloading and integrating external metadata services (TVDB/TMDB) for episode verification.

---