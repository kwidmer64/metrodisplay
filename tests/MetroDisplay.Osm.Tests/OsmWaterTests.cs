using NetTopologySuite.Geometries;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class OsmWaterTests
{
    private static Dictionary<string, string> Tags(string key, string value) => new() { [key] = value };

    private static List<OverpassPoint> Ring(double south, double west, double sizeDegrees) =>
    [
        new(south, west), new(south, west + sizeDegrees), new(south + sizeDegrees, west + sizeDegrees),
        new(south + sizeDegrees, west), new(south, west),
    ];

    private static OsmWater From(params OverpassElement[] elements) => OsmWater.From(new OverpassResponse(elements));

    [Fact]
    public void KeepsCoastlineApartFromInlandWater()
    {
        OsmWater water = From(
            new OverpassElement("way", Tags("natural", "coastline"), [new(-1, 0.05), new(1, 0.05)]),
            new OverpassElement("way", Tags("natural", "water"), Ring(0, 0, 0.002)));

        Assert.Single(water.CoastlineWays);
        Assert.Single(water.InlandWater);
    }

    [Fact]
    public void BuildsAPolygonFromAClosedWay()
    {
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), Ring(0, 0, 0.002)));

        // 0.002 degrees is 222.64 plane metres at the equator: 49,568 square metres.
        Assert.Equal(49568, Assert.Single(water.InlandWater).Area, tolerance: 1);
    }

    [Fact]
    public void JoinsARelationsSplitOutlineAndCutsItsIsland()
    {
        var relation = new OverpassElement("relation", Tags("natural", "water"), Points: null, Members:
        [
            new OverpassMember("way", "outer", [new(0.01, 0.01), new(0.01, 0.02), new(0.02, 0.02)]),
            new OverpassMember("way", "outer", [new(0.02, 0.02), new(0.02, 0.01), new(0.01, 0.01)]),
            new OverpassMember("way", "inner", Ring(0.014, 0.014, 0.002)),
            new OverpassMember("node", "label"),
        ]);

        Geometry area = Assert.Single(From(relation).InlandWater);

        Polygon polygon = Assert.IsType<Polygon>(area);
        Assert.Equal(1, polygon.NumInteriorRings);
    }

    [Fact]
    public void ReadsRiverbanksAsWater()
    {
        OsmWater water = From(new OverpassElement("way", Tags("waterway", "riverbank"), Ring(0, 0, 0.002)));

        Assert.Single(water.InlandWater);
    }

    [Fact]
    public void SkipsAWaterWayThatDoesNotClose()
    {
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), [new(0, 0), new(0, 0.002), new(0.002, 0.002), new(0.002, 0)]));

        Assert.Empty(water.InlandWater);
    }

    [Fact]
    public void SkipsAWaterWayTooShortToEncloseAnything()
    {
        // Out and back along one line: closed, but with no inside.
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), [new(0, 0), new(0, 0.002), new(0, 0)]));

        Assert.Empty(water.InlandWater);
    }

    [Fact]
    public void SkipsARelationWithNoOutline()
    {
        var relation = new OverpassElement("relation", Tags("natural", "water"), Points: null, Members:
        [
            new OverpassMember("way", "outer", [new(0.01, 0.01)]),
            new OverpassMember("node", "label"),
        ]);

        Assert.Empty(From(relation).InlandWater);
    }

    [Fact]
    public void RepairsAnOutlineThatCrossesItself()
    {
        // A bow tie: the outline crosses itself in the middle. Left as it is, clipping it fails.
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"),
            [new(0, 0), new(0.002, 0.002), new(0.002, 0), new(0, 0.002), new(0, 0)]));

        Geometry area = Assert.Single(water.InlandWater);
        Assert.True(area.IsValid);
        Assert.False(area.IsEmpty);
    }

    [Fact]
    public void IgnoresElementsThatAreNotWater()
    {
        OsmWater water = From(
            new OverpassElement("way", Tags("natural", "wood"), Ring(0, 0, 0.002)),
            new OverpassElement("way", Tags: null, Ring(0, 0, 0.002)));

        Assert.Empty(water.InlandWater);
        Assert.Empty(water.CoastlineWays);
    }
}
