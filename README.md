# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows from supported streaming platforms. AutoDownloader uses the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content retrieval.

## 🚀 Key Features of v1.6.1 (Bugfix Release)

* **Multi-Link Batch Processing:** Process a list of URLs or search terms sequentially, one after the other.
* **Smart Search:** Enter a show title or movie name instead of a URL. The Gemini-powered search service finds the best available streaming link.
* **High-Speed Downloads:** Utilizes **aria2c** for segmented, high-speed, parallel downloads.
* **Intelligent Organization:** Automatically organizes content into `[Output Folder]/[Category]/[Show Name]/Season [Number]` based on extracted metadata.
* **Robust Stability:** The 'Stop Download' function now immediately and reliably terminates all processes without freezing the application.

## 🐛 Bug Fixes in v1.6.1

| Component | Issue | Resolution |
| :--- | :--- | :--- |
| **Download Stability** | App freezes or leaves 'ghost processes' after pressing Stop. | Resource cleanup (`Dispose`) logic was consolidated into the `Process.Exited` event, eliminating race conditions and freezing. |
| **UI** | Menu dropdowns are white with light text, making them unreadable in dark mode. | A dark theme style was explicitly applied to all `MenuItem` controls for improved contrast. |
| **Tooling** | Duplicate `await` in `YtDlpService` causing potential deadlocks. | Redundant `await _process.WaitForExitAsync()` call was removed. |
| **About Window**| Incorrect version displayed and poor formatting. | Logic was updated to dynamically show the correct version and content was redesigned for clarity. |

## 🛠️ Requirements

* **Windows 10/11:** This is a WPF application targeting Windows.
* **.NET 9.0 (or later)**
* **Gemini API Key:** Required for the Smart Search feature.

## 📥 Getting Started

### Installation

1.  **Clone the Repository:**
    ```bash
    git clone [https://github.com/neogentrics/AutoDownloader.git](https://github.com/neogentrics/AutoDownloader.git)
    ```
2.  **Open in Visual Studio:** Open the `AutoDownloader.sln` solution file.
3.  **Build:** Build the solution.
4.  **Run:** Launch the `AutoDownloader.UI` project.

### How to Use

1.  **Select Mode:** Use the **Toggle Multi-Link** button for batch processing, or leave it in default mode for single downloads.
2.  **Enter Search Term or URL:** Input a direct video URL or the name of a Movie/TV Show.
3.  **Select Output Folder:** Use the **Browse...** button to set your desired base download location.
4.  **Click Download:** The app will process the input(s) sequentially.
5.  **Stop:** Click the **Stop Download** button to immediately and reliably terminate the process.

## 💡 Future Development (v1.7 Roadmap)

The next major release, **v1.7**, will focus on improving content integrity and organization:

1.  **External Metadata Integration:** Integrate a metadata service (like TVDB/TMDB) to retrieve official show names, episode lists, and ensure accurate naming.
2.  **Missing Episode Detection:** Compare downloaded files against the official episode count to identify and flag missing content.
3.  **Settings System