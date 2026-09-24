using System.Text.Json;
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class CityConfigLoaderTests
{
    private const string SampleJson = """
    {
      "id": "mbta",
      "name": "BOSTON",
      "agency": "MBTA",
      "timezone": "America/New_York",
      "staticFeed": { "url": "https://example.test/MBTA_GTFS.zip" },
      "realtime": {
        "vehiclePositions": { "url": "https://example.test/vp",
                              "intervalMs": 15000,
                              "headers": { "x-api-key": "${MBTA_API_KEY}" } },
        "alerts": { "url": "https://example.test/alerts", "intervalMs": 60000 }
      },
      "routeTypes": [0, 1, 2],
      "routeFilter": { "exclude": ["CapeFlyer"] },
      "extent": { "core": [42.3555, -71.0605], "coreRadiusKm": 22 },
      "simplify": { "toleranceM": 25 },
      "dwellMs": 300000
    }
    """;

    private static Dictionary<string, string> SampleEnvironment() => new() { ["MBTA_API_KEY"] = "secret-value" };

    private static string? ApiKeyHeader(CityConfig config) => config.Realtime.VehiclePositions.Headers?["x-api-key"];

    [Fact]
    public void ResolvesEnvironmentPlaceholdersInHeaders()
    {
        CityConfig config = CityConfigLoader.Load(SampleJson, SampleEnvironment());

        Assert.Equal("secret-value", ApiKeyHeader(config));
    }

    [Fact]
    public void EscapesPlaceholderValuesSoTheyArriveUnchanged()
    {
        const string valueNeedingEscapes = "quote\"and\\backslash";
        var environment = new Dictionary<string, string> { ["MBTA_API_KEY"] = valueNeedingEscapes };

        CityConfig config = CityConfigLoader.Load(SampleJson, environment);

        Assert.Equal(valueNeedingEscapes, ApiKeyHeader(config));
    }

    [Fact]
    public void ThrowsWhenAPlaceholderHasNoEnvironmentValue()
    {
        var environment = new Dictionary<string, string>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CityConfigLoader.Load(SampleJson, environment));

        Assert.Contains("MBTA_API_KEY", exception.Message);
    }

    [Fact]
    public void ReadsNumericSettingsFromJson()
    {
        CityConfig config = CityConfigLoader.Load(SampleJson, SampleEnvironment());

        Assert.Equal(new[] { 42.3555, -71.0605 }, config.Extent.Core);
        Assert.Equal(22, config.Extent.CoreRadiusKm);
        Assert.Equal(25, config.Simplify.ToleranceM);
        Assert.Equal(300000, config.DwellMs);
        Assert.Equal(15000, config.Realtime.VehiclePositions.IntervalMs);
    }

    [Fact]
    public void RejectsAConfigMissingARequiredSetting()
    {
        string withoutRadius = SampleJson.Replace(", \"coreRadiusKm\": 22", "");
        Assert.DoesNotContain("coreRadiusKm", withoutRadius);

        var exception = Assert.Throws<JsonException>(
            () => CityConfigLoader.Load(withoutRadius, SampleEnvironment()));

        Assert.Contains("'coreRadiusKm'", exception.Message);
    }

    [Fact]
    public void RejectsAnUnknownSetting()
    {
        string withTypo = SampleJson.Replace("\"routeFilter\"", "\"routeFiltr\"");
        Assert.Contains("\"routeFiltr\"", withTypo);

        var exception = Assert.Throws<JsonException>(
            () => CityConfigLoader.Load(withTypo, SampleEnvironment()));

        Assert.Contains("routeFiltr", exception.Message);
    }

    [Fact]
    public void TreatsRouteFilterAsOptional()
    {
        string withoutFilter = SampleJson.Replace("\"routeFilter\": { \"exclude\": [\"CapeFlyer\"] },", "");
        Assert.DoesNotContain("routeFilter", withoutFilter);

        CityConfig config = CityConfigLoader.Load(withoutFilter, SampleEnvironment());

        Assert.Null(config.RouteFilter);
    }
}
