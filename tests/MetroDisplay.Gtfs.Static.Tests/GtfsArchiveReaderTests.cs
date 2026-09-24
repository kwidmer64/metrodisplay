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
}
