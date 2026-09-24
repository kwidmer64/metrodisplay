namespace MetroDisplay.Contracts;

/// <summary>
/// One city's entry in the registry, read from <c>cities/&lt;id&gt;.json</c>. Adding a city is adding one of these.
/// </summary>
/// <param name="Id">Registry id, matching the config file name.</param>
/// <param name="Name">Name as shown on screen.</param>
/// <param name="Agency">Operating agency.</param>
/// <param name="Timezone">IANA timezone id for the city's local clock, such as <c>America/New_York</c>.</param>
/// <param name="StaticFeed">Where the static GTFS archive is fetched from.</param>
/// <param name="Realtime">GTFS-RT feeds polled while the city is on screen.</param>
/// <param name="RouteTypes">GTFS <c>route_type</c> values to draw, such as 0 for light rail and 1 for subway.</param>
/// <param name="Extent">Core point and radius that define the visible window.</param>
/// <param name="Simplify">How aggressively track geometry is simplified.</param>
/// <param name="DwellMs">How long the city stays on screen per rotation, in milliseconds.</param>
/// <param name="RouteFilter">Optional include or exclude list by <c>route_id</c>, applied after <paramref name="RouteTypes"/>. Last so it can default to null; every other setting is required.</param>
public sealed record CityConfig(string Id, string Name, string Agency, string Timezone, FeedSource StaticFeed, RealtimeSources Realtime, IReadOnlyList<int> RouteTypes, ExtentConfig Extent, SimplifyConfig Simplify, int DwellMs, RouteFilter? RouteFilter = null);

/// <summary>
/// The HTTP endpoint the system fetches from.
/// </summary>
/// <param name="Url">Absolute URL of the feed.</param>
/// <param name="Headers">Extra request headers. Values may use <c>${ENV_VAR}</c> placeholders, which is how API keys enter the system.</param>
/// <param name="IntervalMs">Poll interval in milliseconds. 0 for feeds that are fetched on a schedule rather than polled.</param>
public sealed record FeedSource(string Url, IReadOnlyDictionary<string, string>? Headers = null, int IntervalMs = 0);

/// <summary>
/// The GTFS-RT feeds for a city.
/// </summary>
/// <param name="VehiclePositions">Vehicle positions feed, polled every few seconds.</param>
/// <param name="Alerts">Service alerts feed, polled less often.</param>
public sealed record RealtimeSources(FeedSource VehiclePositions, FeedSource Alerts);

/// <summary>
/// Narrows the routes selected by <c>route_type</c>. When both lists are set, <paramref name="Include"/> wins.
/// </summary>
/// <param name="Include">Only these route ids are kept.</param>
/// <param name="Exclude">These route ids are removed.</param>
public sealed record RouteFilter(IReadOnlyList<string>? Include = null, IReadOnlyList<string>? Exclude = null);

/// <summary>
/// The real-distance window the map is drawn within. Track beyond it is clipped, not compressed.
/// </summary>
/// <param name="Core">Centre of the window as <c>[latitude, longitude]</c>.</param>
/// <param name="CoreRadiusKm">Radius around the core as ground distance, not Mercator plane distance.</param>
public sealed record ExtentConfig(IReadOnlyList<double> Core, double CoreRadiusKm);

/// <summary>
/// Douglas–Peucker simplification settings.
/// </summary>
/// <param name="ToleranceM">Maximum distance a simplified line may stray from the original, in ground metres.</param>
public sealed record SimplifyConfig(double ToleranceM);
