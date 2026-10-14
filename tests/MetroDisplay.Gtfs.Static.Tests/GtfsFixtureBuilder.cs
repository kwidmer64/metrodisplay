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
    /// rail filter must drop. Green's long name contains a comma inside quotes. The two rail
    /// lines share one station, Crossing, and the bus calls at one of Red's platforms.
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
                """)
            .WithFile("stops.txt", """
                stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station
                place-south,South End,42.3200,-71.0900,1,
                south-platform,South End,42.3201,-71.0901,0,place-south
                place-mid,Midway,42.3400,-71.0800,1,
                mid-north,Midway,42.3401,-71.0801,0,place-mid
                mid-south,Midway,42.3399,-71.0799,0,place-mid
                place-cross,Crossing,42.3530,-71.0735,1,
                cross-red,Crossing,42.3531,-71.0735,0,place-cross
                cross-green,Crossing,42.3529,-71.0735,0,place-cross
                place-north,North End,42.3600,-71.0700,1,
                north-platform,North End,42.3601,-71.0701,0,place-north
                west-stop,West Side,42.3500,-71.1200,0,
                east-stop,East Side,42.3540,-71.0600,0,
                bus-stop,Bus Shelter,42.3000,-71.2000,0,
                entrance-node,,,,3,place-mid
                """)
            .WithFile("stop_times.txt", """
                trip_id,arrival_time,departure_time,stop_id,stop_sequence
                red-north-1,08:00:00,08:00:00,south-platform,1
                red-north-1,08:05:00,08:05:00,mid-north,2
                red-north-1,08:10:00,08:10:00,cross-red,3
                red-north-1,08:15:00,08:15:00,north-platform,4
                red-north-2,09:00:00,09:00:00,south-platform,1
                red-north-2,09:05:00,09:05:00,mid-north,2
                red-north-2,09:10:00,09:10:00,cross-red,3
                red-north-2,09:15:00,09:15:00,north-platform,4
                red-south-1,08:30:00,08:30:00,north-platform,1
                red-south-1,08:35:00,08:35:00,cross-red,2
                red-south-1,08:40:00,08:40:00,mid-south,3
                red-south-1,08:45:00,08:45:00,south-platform,4
                green-west-1,08:00:00,08:00:00,east-stop,1
                green-west-1,08:06:00,08:06:00,cross-green,2
                green-west-1,08:12:00,08:12:00,west-stop,3
                bus-1,08:00:00,08:00:00,bus-stop,1
                bus-1,08:20:00,08:20:00,mid-north,2
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
