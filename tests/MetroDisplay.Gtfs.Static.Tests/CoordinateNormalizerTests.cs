using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class CoordinateNormalizerTests
{
    private static readonly ExtentRectangle WideExtent = new(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

    [Fact]
    public void DividesBothAxesByTheLongestSpanSoNothingIsDistorted()
    {
        (double normalizedX, double normalizedY) = CoordinateNormalizer.Normalize(new PlanePoint(200, 0), WideExtent);

        Assert.Equal(1.0, normalizedX, precision: 6);
        Assert.Equal(0.5, normalizedY, precision: 6);
    }

    [Fact]
    public void NormalizesATallExtentAwayFromTheOrigin()
    {
        // Taller than wide, like MBTA, and offset from the origin, like every real network.
        // Height is the longest span, so it is the divisor on both axes.
        var tallExtent = new ExtentRectangle(MinX: 1000, MinY: 2000, MaxX: 1100, MaxY: 2200);

        (double normalizedX, double normalizedY) = CoordinateNormalizer.Normalize(new PlanePoint(1100, 2000), tallExtent);

        Assert.Equal(0.5, normalizedX, precision: 6);
        Assert.Equal(1.0, normalizedY, precision: 6);
    }

    [Fact]
    public void HandlesASinglePointWithoutDividingByZero()
    {
        // A zero-size extent must not produce NaN: JSON has no way to carry it.
        var point = new PlanePoint(5, 5);
        ExtentRectangle extent = ExtentRectangle.Enclosing([point]);

        Assert.Equal(1.0, extent.Aspect);
        Assert.Equal((0.0, 0.0), CoordinateNormalizer.Normalize(point, extent));
    }

    [Fact]
    public void FlipsYIntoScreenConvention()
    {
        (double _, double northY) = CoordinateNormalizer.Normalize(new PlanePoint(0, 100), WideExtent);
        (double _, double southY) = CoordinateNormalizer.Normalize(new PlanePoint(0, 0), WideExtent);

        Assert.Equal(0.0, northY, precision: 6);
        Assert.True(southY > northY, "A northern point must land nearer the top of the screen.");
    }

    [Fact]
    public void QuantizesToFourDecimalPlaces()
    {
        var extent = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 3, MaxY: 3);

        (double normalizedX, double _) = CoordinateNormalizer.Normalize(new PlanePoint(1, 0), extent);

        Assert.Equal(0.3333, normalizedX);
    }

    [Fact]
    public void FlattensPointsIntoInterleavedPairs()
    {
        IReadOnlyList<double> flattened = CoordinateNormalizer.Flatten(
            [new PlanePoint(0, 0), new PlanePoint(200, 100)], WideExtent);

        Assert.Equal(new[] { 0.0, 0.5, 1.0, 0.0 }, flattened);
    }
}
