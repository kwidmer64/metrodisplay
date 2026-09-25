using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class ExtentRectangleTests
{
    [Fact]
    public void EnclosesEveryPoint()
    {
        ExtentRectangle bounds = ExtentRectangle.Enclosing(
            [new PlanePoint(10, 20), new PlanePoint(-5, 40), new PlanePoint(30, 0)]);

        Assert.Equal(new ExtentRectangle(MinX: -5, MinY: 0, MaxX: 30, MaxY: 40), bounds);
    }

    [Fact]
    public void EnclosesPointsThatAllLieOnOneSideOfTheOrigin()
    {
        // Real networks sit millions of metres from the origin: Boston west and north of it,
        // Sydney east and south. Bounds seeded with 0 instead of infinity would stretch to reach it.
        ExtentRectangle boston = ExtentRectangle.Enclosing(
            [new PlanePoint(-7915000, 5210000), new PlanePoint(-7905000, 5225000)]);
        ExtentRectangle sydney = ExtentRectangle.Enclosing(
            [new PlanePoint(16820000, -4020000), new PlanePoint(16830000, -4010000)]);

        Assert.Equal(new ExtentRectangle(MinX: -7915000, MinY: 5210000, MaxX: -7905000, MaxY: 5225000), boston);
        Assert.Equal(new ExtentRectangle(MinX: 16820000, MinY: -4020000, MaxX: 16830000, MaxY: -4010000), sydney);
    }

    [Fact]
    public void ReportsWidthOverHeightAsAspect()
    {
        var bounds = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

        Assert.Equal(2.0, bounds.Aspect);
        Assert.Equal(200, bounds.LongestSpan);
    }

    [Fact]
    public void RefusesToBoundAnEmptySetOfPoints()
    {
        Assert.Throws<ArgumentException>(() => ExtentRectangle.Enclosing([]));
    }
}
