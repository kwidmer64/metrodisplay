using Xunit;

namespace MetroDisplay.Spatial.Tests;

public class PolylineClipperTests
{
    private static readonly ExtentRectangle Rectangle = new(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

    private static List<PlanePoint> Line(params (double X, double Y)[] points) =>
        points.Select(point => new PlanePoint(point.X, point.Y)).ToList();

    [Fact]
    public void ReturnsALineInsideUntouched()
    {
        // The repeated first point is how real feeds arrive. It must survive.
        List<PlanePoint> line = Line((10, 10), (10, 10), (50, 60), (90, 20));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Fact]
    public void DrawsNothingForALineOutside()
    {
        Assert.Empty(PolylineClipper.Clip(Line((-50, 10), (-10, 60), (-20, 120)), Rectangle));
        // Parallel to an edge and one unit beyond it.
        Assert.Empty(PolylineClipper.Clip(Line((20, 101), (80, 101)), Rectangle));
    }

    [Fact]
    public void CutsALineWhereItLeaves()
    {
        // Rising one unit for every two across, the line meets the east edge at y = 75.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((50, 50), (150, 100)), Rectangle));

        Assert.Equal(Line((50, 50), (100, 75)), run);
    }

    [Fact]
    public void CutsALineWhereItEnters()
    {
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((150, 50), (50, 50)), Rectangle));

        Assert.Equal(Line((100, 50), (50, 50)), run);
    }

    [Fact]
    public void CutsBothEndsOfALineThatCrossesTheWholeRectangle()
    {
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((-50, 30), (150, 30)), Rectangle));

        Assert.Equal(Line((0, 30), (100, 30)), run);
    }

    [Fact]
    public void GivesOneRunPerVisit()
    {
        // Out through the east edge, back in, out through the north edge, back in.
        List<PlanePoint> line = Line((50, 10), (150, 10), (150, 50), (50, 50), (50, 150), (90, 150), (90, 90));

        IReadOnlyList<IReadOnlyList<PlanePoint>> runs = PolylineClipper.Clip(line, Rectangle);

        Assert.Equal(3, runs.Count);
        Assert.Equal(Line((50, 10), (100, 10)), runs[0]);
        Assert.Equal(Line((100, 50), (50, 50), (50, 100)), runs[1]);
        Assert.Equal(Line((90, 100), (90, 90)), runs[2]);
    }

    [Fact]
    public void KeepsASegmentThatCrossesWithBothEndsOutside()
    {
        // Neither end is inside, but the segment cuts across the north-east corner.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((60, 120), (120, 60)), Rectangle));

        Assert.Equal(Line((80, 100), (100, 80)), run);
    }

    [Fact]
    public void DrawsNothingForALineThatOnlyTouchesACorner()
    {
        // Passes through (100, 100) and nowhere else in the rectangle.
        Assert.Empty(PolylineClipper.Clip(Line((90, 110), (110, 90)), Rectangle));
    }

    [Fact]
    public void KeepsOneRunWhenALineTurnsBackAtTheEdge()
    {
        List<PlanePoint> line = Line((50, 50), (100, 50), (50, 80));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Fact]
    public void EndsTheRunAtAVertexOnTheEdge()
    {
        // The second point sits exactly on the east edge and the line carries on outside.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((50, 50), (100, 50), (150, 50)), Rectangle));

        Assert.Equal(Line((50, 50), (100, 50)), run);
    }

    [Fact]
    public void KeepsALineThatRunsAlongTheEdge()
    {
        List<PlanePoint> line = Line((20, 100), (80, 100));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Fact]
    public void DrawsNothingForARepeatedPointThatOnlyTouchesTheEdge()
    {
        // Feeds repeat points. A repeat sitting on the edge, with the line outside on both
        // sides of it, is a touch and not a visit.
        Assert.Empty(PolylineClipper.Clip(Line((150, 50), (100, 50), (100, 50), (150, 50)), Rectangle));
        Assert.Empty(PolylineClipper.Clip(Line((90, 110), (100, 100), (100, 100), (110, 90)), Rectangle));
    }

    [Fact]
    public void DrawsNothingForALineOfNoLength()
    {
        Assert.Empty(PolylineClipper.Clip(Line((10, 10), (10, 10), (10, 10)), Rectangle));
    }

    [Fact]
    public void KeepsARepeatedPointOnTheEdgeWhenTheLineGoesOnInside()
    {
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((150, 50), (100, 50), (100, 50), (50, 50)), Rectangle));

        Assert.Equal(Line((100, 50), (100, 50), (50, 50)), run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DrawsNothingForFewerThanTwoPoints(int pointCount)
    {
        List<PlanePoint> points = Enumerable.Repeat(new PlanePoint(10, 10), pointCount).ToList();

        Assert.Empty(PolylineClipper.Clip(points, Rectangle));
    }
}
