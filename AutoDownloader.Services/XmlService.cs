using System;
using System.IO;
using System.Xml.Linq;
using System.Threading.Tasks;
using AutoDownloader.Core; // <-- CORRECT: References the .Core project for DownloadMetadata
using System.Linq;

namespace AutoDownloader.Services // <-- CORRECTED: Now part of the Services project
{
    /// <summary>
    /// Handles reading and writing show metadata to a local "series_metadata.xml" file.
    /// This service is part of the v3.0 roadmap and will be used to track
    /// which seasons have already been downloaded for a show.
    /// </summary>
    public class XmlService
    {
        /// <summary>
        /// The standard filename for the metadata file, to be placed in the show's root folder.
        /// </summary>
        private const string XML_FILENAME = "series_metadata.xml";

        /// <summary>
        /// Asynchronously saves the provided metadata to an XML file in the series' root path.
        /// This method will create a new file or update an existing one.
        /// </summary>
        /// <param name="seriesRootPath">The root folder for the TV show (e.g., ".../TV Shows/The Mandalorian").</param>
        /// <param name="metadata">The metadata object (from TMDB/TVDB) to be saved.</param>
        public async Task SaveMetadataAsync(string seriesRootPath, DownloadMetadata metadata)
        {
            // ---1. Guard Clause ---
            // Don't save anything if the metadata lookup failed and we have no title.
            if (string.IsNullOrWhiteSpace(metadata.OfficialTitle)) return;

            // Define the full path to the XML file
            string filePath = Path.Combine(seriesRootPath, XML_FILENAME);

            XDocument doc;
            XElement root;

            // ---2. Load or Create the XML Document ---

            // Check if the file already exists
            if (File.Exists(filePath))
            {
                try
                {
                    // File exists, try to load it.
                    doc = XDocument.Load(filePath);
                    // Find the root element, or create a new one if the file is empty.
                    root = doc.Element("SeriesData") ?? new XElement("SeriesData");
                }
                catch
                {
                    // File exists but is corrupt (e.g.,0 bytes, malformed XML).
                    // Create a new, blank document structure in memory to overwrite it.
                    root = new XElement("SeriesData");
                    doc = new XDocument(root);
                }
            }
            else
            {
                // File does not exist, create a new XML structure in memory.
                root = new XElement("SeriesData");
                doc = new XDocument(root);
            }

            // ---3. Add or Update Series Info (once) ---

            // Ensure the main series info is only written once.
            if (root.Element("SeriesId") == null)
            {
                root.Add(new XElement("Title", metadata.OfficialTitle));
                root.Add(new XElement("SeriesId", metadata.SeriesId.GetValueOrDefault()));
                root.Add(new XElement("Source", "TMDB/TVDB")); // Placeholder for which DB we used
            }

            // ---4. Add or Update Season Info ---

            // Find an existing <Season> element where the "Number" attribute matches this season.
            var seasonElement = root.Elements("Season").FirstOrDefault(e =>
                (string?)e.Attribute("Number") == metadata.NextSeasonNumber.ToString());

            if (seasonElement == null)
            {
                // This is the first time we've downloaded this season. Create a new element.
                seasonElement = new XElement("Season",
                    new XAttribute("Number", metadata.NextSeasonNumber),
                    new XElement("ExpectedEpisodeCount", metadata.ExpectedEpisodeCount),
                    new XElement("DownloadDate", DateTime.UtcNow.ToString("yyyy-MM-dd"))
                );
                root.Add(seasonElement);
            }
            else
            {
                // This season already exists. Update its info (e.g., in case episode count changed).
                seasonElement.SetElementValue("ExpectedEpisodeCount", metadata.ExpectedEpisodeCount);
                seasonElement.SetElementValue("DownloadDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            }

            // ---5. Add Episode List (if available) ---
            // Remove existing Episodes element for this season to replace it with fresh data.
            var episodesNode = seasonElement.Element("Episodes");
            if (episodesNode != null) episodesNode.Remove();

            if (metadata.Episodes != null && metadata.Episodes.Any())
            {
                var eps = new XElement("Episodes");
                foreach (var ep in metadata.Episodes)
                {
                    var epEl = new XElement("Episode",
                        new XAttribute("Number", ep.EpisodeNumber),
                        new XElement("Title", ep.EpisodeTitle ?? string.Empty)
                    );
                    eps.Add(epEl);
                }
                seasonElement.Add(eps);
            }

            // ---6. Save the File ---

            // Save the document to disk.
            // We use Task.Run() to perform the blocking file I/O on a background thread,
            // which keeps the main UI thread responsive.
            await Task.Run(() => doc.Save(filePath));
        }
    }
}