using System.Globalization;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class OverpassQueryTests
{
    private static readonly GeoBox BostonBox = new(South: 42.15765, West: -71.32823, North: 42.55335, East: -70.79277);
    private const string BostonArea = "(42.15765,-71.32823,42.55335,-70.79277)";

    [Fact]
    public void AsksForCoastlineAndWaterInsideTheBoxWithGeometry()
    {
        string query = OverpassQuery.Water(BostonBox);

        Assert.Contains($"way[\"natural\"=\"coastline\"]{BostonArea};", query);
        Assert.Contains($"way[\"natural\"=\"water\"]{BostonArea};", query);
        Assert.Contains($"relation[\"natural\"=\"water\"]{BostonArea};", query);
        Assert.Contains($"way[\"waterway\"=\"riverbank\"]{BostonArea};", query);
        Assert.Contains($"relation[\"waterway\"=\"riverbank\"]{BostonArea};", query);
        // Overpass gives up after 25 s unless told otherwise, and a city's water takes longer.
        Assert.Contains("[out:json][timeout:180];", query);
        Assert.Contains("out geom;", query);
    }

    [Fact]
    public void WritesCoordinatesWithDecimalPointsWhateverTheCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // German writes 42,15765. Overpass would read that as two numbers.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Contains(BostonArea, OverpassQuery.Water(BostonBox));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
