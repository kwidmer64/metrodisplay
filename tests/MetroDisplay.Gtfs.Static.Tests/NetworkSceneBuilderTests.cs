using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class NetworkSceneBuilderTests
{
    private static CityConfig TestConfig() => new(
        Id: "test",
        Name: "TESTVILLE",
        Agency: "TT",
        Timezone: "America/New_York",
        StaticFeed: new FeedSource("https://example.test/gtfs.zip"),
        Realtime: new RealtimeSources(
            new FeedSource("https://example.test/vp"),
            new FeedSource("https://example.test/alerts")),
        RouteTypes: [0, 1],
        Extent: new ExtentConfig([42.34, -71.08], CoreRadiusKm: 22),
        Simplify: new SimplifyConfig(ToleranceM: 25),
        DwellMs: 300000);

    private static NetworkScene Build(GtfsFixtureBuilder fixture) =>
        NetworkSceneBuilder.Build(fixture.Build(), TestConfig());

    private static IReadOnlyList<double> ShapePoints(NetworkScene scene, string shapeId) =>
        scene.Lines.SelectMany(line => line.Shapes).First(shape => shape.Id == shapeId).Points;

    [Fact]
    public void ProducesOneLinePerRailRouteAndNoBuses()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(new[] { "Green", "Red" }, scene.Lines.Select(line => line.Id));
    }

    [Fact]
    public void ExposesTheBoundsItsCoordinatesAreNormalizedIn()
    {
        // The fixture's rail runs 42.32..42.36 N and 71.12..71.06 W. Other layers are
        // normalized with these same bounds so they line up with the rail.
        RailLayer layer = NetworkSceneBuilder.BuildLayer(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), TestConfig());

        PlanePoint southWest = MercatorProjector.Project(new GeoPoint(42.32, -71.12));
        PlanePoint northEast = MercatorProjector.Project(new GeoPoint(42.36, -71.06));
        Assert.Equal(new ExtentRectangle(southWest.X, southWest.Y, northEast.X, northEast.Y), layer.Bounds);
        Assert.Empty(layer.Scene.Water);
    }

    [Fact]
    public void CarriesTheCityIdentityFromConfig()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(new CityMetadata("test", "TESTVILLE", "TT", "America/New_York"), scene.City);
    }

    [Fact]
    public void AppliesTheCitysRouteFilter()
    {
        CityConfig config = TestConfig() with { RouteFilter = new RouteFilter(Exclude: ["Green"]) };

        NetworkScene scene = NetworkSceneBuilder.Build(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), config);

        Assert.Equal(new[] { "Red" }, scene.Lines.Select(line => line.Id));
    }

    [Fact]
    public void FitsTheNetworkToItsOwnBounds()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        List<double> values = scene.Lines.SelectMany(line => line.Shapes).SelectMany(shape => shape.Points).ToList();
        List<double> xValues = values.Where((_, index) => index % 2 == 0).ToList();
        List<double> yValues = values.Where((_, index) => index % 2 == 1).ToList();

        // The fixture network is wider than it is tall, so X spans all of [0, 1].
        Assert.Equal(0.0, xValues.Min());
        Assert.Equal(1.0, xValues.Max());
        Assert.Equal(0.0, yValues.Min());
        Assert.InRange(yValues.Max(), 0.0, 1.0);
    }

    [Fact]
    public void OrdersShapePointsBySequenceNotFileOrder()
    {
        NetworkScene inOrder = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());
        NetworkScene shuffled = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3600,-71.0700,3
            shape-green,42.3540,-71.0600,3
            shape-red,42.3200,-71.0900,1
            shape-red-rev,42.3200,-71.0900,3
            shape-green,42.3500,-71.1200,1
            shape-red,42.3400,-71.0800,2
            shape-red-rev,42.3600,-71.0700,1
            shape-green,42.3520,-71.0900,2
            shape-red-rev,42.3400,-71.0800,2
            """));

        Assert.Equal(ShapePoints(inOrder, "shape-red"), ShapePoints(shuffled, "shape-red"));
    }

    [Theory]
    [InlineData("DA291C", "#DA291C")]
    [InlineData("00843d", "#00843D")]
    [InlineData("#ED8B00", "#ED8B00")]
    [InlineData("", NetworkSceneBuilder.FallbackColor)]
    [InlineData("red", NetworkSceneBuilder.FallbackColor)]
    public void FormatsRouteColorAsHashAndSixHexDigits(string routeColor, string expectedColor)
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", $"""
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,{routeColor}
            """));

        Assert.Equal(expectedColor, scene.Lines.Single().Color);
    }

    [Fact]
    public void SkipsTripsWhoseShapeIsNotInShapesTxt()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("trips.txt", """
            route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
            Red,weekday,red-north-1,Alewife,0,shape-red
            Red,weekday,red-ghost-1,Nowhere,0,shape-not-published
            Green,weekday,green-west-1,Boston College,0,shape-green
            """));

        LineScene red = scene.Lines.Single(line => line.Id == "Red");
        Assert.Equal(new[] { "shape-red" }, red.Shapes.Select(shape => shape.Id));
    }

    [Fact]
    public void LeavesOutRailRoutesThatHaveNoTrips()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,DA291C
            Green,B,"Green Line, B Branch",0,00843D
            Orange,,Orange Line,1,ED8B00
            """));

        Assert.Equal(new[] { "Green", "Red" }, scene.Lines.Select(line => line.Id));
    }

    [Fact]
    public void NamesLinesByLongNameThenShortName()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,RL,Red Line,1,DA291C
            Green,GL,,0,00843D
            """));

        Assert.Equal("Red Line", scene.Lines.Single(line => line.Id == "Red").Name);
        Assert.Equal("GL", scene.Lines.Single(line => line.Id == "Green").Name);
    }

    [Fact]
    public void MeasuresShapeLengthOnTheGround()
    {
        // 0.01 degrees along a meridian: pi * 6371008.8 / 180 / 100 = 1111.95 m.
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3000,-71.1000,1
            shape-red,42.3100,-71.1000,2
            """));

        ShapeGeometry shape = scene.Lines.Single(line => line.Id == "Red").Shapes.Single();
        Assert.Equal(1111.95, shape.LengthM, tolerance: 0.1);
    }

    [Fact]
    public void ReportsAspectAndSpanOfTheNetworkBounds()
    {
        // Bounds run 42.32..42.36 N and 71.12..71.06 W: 6679 x 6024 plane metres, and
        // the 6679 m long side is 4.94 km on the ground at the centre latitude 42.34.
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(1.1087, scene.Extent.Aspect);
        Assert.Equal(4.94, scene.Extent.SpanKm);
        Assert.Equal(22, scene.Extent.CoreRadiusKm);
    }

    [Fact]
    public void ReportsSpanAlongTheLongerAxisOfATallNetwork()
    {
        // Taller than wide, like MBTA: 1113 x 3011 plane metres. The span is the height,
        // 2.23 km on the ground; the width would give 0.82 km.
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3000,-71.1000,1
            shape-red,42.3200,-71.0900,2
            """));

        Assert.Equal(0.3698, scene.Extent.Aspect);
        Assert.Equal(2.23, scene.Extent.SpanKm);
    }

    [Fact]
    public void DerivesTheVersionFromTheZipContents()
    {
        byte[] original = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();
        byte[] recoloured = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,DA291D
            """).Build();

        string originalVersion = NetworkSceneBuilder.Build(original, TestConfig()).ArtifactVersion;

        Assert.Matches("^test@[0-9a-f]{8}$", originalVersion);
        Assert.Equal(originalVersion, NetworkSceneBuilder.Build(original, TestConfig()).ArtifactVersion);
        Assert.NotEqual(originalVersion, NetworkSceneBuilder.Build(recoloured, TestConfig()).ArtifactVersion);
    }

    [Fact]
    public void RefusesAFeedWithNoRailShapes()
    {
        GtfsFixtureBuilder busesOnly = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Bus7,7,Bus Route 7,3,FFC72C
            """);

        var exception = Assert.Throws<InvalidDataException>(() => Build(busesOnly));

        Assert.Contains("no rail shapes", exception.Message);
    }
}
