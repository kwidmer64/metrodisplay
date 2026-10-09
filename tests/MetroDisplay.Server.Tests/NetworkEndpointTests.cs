using System.Net;
using System.Text.Json;
using MetroDisplay.Gtfs.Static.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>A pond of about 91 ha inside the fixture network's area.</summary>
    private const string PondLayerJson = """
        {"elements":[{"type":"way","id":1,"tags":{"natural":"water"},"geometry":[
          {"lat":42.33,"lon":-71.10},{"lat":42.33,"lon":-71.09},{"lat":42.34,"lon":-71.09},
          {"lat":42.34,"lon":-71.10},{"lat":42.33,"lon":-71.10}]}]}
        """;

    private const string EmptyLayerJson = """{"elements":[]}""";

    private readonly TemporaryDirectory workspace = new();

    /// <summary>
    /// A Server pointed at a temp cities directory and a cache that already holds the fixture
    /// zip, so startup never touches the network.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory(string cityId, string cityJson = TestCityJson, string? waterLayerJson = EmptyLayerJson)
    {
        string citiesDirectory = Path.Combine(workspace.FullPath, "cities");
        string cacheDirectory = Path.Combine(workspace.FullPath, "cache");
        string osmDirectory = Path.Combine(workspace.FullPath, "osm");
        Directory.CreateDirectory(citiesDirectory);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(osmDirectory);
        File.WriteAllText(Path.Combine(citiesDirectory, "test.json"), cityJson);
        File.WriteAllBytes(Path.Combine(cacheDirectory, "test.zip"), GtfsFixtureBuilder.TwoRailLinesAndABus().Build());
        // A cached layer keeps startup off the network. Null leaves the cache empty.
        if (waterLayerJson is not null)
        {
            File.WriteAllText(Path.Combine(osmDirectory, "test-water.json"), waterLayerJson);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("MetroDisplay:CityId", cityId)
            .UseSetting("MetroDisplay:CitiesDirectory", citiesDirectory)
            .UseSetting("MetroDisplay:FeedCacheDirectory", cacheDirectory)
            .UseSetting("MetroDisplay:OsmCacheDirectory", osmDirectory)
            // Nothing listens here, so a fetch fails at once instead of reaching the internet.
            .UseSetting("MetroDisplay:OverpassUrl", "http://127.0.0.1:9/api/interpreter"));
    }

    private static async Task<JsonElement> GetNetworkAsync(WebApplicationFactory<Program> factory)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/network");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
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
    public void NamesTheCachedFeedWhenItIsNotAZip()
    {
        // A captive portal or an interrupted download can leave a cached file that is not a zip.
        // Every later start would fail on it, so the error has to say which file to delete.
        using WebApplicationFactory<Program> factory = CreateFactory("test");
        string cachedFeed = Path.Combine(workspace.FullPath, "cache", "test.zip");
        File.WriteAllText(cachedFeed, "<html>Sign in to continue</html>");

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(cachedFeed, exception.Message);
    }

    [Fact]
    public void RefusesToStartWithoutItsCityConfig()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("missing");

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("missing.json", exception.ToString());
    }

    [Fact]
    public async Task ServesWaterBesideTheLines()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: PondLayerJson);

        JsonElement network = await GetNetworkAsync(factory);

        JsonElement pond = Assert.Single(network.GetProperty("water").EnumerateArray());
        List<double> ring = Assert.Single(pond.GetProperty("rings").EnumerateArray()).EnumerateArray().Select(value => value.GetDouble()).ToList();
        Assert.Equal(8, ring.Count);
        // The fixture's rail runs from 71.12 to 71.06 W, its longer side. The pond sits between
        // 71.10 and 71.09 W, so in the rail's frame it spans a third to a half of the way across.
        List<double> horizontal = ring.Where((_, index) => index % 2 == 0).ToList();
        Assert.Equal(0.3333, horizontal.Min());
        Assert.Equal(0.5, horizontal.Max());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task FetchesWaterFromOverpassOnTheFirstStartAndKeepsIt()
    {
        string? sentBody = null;
        var overpass = new StubHttpHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PondLayerJson) };
        });
        using WebApplicationFactory<Program> baseFactory = CreateFactory("test", waterLayerJson: null);
        using WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(host => host
            .ConfigureTestServices(services => services.AddHttpClient("overpass").ConfigurePrimaryHttpMessageHandler(() => overpass)));

        JsonElement network = await GetNetworkAsync(factory);

        Assert.Single(network.GetProperty("water").EnumerateArray());
        HttpRequestMessage request = Assert.Single(overpass.Requests);
        // Overpass asks callers to say who they are.
        Assert.Equal("MetroDisplay/0.1", request.Headers.UserAgent.ToString());
        // 22 km around 42.34 N is 0.19785 degrees of latitude either way.
        string query = System.Web.HttpUtility.ParseQueryString(sentBody!)["data"]!;
        Assert.Contains("way[\"natural\"=\"coastline\"](42.14215,", query);
        Assert.Contains(",42.53785,", query);
        Assert.True(File.Exists(Path.Combine(workspace.FullPath, "osm", "test-water.json")));
    }

    [Fact]
    public async Task StartsWithoutWaterWhenOverpassIsUnreachable()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: null);

        JsonElement network = await GetNetworkAsync(factory);

        Assert.Equal(0, network.GetProperty("water").GetArrayLength());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
        // Nothing was cached, so the next start tries again.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(workspace.FullPath, "osm")));
    }

    [Fact]
    public async Task StartsWithoutWaterWhenTheCachedLayerIsUnreadable()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: "<html>Sign in to continue</html>");

        JsonElement network = await GetNetworkAsync(factory);

        Assert.Equal(0, network.GetProperty("water").GetArrayLength());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
    }

    public void Dispose() => workspace.Dispose();
}
