# AutoDownloader

An intelligent, high-speed, and automated WPF application for downloading Movies and TV Shows. AutoDownloader is designed for personal media organization and archival, utilizing the power of `yt-dlp`, `aria2c`, and Google's Gemini API for seamless content management.

## 🚀 Key Features of v1.7.5

* **Intelligent Naming (v1.7):** Automatically uses **The Movie Database (TMDB)** to retrieve official show titles, guaranteeing correct folder names (eliminating the "NA" bug) and standardized episode numbering.
* **Multi-Link Batch Processing (v1.6):** Supports sequential processing of multiple URLs or search terms entered line-by-line.
* **Smart Search:** Enter a show title instead of a URL. The Gemini-powered service finds a supported streaming link.
* **High-Speed Downloads:** Uses **aria2c** for segmented, high-speed, parallel downloads.
* **Robust Stability:** The 'Stop Download' function immediately terminates all processes without freezing the application.

## ⚠️ Known Issue in v1.7.5

| Component | Issue | Status |
| :--- | :--- | :--- |
| **Playlist Extraction** | Downloads are limited to 20 items/episodes on some series pages (e.g., Tubi), even if more are available. | Deferred to v1.8 for specific `yt-dlp` flag research. |

## 🛠️ Requirements

* **Windows 10/11:** This is a WPF application.
* **.NET 9.0 (or later)**
* **TMDB API Key:** Required for Metadata Integration.
* **Gemini API Key:** Required for the Smart Search feature.

## 📥 Getting Started

### Installation

1.  **Clone the Repository:**
    ```bash
    git clone [https://github.com/neogentrics/AutoDownloader.git](https://github.com/neogentrics/AutoDownloader.git)
    ```
2.  **Open in Visual Studio:** Open the `AutoDownloader.sln` solution file.
3.  **Build:** Build the solution to ensure all NuGet packages are restored.
4.  **Run:** Launch the `AutoDownloader.UI` project.

### How to Use

1.  **Select Mode:** Choose between single-link mode or use the **Toggle Multi-Link** button for batch processing.
2.  **Enter Input:** Input a direct video URL or the name of a Movie/TV Show.
3.  **Select Output Folder:** Use the **Browse...** button to set your desired base download location.
4.  **Click Download:** The app will automatically find the best source (if a search term was used), fetch the official TMDB title, and begin downloading.
5.  **Output Structure:** Files are saved as: `/TV Shows/Official Show Name/Season 01/Official Show Name - s01e01 - Episode Title.mp4`

## 🔮 Future Development (v1.8 Roadmap)

The next major release, **v1.8**, will focus on verifying and managing content integrity:

1.  **Content Verification:** Compare the actual number of downloaded episodes against the official episode count retrieved from TMDB to identify and flag missing files.
2.  **Settings System:** Implement a dedicated settings UI and backend configuration file (e.g., for default quality, API keys, and download limits).
3.  **Playlist Limit Fix:** Deep research and implementation of the correct `yt-dlp` flag to overcome the 20-item playlist limitation.

---

You are all set to commit your changes and push the **v1.7.5** release! Let me know when you are ready to start planning the code for **v1.8**!