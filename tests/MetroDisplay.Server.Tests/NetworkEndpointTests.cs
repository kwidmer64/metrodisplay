using System.Text.Json;
using MetroDisplay.Gtfs.Static.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MetroDisplay.Server.Tests;

public sealed class NetworkEndpointTests : IDisposable
{
    private const string TestCityJson = """
        {
          "id": "test",
          "name": "TESTVILLE",
          "agency": "TT",
          "timezone": "America/New_York",
          "staticFeed": { "url": "https://example.test/gtfs.zip" },
          "realtime": {
            "vehiclePositions": { "url": "https://example.test/vp" },
            "alerts": { "url": "https://example.test/alerts" }
          },
          "routeTypes": [0, 1],
          "extent": { "core": [42.34, -71.08], "coreRadiusKm": 22 },
          "simplify": { "toleranceM": 25 },
          "dwellMs": 300000
        }
        """;

    private readonly TemporaryDirectory workspace = new();

    /// <summary>
    /// A Server pointed at a temp cities directory and a cache that already holds the fixture
    /// zip, so startup never touches the network.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory(string cityId, string cityJson = TestCityJson)
    {
        string citiesDirectory = Path.Combine(workspace.FullPath, "cities");
        string cacheDirectory = Path.Combine(workspace.FullPath, "cache");
        Directory.CreateDirectory(citiesDirectory);
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(citiesDirectory, "test.json"), cityJson);
        File.WriteAllBytes(Path.Combine(cacheDirectory, "test.zip"), GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        return new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("MetroDisplay:CityId", cityId)
            .UseSetting("MetroDisplay:CitiesDirectory", citiesDirectory)
            .UseSetting("MetroDisplay:FeedCacheDirectory", cacheDirectory));
    }

    [Fact]
    public async Task ServesTheSceneAsCamelCaseJson()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/network");

        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        Assert.StartsWith("test@", root.GetProperty("artifactVersion").GetString());
        Assert.Equal(new[] { "Green", "Red" }, root.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task ResolvesCityPlaceholdersFromConfiguration()
    {
        // A key set anywhere in configuration (user-secrets, app settings, environment variables)
        // fills a ${KEY} in the city config. Without it, startup would refuse the config.
        string keyedCityJson = TestCityJson.Replace(
            "\"url\": \"https://example.test/gtfs.zip\"",
            "\"url\": \"https://example.test/gtfs.zip\", \"headers\": { \"x-api-key\": \"${TEST_FEED_KEY}\" }");
        Assert.Contains("${TEST_FEED_KEY}", keyedCityJson);
        using WebApplicationFactory<Program> baseFactory = CreateFactory("test", keyedCityJson);
        using WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(host => host
            .UseSetting("TEST_FEED_KEY", "secret-value"));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/network");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void RefusesToStartWithoutItsCityConfig()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("missing");

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("missing.json", exception.ToString());
    }

    public void Dispose() => workspace.Dispose();
}
