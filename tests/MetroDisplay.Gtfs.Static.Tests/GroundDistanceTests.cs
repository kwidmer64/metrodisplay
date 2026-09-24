using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class GroundDistanceTests
{
    /// <summary>One degree of arc on the mean-radius sphere: pi * 6371008.8 / 180.</summary>
    private const double OneDegreeM = 111195.08;

    [Fact]
    public void MeasuresOneDegreeOfLatitude()
    {
        double distanceM = GroundDistance.BetweenM(new GeoPoint(0, 0), new GeoPoint(1, 0));

        Assert.Equal(OneDegreeM, distanceM, tolerance: 0.01);
    }

    [Fact]
    public void ShortensLongitudeAwayFromTheEquator()
    {
        // 0.01 degrees of longitude at Boston's latitude spans cos(42.35) of 1111.95 m.
        double distanceM = GroundDistance.BetweenM(new GeoPoint(42.35, -71.06), new GeoPoint(42.35, -71.05));

        Assert.Equal(821.78, distanceM, tolerance: 0.01);
    }

    [Fact]
    public void SumsTheSegmentsOfAPolyline()
    {
        GeoPoint[] alongTheEquator = [new(0, 0), new(0, 1), new(0, 2)];

        Assert.Equal(2 * OneDegreeM, GroundDistance.PolylineLengthM(alongTheEquator), tolerance: 0.02);
    }

    [Fact]
    public void GivesASinglePointNoLength()
    {
        Assert.Equal(0, GroundDistance.PolylineLengthM([new GeoPoint(42.35, -71.06)]));
    }
}
