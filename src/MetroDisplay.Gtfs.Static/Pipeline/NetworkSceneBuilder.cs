using System.Globalization;
using System.Security.Cryptography;
using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// The rail scene together with the frame its coordinates are normalized in.
/// </summary>
/// <param name="Scene">The scene, with no water yet.</param>
/// <param name="Bounds">The extent: the plane rectangle every normalized coordinate in <paramref name="Scene"/> is relative to. Other layers normalize with it so they line up with the rail.</param>
public sealed record RailLayer(NetworkScene Scene, ExtentRectangle Bounds);

/// <summary>
/// Turns a GTFS zip and a city config into the scene the renderer draws. Pure over its
/// inputs: the same zip and config always produce the same scene, version included.
/// </summary>
/// <remarks>
/// Lines and stations, framed on the extent of spec §5 and cut at its edge. Edge labels and
/// simplification each arrive in a later slice (spec §14).
/// </remarks>
public static class NetworkSceneBuilder
{
    /// <summary>
    /// Drawn for a route whose agency publishes no usable <c>route_color</c>.
    /// </summary>
    public const string FallbackColor = "#8E9BAD";

    /// <summary>
    /// How far past the extent's edge a station's own position may lie and still be drawn, on
    /// the edge. That position is the middle of the station, which at the end of a line lies a
    /// little beyond the last point of track, and the track is what set the edge.
    /// </summary>
    private const double StationReachM = 100;

    /// <summary>
    /// The rail scene alone, for callers that need no other layer.
    /// </summary>
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column. <c>stops.txt</c> and <c>stop_times.txt</c> are required.</exception>
    /// <exception cref="InvalidOperationException">No rail lies inside the city's core square: the config's core point or radius is wrong.</exception>
    public static NetworkScene Build(byte[] zipBytes, CityConfig config) => BuildLayer(zipBytes, config).Scene;

    /// <param name="zipBytes">The static GTFS archive.</param>
    /// <param name="config">The city being built.</param>
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column. <c>stops.txt</c> and <c>stop_times.txt</c> are required.</exception>
    /// <exception cref="InvalidOperationException">No rail lies inside the city's core square: the config's core point or radius is wrong.</exception>
    public static RailLayer BuildLayer(byte[] zipBytes, CityConfig config)
    {
        // Read the GTFS ZIP file and select the rail routes
        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);
        RailSelection selection = RailRouteSelector.Select(archive, config.RouteTypes, config.RouteFilter);

        // Get dictionary of sorted geo points grouped by shape id
        Dictionary<string, List<GeoPoint>> geoPointsByShapeId = GroupShapePoints(archive.ShapePoints, selection.ShapeIds);
        if (geoPointsByShapeId.Count == 0)
        {
            throw new InvalidDataException($"Feed for '{config.Id}' has no rail shapes for route types [{string.Join(", ", config.RouteTypes)}].");
        }

        // convert the geo points into plane points
        Dictionary<string, List<PlanePoint>> planePointsByShapeId = geoPointsByShapeId.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Select(MercatorProjector.Project).ToList(),
            StringComparer.Ordinal);

        // create a bounding box that tightly fits all shapes combined
        ExtentRectangle content = ExtentRectangle.Enclosing(planePointsByShapeId.Values.SelectMany(points => points));

        // The extent is that box cut down to the core square, so the map's scale is set by
        // real distance from the core and not by how far the rail happens to run.
        GeoPoint core = new(config.Extent.Core[0], config.Extent.Core[1]);
        ExtentRectangle coreSquare = GeoBox.Around(core, config.Extent.CoreRadiusKm).ToPlane();
        ExtentRectangle extent = content.Intersect(coreSquare) ?? throw NoRailInTheCoreSquare(config);

        // The box can overlap the square without any rail running through the overlap.
        List<LineScene> lines = BuildLines(selection, planePointsByShapeId, extent);
        if (lines.Count == 0)
        {
            throw NoRailInTheCoreSquare(config);
        }

        // Mercator stretches based on latitude, and using the extent's middle latitude minimizes the error
        double southLatitude = MercatorProjector.Unproject(new PlanePoint(extent.MinX, extent.MinY)).Latitude;
        double northLatitude = MercatorProjector.Unproject(new PlanePoint(extent.MinX, extent.MaxY)).Latitude;
        double centerLatitude = (southLatitude + northLatitude) / 2.0;

        // Stations come from the calls rail trips make, so only those trips' rows are read.
        HashSet<string> tripIds = selection.Trips.Select(trip => trip.TripId).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, tripIds);
        List<StationMarker> stations = BuildStationMarkers(
            StationBuilder.Build(selection, archive.Stops, stopTimes),
            extent,
            MercatorProjector.GroundMetresToPlane(StationReachM, centerLatitude));

        // create a new extend window
        ExtentInfo extentInfo = new ExtentInfo(
            Aspect: Math.Round(extent.Aspect, 4),
            CoreRadiusKm: config.Extent.CoreRadiusKm,
            SpanKm: Math.Round(MercatorProjector.PlaneMetresToGround(extent.LongestSpan, centerLatitude) / 1000.0, 2));

        NetworkScene scene = new(
            ArtifactVersion: ContentVersion(config.Id, zipBytes),
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: extentInfo,
            Lines: lines,
            Stations: stations,
            EdgeLabels: [],
            Water: []);

        return new RailLayer(scene, extent);
    }

    /// <summary>
    /// Groups the wanted shapes' points, each shape in <c>shape_pt_sequence</c> order. Row
    /// order in <c>shapes.txt</c> is not guaranteed by GTFS.
    /// </summary>
    private static Dictionary<string, List<GeoPoint>> GroupShapePoints(IReadOnlyList<GtfsShapePoint> shapePoints, IReadOnlySet<string> wantedShapeIds)
        => shapePoints
            .Where(point => wantedShapeIds.Contains(point.ShapeId)) // Filters to only shapeIds in wantedShapeIds
            .GroupBy(point => point.ShapeId, StringComparer.Ordinal) // Filters into groups where each point shares the same ShapeId
            .ToDictionary(
                group => group.Key, // Set the dictionary key
                group => group.OrderBy(point => point.Sequence) // Order each group by the sequence (shape_pt_sequence) property
                              .Select(point => new GeoPoint(point.Latitude, point.Longitude)) // Map each point onto a GeoPoint object
                              .ToList(),
                StringComparer.Ordinal);

    private static List<LineScene> BuildLines(RailSelection selection, Dictionary<string, List<PlanePoint>> planePointsByShapeId, ExtentRectangle extent)
    {
        // Trips whose shape is not in shapes.txt drop out here
        // Create a Lookup linking each routeId to all valid shapeIds associated with its trips
        ILookup<string, string> shapeIdsByRouteId = selection.Trips
            .Where(trip => planePointsByShapeId.ContainsKey(trip.ShapeId))
            .ToLookup(trip => trip.RouteId, trip => trip.ShapeId, StringComparer.Ordinal);

        List<LineScene> lines = new();
        foreach (GtfsRoute route in selection.Routes.OrderBy(route => route.RouteId, StringComparer.Ordinal))
        {
            List<ShapeGeometry> shapes = shapeIdsByRouteId[route.RouteId] // pull all shapeIds linked to this route
                .Distinct(StringComparer.Ordinal) // Remove duplicate shapeIds
                .Order(StringComparer.Ordinal) // Sort them alphbetically
                .SelectMany(shapeId => ClipToRuns(shapeId, planePointsByShapeId[shapeId], extent)) // cut each shape to the extent; a shape outside it gives nothing
                .ToList();

            // A route listed in routes.txt with nothing to draw, such as a seasonal route or
            // one that lies outside the extent, is left off the map instead of being sent as
            // an empty line.
            if (shapes.Count == 0)
            {
                continue;
            }

            lines.Add(new LineScene(route.RouteId, DisplayName(route), FormatColor(route.RouteColor), shapes));
        }

        // return LineScene objects for rendering
        return lines;
    }

    /// <summary>
    /// The parts of one shape that lie inside the extent, each as its own polyline. The first
    /// keeps the shape's id; a shape that leaves and returns gives further runs, numbered
    /// <c>#1</c>, <c>#2</c> in the order the shape visits them.
    /// </summary>
    private static IEnumerable<ShapeGeometry> ClipToRuns(string shapeId, IReadOnlyList<PlanePoint> points, ExtentRectangle extent)
        => PolylineClipper.Clip(points, extent).Select((run, runIndex) => new ShapeGeometry(
            runIndex == 0 ? shapeId : $"{shapeId}#{runIndex}",
            CoordinateNormalizer.Flatten(run, extent), // scale the points relative to the extent
            Math.Round(GroundDistance.PolylineLengthM(run.Select(MercatorProjector.Unproject).ToList()), 1))); // the real world length of what is drawn, rounded to 1 decimal place

    /// <summary>
    /// Stations in the same normalized frame as the lines. One just past the edge is drawn on
    /// it. One farther out is left off: its line was cut before reaching it.
    /// </summary>
    /// <param name="stations">Every rail station in the feed.</param>
    /// <param name="extent">The frame the lines are normalized in.</param>
    /// <param name="reachPlaneUnits">How far past the edge still counts. Plane units, not metres of ground.</param>
    private static List<StationMarker> BuildStationMarkers(IReadOnlyList<Station> stations, ExtentRectangle extent, double reachPlaneUnits)
    {
        List<StationMarker> markers = new();
        foreach (Station station in stations)
        {
            PlanePoint position = MercatorProjector.Project(station.Position);
            PlanePoint nearest = extent.Clamp(position);
            double distancePastEdge = Math.Sqrt(Math.Pow(position.X - nearest.X, 2) + Math.Pow(position.Y - nearest.Y, 2));

            // Written this way round so a position that is not a number is left off too.
            if (!(distancePastEdge <= reachPlaneUnits))
            {
                continue;
            }

            (double normalizedX, double normalizedY) = CoordinateNormalizer.Normalize(nearest, extent);
            markers.Add(new StationMarker(normalizedX, normalizedY, station.Name, station.Rank));
        }
        return markers;
    }

    /// <summary>
    /// The feed is sound and the config is not, so this is not the feed's exception type:
    /// the Server answers a bad feed by naming the cached file to delete.
    /// </summary>
    private static InvalidOperationException NoRailInTheCoreSquare(CityConfig config)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"No rail for '{config.Id}' lies within {config.Extent.CoreRadiusKm} km of its core point ({config.Extent.Core[0]}, {config.Extent.Core[1]}). Check extent.core and extent.coreRadiusKm in the city's config."));

    // Gets the display name of the route
    private static string DisplayName(GtfsRoute route) => string.IsNullOrWhiteSpace(route.RouteLongName) ? route.RouteShortName : route.RouteLongName;

    /// <summary>
    /// GTFS specifies six hex digits without a leading <c>#</c>. Agencies vary, so case and a
    /// stray <c>#</c> are tolerated; anything else falls back to grey.
    /// </summary>
    internal static string FormatColor(string routeColor)
    {
        // trim the hex input, then return the result is valid hex
        // returns fallback color if invalid
        string hex = routeColor.Trim().TrimStart('#');
        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? $"#{hex.ToUpperInvariant()}" : FallbackColor;
    }

    /// <summary>
    /// Derived from the zip so the same feed always yields the same version. The artifact
    /// store replaces this with a dated version in slice 14.
    /// </summary>
    private static string ContentVersion(string cityId, byte[] zipBytes) => $"{cityId}@{Convert.ToHexStringLower(SHA256.HashData(zipBytes))[..8]}";
}
