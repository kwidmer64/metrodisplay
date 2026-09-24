using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class MercatorProjectorTests
{
    [Fact]
    public void ProjectsTheOriginToZero()
    {
        PlanePoint projected = MercatorProjector.Project(new GeoPoint(0, 0));

        Assert.Equal(0, projected.X, precision: 6);
        Assert.Equal(0, projected.Y, precision: 6);
    }

    [Fact]
    public void ProjectsLongitudeLinearlyAlongTheEquator()
    {
        PlanePoint projected = MercatorProjector.Project(new GeoPoint(0, 180));

        // Half the equatorial circumference: pi * 6378137.
        Assert.Equal(20037508.34, projected.X, precision: 2);
    }

    [Fact]
    public void ProjectsNorthwardLatitudeToIncreasingY()
    {
        PlanePoint south = MercatorProjector.Project(new GeoPoint(42.32, -71.09));
        PlanePoint north = MercatorProjector.Project(new GeoPoint(42.36, -71.09));

        Assert.True(north.Y > south.Y, "Mercator Y grows northward; the screen flip happens later, in normalization.");
    }

    [Fact]
    public void StretchesNorthingWithLatitude()
    {
        // R * ln(tan(45 + 42 / 2)) in degrees. A plain R * latitude would give 4675418.61
        // and squash Boston vertically by cos(42), about a quarter.
        PlanePoint projected = MercatorProjector.Project(new GeoPoint(42, 0));

        Assert.Equal(5160979.44, projected.Y, precision: 2);
    }

    [Fact]
    public void ShrinksPlaneDistanceBackToGroundDistanceAwayFromTheEquator()
    {
        // Mercator stretches distance by 1/cos(latitude): about 1.346 at latitude 42.
        double groundMetres = MercatorProjector.PlaneMetresToGround(1000, latitudeDegrees: 42);

        Assert.Equal(1000 * Math.Cos(42 * Math.PI / 180), groundMetres, precision: 3);
    }

    [Fact]
    public void PlaneAndGroundDistanceAgreeAtTheEquator()
    {
        Assert.Equal(1000, MercatorProjector.PlaneMetresToGround(1000, latitudeDegrees: 0), precision: 6);
    }
}
