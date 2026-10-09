using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class CoastlineCloserTests
{
    /// <summary>A 100 by 100 map, so areas read as percentages of ten thousand.</summary>
    private static readonly ExtentRectangle Frame = new(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

    private static IReadOnlyList<PlanePoint> Way(params (double X, double Y)[] points) =>
        points.Select(point => new PlanePoint(point.X, point.Y)).ToList();

    private static Point At(double planeX, double planeY) => new(planeX, planeY);

    [Fact]
    public void PutsTheSeaEastOfANorthboundCoast()
    {
        // Heading north, land is on the left, the west.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea([Way((50, -10), (50, 110))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(75, 50)));
    }

    [Fact]
    public void PutsTheSeaWestOfASouthboundCoast()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea([Way((50, 110), (50, -10))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(25, 50)));
    }

    [Fact]
    public void ClosesACoastSplitIntoWaysInAnyOrder()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, 40), (50, 110)), Way((50, -10), (50, 40))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(75, 50)));
    }

    [Fact]
    public void LeavesAnIslandAsAHoleInTheSea()
    {
        // A closed way running counter-clockwise keeps its land, the inside, on the left.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((40, 40), (60, 40), (60, 60), (40, 60), (40, 40))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(10000 - 400, area.Area, precision: 6);
        Assert.Equal(1, area.NumInteriorRings);
    }

    [Fact]
    public void FollowsAPeninsulaOutAndBack()
    {
        // Land to the west, with a 30 by 20 peninsula reaching east.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 40), (80, 40), (80, 60), (50, 60), (50, 110))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000 - 600, area.Area, precision: 6);
        Assert.False(area.Contains(At(65, 50)));
    }

    [Fact]
    public void FindsASeaOnEachSideOfALandBridge()
    {
        // Land between x = 30 and x = 70, sea to both sides.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((30, 110), (30, -10)), Way((70, -10), (70, 110))], Frame);

        Assert.Equal(2, sea.Count);
        Assert.Equal(3000 + 3000, sea.Sum(area => area.Area), precision: 6);
    }

    [Fact]
    public void TreatsAFaceNoProbeReachesAsLand()
    {
        // A second coast cuts the north-east corner, heading south-east with its land on the
        // corner side. Its one segment is so long that its midpoint lies outside the frame, so
        // it drops no probes and the corner gets no votes either way.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 110)), Way((60, 130), (260, -70))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000 - 50, area.Area, precision: 6);
        Assert.False(area.Contains(At(98, 98)));
    }

    [Fact]
    public void IgnoresAWayTooShortToBeALine()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way(), Way((20, 20)), Way((50, -10), (50, 110))], Frame);

        Assert.Equal(5000, Assert.Single(sea).Area, precision: 6);
    }

    [Fact]
    public void ToleratesAPointRepeatedAlongAWay()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 40), (50, 40), (50, 110))], Frame);

        Assert.Equal(5000, Assert.Single(sea).Area, precision: 6);
    }

    [Fact]
    public void FindsNoSeaWithoutACoastline()
    {
        Assert.Empty(CoastlineCloser.CloseSea([], Frame));
    }

    [Fact]
    public void FindsNoSeaWhenTheCoastlineMissesTheFrame()
    {
        Assert.Empty(CoastlineCloser.CloseSea([Way((500, -10), (500, 110))], Frame));
    }
}
