using System.Net;
using MetroDisplay.Server.Feeds;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class OsmLayerCacheTests
{
    private const string OverpassUrl = "https://overpass.example.test/api/interpreter";
    private const string Query = "[out:json];way[\"natural\"=\"water\"](1,2,3,4);out geom;";
    private const string LayerJson = """{"elements":[{"type":"way","id":1,"tags":{"natural":"water"},"geometry":[{"lat":1,"lon":2}]}]}""";

    private static StubHttpHandler Answering(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public async Task DownloadsAndKeepsTheLayerWhenNothingIsCached()
    {
        using var workspace = new TemporaryDirectory();
        string cacheDirectory = Path.Combine(workspace.FullPath, "osm");
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.OK, LayerJson)), cacheDirectory, OverpassUrl);

        string result = await cache.GetAsync("test", "water", Query);

        Assert.Equal(LayerJson, result);
        Assert.Equal(LayerJson, await File.ReadAllTextAsync(cache.CachePathFor("test", "water")));
        // Nothing but the finished file: no partial download left behind.
        Assert.Equal(new[] { "test-water.json" }, Directory.EnumerateFiles(cacheDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ReusesTheCachedLayerWithoutDownloading()
    {
        using var workspace = new TemporaryDirectory();
        StubHttpHandler handler = Answering(HttpStatusCode.OK, "{}");
        var cache = new OsmLayerCache(new HttpClient(handler), workspace.FullPath, OverpassUrl);
        await File.WriteAllTextAsync(cache.CachePathFor("test", "water"), LayerJson);

        string result = await cache.GetAsync("test", "water", Query);

        Assert.Equal(LayerJson, result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostsTheQueryAsFormData()
    {
        using var workspace = new TemporaryDirectory();
        string? sentBody = null;
        var handler = new StubHttpHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LayerJson) };
        });
        var cache = new OsmLayerCache(new HttpClient(handler), workspace.FullPath, OverpassUrl);

        await cache.GetAsync("test", "water", Query);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(OverpassUrl, request.RequestUri!.ToString());
        // Decoded, because form encoding writes a space as "+" where a URL would write "%20".
        Assert.Equal(Query, System.Web.HttpUtility.ParseQueryString(sentBody!)["data"]);
    }

    [Fact]
    public async Task CachesNothingWhenOverpassFails()
    {
        using var workspace = new TemporaryDirectory();
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.TooManyRequests, "slow down")), workspace.FullPath, OverpassUrl);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("test", "water", Query));

        Assert.Contains("429", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }

    [Fact]
    public async Task CachesNothingWhenOverpassReportsARemark()
    {
        using var workspace = new TemporaryDirectory();
        const string timedOut = """{"elements":[],"remark":"runtime error: Query timed out"}""";
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.OK, timedOut)), workspace.FullPath, OverpassUrl);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("test", "water", Query));

        Assert.Contains("timed out", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }
}
