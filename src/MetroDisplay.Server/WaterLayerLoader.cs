using MetroDisplay.Contracts;
using MetroDisplay.Osm;
using MetroDisplay.Server.Feeds;
using MetroDisplay.Spatial;

namespace MetroDisplay.Server;

/// <summary>
/// Loads a city's water. Water is decoration from a volunteer-run service, so unlike the rail
/// it never stops startup: whatever goes wrong, the map comes up without it.
/// </summary>
public static class WaterLayerLoader
{
    public const string LayerName = "water";

    /// <param name="cache">Where the raw layer is fetched from and kept.</param>
    /// <param name="config">The city, for its core point and radius.</param>
    /// <param name="railBounds">The rail's frame, so water lines up with it.</param>
    /// <param name="logger">Receives the warning when water is left out.</param>
    /// <returns>The water areas, or an empty list when the layer could not be fetched or read.</returns>
    public static async Task<IReadOnlyList<WaterArea>> LoadAsync(OsmLayerCache cache, CityConfig config, ExtentRectangle railBounds, ILogger logger, CancellationToken cancellationToken = default)
    {
        // Set once the query is known, so the warning can name the file it would be kept in.
        string? cachePath = null;
        try
        {
            GeoBox box = GeoBox.Around(new GeoPoint(config.Extent.Core[0], config.Extent.Core[1]), config.Extent.CoreRadiusKm);
            string query = OverpassQuery.Water(box);
            cachePath = cache.CachePathFor(config.Id, LayerName, query);
            string json = await cache.GetAsync(config.Id, LayerName, query, cancellationToken);
            return WaterLayerBuilder.Build(OverpassReader.Read(json), box.ToPlane(), railBounds, box.CentreLatitude);
        }
        catch (Exception exception)
        {
            // Deliberately broad: no failure in an optional layer may take the rail down with it.
            logger.LogWarning(
                exception,
                "Starting without water: {Reason} If {CachePath} exists, delete it to fetch the layer again.",
                exception.Message,
                cachePath ?? "the cached layer");
            return [];
        }
    }
}
