using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class GtfsArchiveReaderTests
{
    [Fact]
    public void ReadsRoutesTripsAndShapePoints()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Equal(3, archive.Routes.Count);
        Assert.Equal(5, archive.Trips.Count);
        Assert.Equal(11, archive.ShapePoints.Count);
    }

    [Fact]
    public void MapsColumnsByHeaderNameNotPosition()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("routes.txt", """
                route_type,route_color,route_id,route_long_name
                1,DA291C,Red,Red Line
                """)
            .Build();

        GtfsRoute route = GtfsArchiveReader.Read(zipBytes).Routes.Single();

        Assert.Equal("Red", route.RouteId);
        Assert.Equal(1, route.RouteType);
        Assert.Equal("DA291C", route.RouteColor);
        Assert.Equal("", route.RouteShortName);
    }

    [Fact]
    public void KeepsCommasInsideQuotedFields()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Contains(archive.Routes, route => route.RouteLongName == "Green Line, B Branch");
    }

    [Fact]
    public void ReadsFilesThatStartWithAByteOrderMark()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build(withByteOrderMark: true);

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Equal("Red", archive.Routes[0].RouteId);
    }

    [Fact]
    public void NamesTheFileWhenARequiredFileIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("shapes.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("shapes.txt", exception.Message);
    }

    [Fact]
    public void NamesTheColumnWhenARequiredColumnIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("shapes.txt", """
                shape_id,shape_pt_lon,shape_pt_sequence
                shape-red,-71.0900,1
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("shapes.txt", exception.Message);
        Assert.Contains("shape_pt_lat", exception.Message);
    }

    [Fact]
    public void ReadsStops()
    {
        GtfsArchive archive = GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        Assert.Equal(14, archive.Stops.Count);
        GtfsStop platform = archive.Stops.Single(stop => stop.StopId == "mid-north");
        Assert.Equal("Midway", platform.StopName);
        Assert.Equal(42.3401, platform.Latitude);
        Assert.Equal(-71.0801, platform.Longitude);
        Assert.Equal("place-mid", platform.ParentStation);
    }

    [Fact]
    public void ReadsAStopWithNoPosition()
    {
        // Entrances and generic nodes carry no stop_lat or stop_lon. They must not fail the read.
        GtfsArchive archive = GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        GtfsStop entrance = archive.Stops.Single(stop => stop.StopId == "entrance-node");
        Assert.Null(entrance.Latitude);
        Assert.Null(entrance.Longitude);
        Assert.Equal("", entrance.StopName);
    }

    [Fact]
    public void NamesTheFileWhenStopsAreMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("stops.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("stops.txt", exception.Message);
    }

    [Fact]
    public void ReadsOnlyTheStopTimesOfWantedTrips()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();
        HashSet<string> wantedTripIds = ["red-north-1", "green-west-1"];

        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, wantedTripIds);

        Assert.Equal(7, stopTimes.Count);
        Assert.All(stopTimes, stopTime => Assert.Contains(stopTime.TripId, wantedTripIds));
        Assert.Contains(new GtfsStopTime("green-west-1", "cross-green", 2), stopTimes);
    }

    [Fact]
    public void MapsStopTimeColumnsByHeaderNameNotPosition()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                stop_sequence,pickup_type,stop_id,trip_id
                7,0,mid-north,red-north-1
                """)
            .Build();

        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" });

        Assert.Equal(new GtfsStopTime("red-north-1", "mid-north", 7), Assert.Single(stopTimes));
    }

    [Fact]
    public void NamesTheFileWhenStopTimesAreMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("stop_times.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
    }

    [Fact]
    public void NamesTheColumnWhenAStopTimesColumnIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                trip_id,stop_id
                red-north-1,mid-north
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
        Assert.Contains("stop_sequence", exception.Message);
    }

    [Fact]
    public void NamesTheValueWhenAStopSequenceIsNotANumber()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                red-north-1,mid-north,second
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
        Assert.Contains("'second'", exception.Message);
    }
}
