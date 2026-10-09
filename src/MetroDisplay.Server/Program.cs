using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using MetroDisplay.Server;
using MetroDisplay.Server.Feeds;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("overpass", client =>
{
    // The first fetch for a city can take most of a minute; Overpass asks callers to say who they are.
    client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MetroDisplay/0.1");
});

var app = builder.Build();

// Read after Build so a test host's settings apply.
ServerSettings settings = app.Configuration.GetRequiredSection("MetroDisplay").Get<ServerSettings>() ?? throw new InvalidOperationException("The MetroDisplay configuration section is empty.");
string contentRoot = app.Environment.ContentRootPath;

// The scene is built once, before the Server listens. Any failure stops startup and names
// its cause; degrading gracefully is slice 15's job.
string configPath = Path.GetFullPath(Path.Combine(contentRoot, settings.CitiesDirectory, $"{settings.CityId}.json"));
CityConfig config = CityConfigLoader.Load(await File.ReadAllTextAsync(configPath), ConfigurationEnvironment.From(app.Configuration));

var feedCache = new StaticFeedCache(app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(), Path.GetFullPath(Path.Combine(contentRoot, settings.FeedCacheDirectory)));
byte[] zipBytes = await feedCache.GetAsync(config.Id, config.StaticFeed);

// The cached file is trusted on every start, so a bad one (a captive-portal page, an
// interrupted download) would fail every start. Name it, and say how to recover.
RailLayer rail;
try
{
    rail = NetworkSceneBuilder.BuildLayer(zipBytes, config);
}
catch (InvalidDataException exception)
{
    throw new InvalidDataException(
        $"The static feed cached at {feedCache.CachePathFor(config.Id)} could not be read: {exception.Message} Delete it to download the feed again.",
        exception);
}

// Water is fetched once per city and then kept. It never stops startup.
OsmLayerCache osmCache = new(
    app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("overpass"),
    Path.GetFullPath(Path.Combine(contentRoot, settings.OsmCacheDirectory)),
    settings.OverpassUrl);
IReadOnlyList<WaterArea> water = await WaterLayerLoader.LoadAsync(osmCache, config, rail.Bounds, app.Logger);
NetworkScene scene = rail.Scene with { Water = water };

app.Logger.LogInformation(
    "Built {ArtifactVersion}: {LineCount} lines, {ShapeCount} shapes, {WaterCount} water areas",
    scene.ArtifactVersion,
    scene.Lines.Count,
    scene.Lines.Sum(line => line.Shapes.Count),
    scene.Water.Count);

app.MapGet("/api/network", () => TypedResults.Json(scene, JsonDefaults.Options));

app.Run();
