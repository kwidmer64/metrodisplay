using MetroDisplay.Contracts;
using MetroDisplay.Server.Feeds;
using MetroDisplay.Spatial;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class WaterLayerLoaderTests
{
    private static CityConfig TestConfig() => new(
        Id: "test",
        Name: "TESTVILLE",
        Agency: "TT",
        Timezone: "America/New_York",
        StaticFeed: new FeedSource("https://example.test/gtfs.zip"),
        Realtime: new RealtimeSources(
            new FeedSource("https://example.test/vp"),
            new FeedSource("https://example.test/alerts")),
        RouteTypes: [0, 1],
        Extent: new ExtentConfig([42.34, -71.08], CoreRadiusKm: 22),
        Simplify: new SimplifyConfig(ToleranceM: 25),
        DwellMs: 300000);

    /// <summary>Keeps what was logged, so a test can read the warning.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task WarnsWithTheFileToDeleteWhenTheCachedLayerCannotBeRead()
    {
        using var workspace = new TemporaryDirectory();
        var cache = new OsmLayerCache(new HttpClient(new StubHttpHandler(_ => throw new InvalidOperationException("The cache should have answered."))), workspace.FullPath, "https://overpass.example.test/api/interpreter");
        string cachedLayer = cache.CachePathFor("test", "water");
        await File.WriteAllTextAsync(cachedLayer, "<html>Sign in to continue</html>");
        var logger = new RecordingLogger();

        IReadOnlyList<WaterArea> water = await WaterLayerLoader.LoadAsync(cache, TestConfig(), new ExtentRectangle(0, 0, 1, 1), logger);

        Assert.Empty(water);
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(cachedLayer, message);
        Assert.Contains("not valid JSON", message);
    }
}
