using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads the GTFS files this system needs out of a zip. <c>stop_times.txt</c>, the bulk of a
/// typical archive, is never opened.
/// </summary>
public static class GtfsArchiveReader
{
    /// <summary>
    /// Header validation stays on, so a missing required column fails instead of reading as 0.
    /// Columns marked <c>[Optional]</c> are exempt.
    /// </summary>
    private static readonly CsvConfiguration CsvSettings = new(CultureInfo.InvariantCulture)
    {
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    /// <summary>
    /// Reads routes, trips and shapes. <c>shapes.txt</c> is optional in GTFS but required
    /// here: without it there is no track geometry to draw.
    /// </summary>
    /// <param name="zipBytes">The GTFS archive as downloaded.</param>
    /// <exception cref="InvalidDataException">A required file or column is missing. The message names it.</exception>
    public static GtfsArchive Read(byte[] zipBytes)
    {
        // Create a new memory stream instance, passing in the byte array of the zip file
        using (MemoryStream memStream = new MemoryStream(zipBytes))
        {
            // Create a ZipArchive instance from the previously created memory stream
            using (ZipArchive archive = new ZipArchive(memStream, ZipArchiveMode.Read))
            {
                List<GtfsRoute> routes = ReadRequired<GtfsRoute>(archive, "routes.txt");
                List<GtfsTrip> trips = ReadRequired<GtfsTrip>(archive, "trips.txt");
                List<GtfsShapePoint> shapePoints = ReadRequired<GtfsShapePoint>(archive, "shapes.txt");
                return new GtfsArchive(routes, trips, shapePoints);
            }
        }
    }

    private static List<TRecord> ReadRequired<TRecord>(ZipArchive archive, string entryName)
    {
        // Get the entry from the zip file, throw an exception if entry doesn't exist
        ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"GTFS archive has no {entryName}.");

        // Create a StreamReader with the contents of the entry
        using (StreamReader reader = new StreamReader(entry.Open()))
        {
            using (CsvReader csvReader = new CsvReader(reader, CsvSettings))
            {
                // Try to match the CSV columns to a TRecord object
                try
                {
                    return csvReader.GetRecords<TRecord>().ToList();
                }
                catch (HeaderValidationException exception)
                {
                    string missingColumns = string.Join(", ", exception.InvalidHeaders.SelectMany(header => header.Names));
                    throw new InvalidDataException($"{entryName} is missing required column(s): {missingColumns}.", exception);
                }
            }
        }
    }
}
