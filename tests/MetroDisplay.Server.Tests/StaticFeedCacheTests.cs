using System.Net;
using MetroDisplay.Contracts;
using MetroDisplay.Server.Feeds;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class StaticFeedCacheTests
{
    private static readonly FeedSource Source = new("https://example.test/gtfs.zip");

    /// <summary>The cache treats the feed as opaque bytes, so these need not be a real zip.</summary>
    private static readonly byte[] FeedBytes = [0x50, 0x4B, 0x03, 0x04, 0x2A];

    private static StubHttpHandler Serving(byte[] body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    [Fact]
    public async Task DownloadsAndKeepsTheFeedWhenNothingIsCached()
    {
        using var workspace = new TemporaryDirectory();
        string cacheDirectory = Path.Combine(workspace.FullPath, "gtfs");
        var cache = new StaticFeedCache(new HttpClient(Serving(FeedBytes)), cacheDirectory);

        byte[] result = await cache.GetAsync("test", Source);

        Assert.Equal(FeedBytes, result);
        Assert.Equal(FeedBytes, await File.ReadAllBytesAsync(Path.Combine(cacheDirectory, "test.zip")));
    }

    [Fact]
    public async Task ReusesTheCachedFeedWithoutDownloading()
    {
        using var workspace = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(workspace.FullPath, "test.zip"), FeedBytes);
        StubHttpHandler handler = Serving([]);
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);

        byte[] result = await cache.GetAsync("test", Source);

        Assert.Equal(FeedBytes, result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendsTheConfiguredHeaders()
    {
        using var workspace = new TemporaryDirectory();
        StubHttpHandler handler = Serving(FeedBytes);
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);
        var keyedSource = Source with { Headers = new Dictionary<string, string> { ["x-api-key"] = "secret-value" } };

        await cache.GetAsync("test", keyedSource);

        Assert.Equal("secret-value", handler.Requests.Single().Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task CachesNothingWhenTheDownloadFails()
    {
        using var workspace = new TemporaryDirectory();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("test", Source));

        Assert.Contains(Source.Url, exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }
}
