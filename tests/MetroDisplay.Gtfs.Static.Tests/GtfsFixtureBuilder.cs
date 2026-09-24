using System.IO.Compression;
using System.Text;

namespace MetroDisplay.Gtfs.Static.Tests;

/// <summary>
/// Builds a GTFS zip in memory. A test reads as a description of a feed instead of depending
/// on a committed binary, and each test can vary one file.
/// </summary>
public sealed class GtfsFixtureBuilder
{
    private readonly Dictionary<string, string> filesByName = [];

    /// <summary>
    /// A subway line running both directions, a light rail line, and a bus route the
    /// rail filter must drop. Green's long name contains a comma inside quotes.
    /// </summary>
    public static GtfsFixtureBuilder TwoRailLinesAndABus()
    {
        return new GtfsFixtureBuilder()
            .WithFile("routes.txt", """
                route_id,route_short_name,route_long_name,route_type,route_color
                Red,,Red Line,1,DA291C
                Green,B,"Green Line, B Branch",0,00843D
                Bus7,7,Bus Route 7,3,FFC72C
                """)
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Red,weekday,red-north-1,Alewife,0,shape-red
                Red,weekday,red-north-2,Alewife,0,shape-red
                Red,weekday,red-south-1,Ashmont,1,shape-red-rev
                Green,weekday,green-west-1,Boston College,0,shape-green
                Bus7,weekday,bus-1,Nowhere,0,shape-bus
                """)
            .WithFile("shapes.txt", """
                shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
                shape-red,42.3200,-71.0900,1
                shape-red,42.3400,-71.0800,2
                shape-red,42.3600,-71.0700,3
                shape-red-rev,42.3600,-71.0700,1
                shape-red-rev,42.3400,-71.0800,2
                shape-red-rev,42.3200,-71.0900,3
                shape-green,42.3500,-71.1200,1
                shape-green,42.3520,-71.0900,2
                shape-green,42.3540,-71.0600,3
                shape-bus,42.3000,-71.2000,1
                shape-bus,42.3100,-71.1900,2
                """);
    }

    public GtfsFixtureBuilder WithFile(string name, string contents)
    {
        filesByName[name] = contents;
        return this;
    }

    public GtfsFixtureBuilder WithoutFile(string name)
    {
        filesByName.Remove(name);
        return this;
    }

    /// <param name="withByteOrderMark">Start every file with a UTF-8 byte order mark, as some agencies do.</param>
    public byte[] Build(bool withByteOrderMark = false)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: withByteOrderMark);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string contents) in filesByName)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), encoding);
                writer.Write(contents);
            }
        }
        return buffer.ToArray();
    }
}
