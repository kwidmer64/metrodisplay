using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads the GTFS files this system needs out of a zip. <c>stop_times.txt</c>, the bulk of a
/// typical archive and nearly all of it buses, is streamed and filtered instead of loaded.
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
    /// Reads routes, trips, shapes and stops. <c>shapes.txt</c> is optional in GTFS but
    /// required here: without it there is no track geometry to draw.
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
                List<GtfsStop> stops = ReadRequired<GtfsStop>(archive, "stops.txt");
                return new GtfsArchive(routes, trips, shapePoints, stops);
            }
        }
    }

    /// <summary>
    /// Reads the calls made by the wanted trips. Every other row is passed over without being
    /// kept, so memory holds the rail rows only.
    /// </summary>
    /// <param name="zipBytes">The GTFS archive as downloaded.</param>
    /// <param name="wantedTripIds">The trips whose calls to keep.</param>
    /// <returns>The kept rows, in file order. GTFS does not promise that order means anything.</returns>
    /// <exception cref="InvalidDataException">The file or a required column is missing, or a <c>stop_sequence</c> is not a whole number. The message names it.</exception>
    public static IReadOnlyList<GtfsStopTime> ReadStopTimes(byte[] zipBytes, IReadOnlySet<string> wantedTripIds)
    {
        const string entryName = "stop_times.txt";

        using (MemoryStream memStream = new MemoryStream(zipBytes))
        {
            using (ZipArchive archive = new ZipArchive(memStream, ZipArchiveMode.Read))
            {
                // look for the stop times text file in the zip
                ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"GTFS archive has no {entryName}.");

                using (StreamReader reader = new StreamReader(entry.Open()))
                {
                    using (CsvReader csvReader = new CsvReader(reader, CsvSettings)) // Read the text file as a csv
                    {
                        string[] requiredColumns = ["trip_id", "stop_id", "stop_sequence"];

                        // Attempt to read the next row and map the csv headers column names
                        // use the header record if there is one, else use an empty array
                        string[] header = csvReader.Read() && csvReader.ReadHeader() ? csvReader.HeaderRecord ?? [] : [];

                        // check if any of the required columns are missing
                        string[] missingColumns = requiredColumns.Where(column => !header.Contains(column, StringComparer.Ordinal)).ToArray();

                        // throw error if missing any columns
                        if (missingColumns.Length > 0)
                        {
                            throw new InvalidDataException($"{entryName} is missing required column(s): {string.Join(", ", missingColumns)}.");
                        }

                        // Fields are fetched by position
                        // Mapping each of millions of rows onto an object, to throw nine in ten away, costs several times as long.
                        int tripIdIndex = Array.IndexOf(header, "trip_id");
                        int stopIdIndex = Array.IndexOf(header, "stop_id");
                        int sequenceIndex = Array.IndexOf(header, "stop_sequence");

                        List<GtfsStopTime> stopTimes = new();

                        // read each stop into the list of stop times
                        while (csvReader.Read())
                        {
                            // get the trip Id field
                            string tripId = csvReader.GetField(tripIdIndex) ?? "";

                            // skip over record if the trip id is not wanted
                            if (!wantedTripIds.Contains(tripId))
                            {
                                continue;
                            }

                            // get the stop sequence
                            string sequenceText = csvReader.GetField(sequenceIndex) ?? "";

                            // check that every stop in a sequence is valid
                            if (!int.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence))
                            {
                                throw new InvalidDataException($"{entryName} has a stop_sequence that is not a whole number: '{sequenceText}' on trip '{tripId}'.");
                            }

                            stopTimes.Add(new GtfsStopTime(tripId, csvReader.GetField(stopIdIndex) ?? "", sequence));
                        }
                        return stopTimes;
                    }
                }
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
