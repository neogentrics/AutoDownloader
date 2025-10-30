using System;
using System.IO;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace AutoDownloader.Core
{
    /// <summary>
    /// Handles saving show metadata to a local XML file for archival and checking downloaded seasons.
    /// </summary>
    public class XmlService
    {
        private const string XML_FILENAME = "series_metadata.xml";

        public async Task SaveMetadataAsync(string seriesRootPath, DownloadMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.OfficialTitle)) return;

            string filePath = Path.Combine(seriesRootPath, XML_FILENAME);

            XDocument doc;
            XElement root;

            // Check if the file already exists
            if (File.Exists(filePath))
            {
                try
                {
                    doc = XDocument.Load(filePath);
                    root = doc.Element("SeriesData") ?? new XElement("SeriesData");
                }
                catch
                {
                    // File exists but is corrupt, create new
                    root = new XElement("SeriesData");
                    doc = new XDocument(root);
                }
            }
            else
            {
                // Create a new XML structure
                root = new XElement("SeriesData");
                doc = new XDocument(root);
            }

            // Ensure unique Series ID is stored once
            if (root.Element("SeriesId") == null)
            {
                root.Add(new XElement("Title", metadata.OfficialTitle));
                root.Add(new XElement("SeriesId", metadata.SeriesId.GetValueOrDefault()));
                root.Add(new XElement("Source", "TMDB/TVDB"));
            }

            // Add or update the downloaded season information
            var seasonElement = root.Elements("Season").FirstOrDefault(e =>
                (string?)e.Attribute("Number") == metadata.NextSeasonNumber.ToString());

            if (seasonElement == null)
            {
                seasonElement = new XElement("Season",
                    new XAttribute("Number", metadata.NextSeasonNumber),
                    new XElement("ExpectedEpisodeCount", metadata.ExpectedEpisodeCount),
                    new XElement("DownloadDate", DateTime.UtcNow.ToString("yyyy-MM-dd"))
                );
                root.Add(seasonElement);
            }
            else
            {
                // If season entry exists, just update the expected count and date
                seasonElement.SetElementValue("ExpectedEpisodeCount", metadata.ExpectedEpisodeCount);
                seasonElement.SetElementValue("DownloadDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            }

            // Save the document asynchronously
            await Task.Run(() => doc.Save(filePath));
        }
    }
}