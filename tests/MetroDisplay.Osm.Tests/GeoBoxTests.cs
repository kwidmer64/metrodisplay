using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class GeoBoxTests
{
    [Fact]
    public void ReachesTheRadiusOnTheGroundInEveryDirection()
    {
        // 22 km is 0.19785 degrees of latitude. At Boston's latitude a degree of longitude is
        // shorter by cos(42.3555), so the same 22 km is 0.26773 degrees of longitude.
        GeoBox box = GeoBox.Around(new GeoPoint(42.3555, -71.0605), radiusKm: 22);

        Assert.Equal(42.15765, box.South, precision: 5);
        Assert.Equal(42.55335, box.North, precision: 5);
        Assert.Equal(-71.32823, box.West, precision: 5);
        Assert.Equal(-70.79277, box.East, precision: 5);
        Assert.Equal(42.3555, box.CentreLatitude, precision: 5);
    }

    [Fact]
    public void IsSquareOnTheProjectedPlane()
    {
        ExtentRectangle plane = GeoBox.Around(new GeoPoint(42.3555, -71.0605), radiusKm: 22).ToPlane();

        Assert.Equal(1.0, plane.Aspect, precision: 3);
        // 44 km on the ground is about 59.5 km of plane distance at this latitude. The 100 m
        // allowance covers the two radii in play: the box is sized with the mean Earth radius,
        // and Web Mercator projects with the equatorial one, 0.11% larger (about 67 m here).
        Assert.Equal(44_000 / Math.Cos(42.3555 * Math.PI / 180), plane.Width, tolerance: 100);
    }
}
