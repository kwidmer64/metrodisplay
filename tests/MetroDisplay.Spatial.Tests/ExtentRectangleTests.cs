using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Spatial.Tests;

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

    [Fact]
    public void IntersectsToTheAreaBothRectanglesCover()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var second = new ExtentRectangle(MinX: 60, MinY: -20, MaxX: 140, MaxY: 70);

        Assert.Equal(new ExtentRectangle(MinX: 60, MinY: 0, MaxX: 100, MaxY: 70), first.Intersect(second));
        Assert.Equal(first.Intersect(second), second.Intersect(first));
    }

    [Fact]
    public void IntersectsToTheSmallerRectangleWhenItLiesInsideTheOther()
    {
        var outer = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var inner = new ExtentRectangle(MinX: 20, MinY: 30, MaxX: 40, MaxY: 50);

        Assert.Equal(inner, outer.Intersect(inner));
        Assert.Equal(inner, inner.Intersect(outer));
    }

    [Fact]
    public void HasNoIntersectionWithARectangleItDoesNotReach()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

        Assert.Null(first.Intersect(new ExtentRectangle(MinX: 101, MinY: 0, MaxX: 200, MaxY: 100)));
        Assert.Null(first.Intersect(new ExtentRectangle(MinX: 0, MinY: -50, MaxX: 100, MaxY: -1)));
    }

    [Fact]
    public void IntersectsRectanglesThatShareOnlyAnEdgeInALineOfNoWidth()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var second = new ExtentRectangle(MinX: 100, MinY: 20, MaxX: 200, MaxY: 60);

        Assert.Equal(new ExtentRectangle(MinX: 100, MinY: 20, MaxX: 100, MaxY: 60), first.Intersect(second));
    }

    [Fact]
    public void ClampsAPointToTheNearestPointOfTheRectangle()
    {
        var rectangle = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 50);

        // Inside or on the edge: unchanged.
        Assert.Equal(new PlanePoint(40, 20), rectangle.Clamp(new PlanePoint(40, 20)));
        Assert.Equal(new PlanePoint(100, 50), rectangle.Clamp(new PlanePoint(100, 50)));
        // Past one edge: moved straight back onto it.
        Assert.Equal(new PlanePoint(100, 20), rectangle.Clamp(new PlanePoint(120, 20)));
        Assert.Equal(new PlanePoint(40, 0), rectangle.Clamp(new PlanePoint(40, -5)));
        // Past a corner: moved to the corner.
        Assert.Equal(new PlanePoint(0, 50), rectangle.Clamp(new PlanePoint(-5, 60)));
    }
}
