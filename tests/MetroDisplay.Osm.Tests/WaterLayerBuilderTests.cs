using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class WaterLayerBuilderTests
{
    /// <summary>A frame about 2.2 km across, centred on the equator.</summary>
    private static readonly ExtentRectangle Frame = new GeoBox(South: -0.01, West: -0.01, North: 0.01, East: 0.01).ToPlane();

    private static Dictionary<string, string> Water() => new() { ["natural"] = "water" };

    private static List<OverpassPoint> Ring(double south, double west, double sizeDegrees) =>
    [
        new(south, west), new(south, west + sizeDegrees), new(south + sizeDegrees, west + sizeDegrees),
        new(south + sizeDegrees, west), new(south, west),
    ];

    private static OverpassElement Lake(double south, double west, double sizeDegrees) => new("way", Water(), Ring(south, west, sizeDegrees));

    /// <summary>A 0.004 degree square lake whose south shore bulges outward at its middle.</summary>
    private static OverpassElement BumpedLake(double bumpDegrees) => new("way", Water(),
    [
        new(0, 0), new(-bumpDegrees, 0.002), new(0, 0.004), new(0.004, 0.004), new(0.004, 0), new(0, 0),
    ]);

    /// <summary>Builds with the rail frame equal to the clip frame unless a test says otherwise.</summary>
    private static IReadOnlyList<WaterArea> Build(ExtentRectangle railBounds, params OverpassElement[] elements) =>
        WaterLayerBuilder.Build(new OverpassResponse(elements), Frame, railBounds, latitudeDegrees: 0);

    private static List<double> XValues(WaterArea area) =>
        area.Rings.SelectMany(ring => ring.Where((_, index) => index % 2 == 0)).ToList();

    [Fact]
    public void KeepsWaterOfAtLeastTwoHectares()
    {
        // 4.96 ha stays; 1.24 ha is too small to read as water on screen.
        IReadOnlyList<WaterArea> water = Build(Frame, Lake(0, 0, 0.002), Lake(0.005, 0.005, 0.001));

        Assert.Single(water);
    }

    [Fact]
    public void ClipsWaterToTheFrame()
    {
        // The lake runs from 0.005 to 0.015 degrees east; the frame ends at 0.01.
        WaterArea area = Assert.Single(Build(Frame, Lake(0, 0.005, 0.01)));

        Assert.Equal(1.0, XValues(area).Max());
        Assert.Equal(0.75, XValues(area).Min());
    }

    [Fact]
    public void NormalizesInTheRailFrameEvenBeyondIt()
    {
        // The rail covers only the north-east quarter; the lake lies west of it.
        ExtentRectangle railBounds = new GeoBox(South: 0, West: 0, North: 0.01, East: 0.01).ToPlane();

        WaterArea area = Assert.Single(Build(railBounds, Lake(0, -0.008, 0.004)));

        Assert.Equal(-0.8, XValues(area).Min(), precision: 3);
        Assert.Equal(-0.4, XValues(area).Max(), precision: 3);
    }

    [Fact]
    public void KeepsAnIslandAsASecondRing()
    {
        var lakeWithIsland = new OverpassElement("relation", Water(), Points: null, Members:
        [
            new OverpassMember("way", "outer", Ring(-0.005, -0.005, 0.01)),
            new OverpassMember("way", "inner", Ring(-0.002, -0.002, 0.004)),
        ]);

        WaterArea area = Assert.Single(Build(Frame, lakeWithIsland));

        Assert.Equal(2, area.Rings.Count);
    }

    [Fact]
    public void LeavesRingsImplicitlyClosed()
    {
        IReadOnlyList<double> ring = Assert.Single(Build(Frame, Lake(0, 0, 0.002))).Rings[0];

        // Four corners, without the first repeated at the end.
        Assert.Equal(8, ring.Count);
        Assert.False(ring[0] == ring[^2] && ring[1] == ring[^1]);
    }

    [Fact]
    public void DropsABumpSmallerThanFifteenMetres()
    {
        // 0.00009 degrees is 10 m at the equator.
        WaterArea area = Assert.Single(Build(Frame, BumpedLake(bumpDegrees: 0.00009)));

        Assert.Equal(8, area.Rings[0].Count);
    }

    [Fact]
    public void KeepsABumpLargerThanFifteenMetres()
    {
        // 0.00036 degrees is 40 m at the equator.
        WaterArea area = Assert.Single(Build(Frame, BumpedLake(bumpDegrees: 0.00036)));

        Assert.Equal(10, area.Rings[0].Count);
    }

    [Fact]
    public void MeasuresTheSimplifyToleranceOnTheGround()
    {
        // At 60 degrees north the plane is stretched to twice the ground. A 20 m bump on the
        // plane is 10 m on the ground, under the tolerance, though it would stay at the equator.
        var response = new OverpassResponse([BumpedLake(bumpDegrees: 0.00018)]);

        Assert.Equal(10, Assert.Single(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 0)).Rings[0].Count);
        Assert.Equal(8, Assert.Single(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 60)).Rings[0].Count);
    }

    [Fact]
    public void MeasuresTheTwoHectaresOnTheGround()
    {
        // 4.96 ha on the plane is 1.24 ha on the ground at 60 degrees north, where areas are
        // stretched fourfold.
        var response = new OverpassResponse([Lake(0, 0, 0.002)]);

        Assert.Empty(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 60));
    }

    [Fact]
    public void FillsTheSeaBesideACoastline()
    {
        // A coast running north through the middle: land west, sea east.
        var coast = new OverpassElement("way", new Dictionary<string, string> { ["natural"] = "coastline" }, [new(-0.02, 0), new(0.02, 0)]);

        WaterArea sea = Assert.Single(Build(Frame, coast));

        Assert.Equal(0.5, XValues(sea).Min());
        Assert.Equal(1.0, XValues(sea).Max());
    }
}
