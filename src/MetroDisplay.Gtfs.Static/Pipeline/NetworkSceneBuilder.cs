using System.Security.Cryptography;
using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// Turns a GTFS zip and a city config into the scene the renderer draws. Pure over its
/// inputs: the same zip and config always produce the same scene, version included.
/// </summary>
/// <remarks>
/// Lines only, fitted to the network's own bounds. The configured extent, clipping, edge
/// labels, stations and simplification each arrive in a later slice (spec §14).
/// </remarks>
public static class NetworkSceneBuilder
{
    /// <summary>
    /// Drawn for a route whose agency publishes no usable <c>route_color</c>.
    /// </summary>
    public const string FallbackColor = "#8E9BAD";

    /// <param name="zipBytes">The static GTFS archive.</param>
    /// <param name="config">The city being built.</param>
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column.</exception>
    public static NetworkScene Build(byte[] zipBytes, CityConfig config)
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
        ExtentRectangle bounds = ExtentRectangle.Enclosing(planePointsByShapeId.Values.SelectMany(points => points));


        // selects all the points, gets the latitudes from those points, converts to list
        // Mercator stretches based on latitude, and using the middle latittude minimizes the error
        List<double> latitudes = geoPointsByShapeId.Values.SelectMany(points => points).Select(point => point.Latitude).ToList();
        double centerLatitude = (latitudes.Min() + latitudes.Max()) / 2.0;

        // create a new extend window
        ExtentInfo extent = new ExtentInfo(
            Aspect: Math.Round(bounds.Aspect, 4),
            CoreRadiusKm: config.Extent.CoreRadiusKm,
            SpanKm: Math.Round(MercatorProjector.PlaneMetresToGround(bounds.LongestSpan, centerLatitude) / 1000.0, 2));

        return new NetworkScene(
            ArtifactVersion: ContentVersion(config.Id, zipBytes),
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: extent,
            Lines: BuildLines(selection, geoPointsByShapeId, planePointsByShapeId, bounds),
            Stations: [],
            EdgeLabels: []);
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

    private static List<LineScene> BuildLines(RailSelection selection, Dictionary<string, List<GeoPoint>> geoPointsByShapeId, Dictionary<string, List<PlanePoint>> planePointsByShapeId, ExtentRectangle bounds)
    {
        // Trips whose shape is not in shapes.txt drop out here
        // Create a Lookup linking each routeId to all valid shapeIds associated with its trips
        ILookup<string, string> shapeIdsByRouteId = selection.Trips
            .Where(trip => geoPointsByShapeId.ContainsKey(trip.ShapeId))
            .ToLookup(trip => trip.RouteId, trip => trip.ShapeId, StringComparer.Ordinal);

        List<LineScene> lines = new();
        foreach (GtfsRoute route in selection.Routes.OrderBy(route => route.RouteId, StringComparer.Ordinal))
        {
            List<ShapeGeometry> shapes = shapeIdsByRouteId[route.RouteId] // pull all shapeIds linked to this route
                .Distinct(StringComparer.Ordinal) // Remove duplicate shapeIds
                .Order(StringComparer.Ordinal) // Sort them alphbetically
                .Select(shapeId => new ShapeGeometry( // convert each unique shapeId into a ShapeGeometry object
                    shapeId,
                    CoordinateNormalizer.Flatten(planePointsByShapeId[shapeId], bounds), // scale the points relative to the bounding box
                    Math.Round(GroundDistance.PolylineLengthM(geoPointsByShapeId[shapeId]), 1)))  // caulculate the real world length of the route, rounded to 1 decimal place
                .ToList();

            // A route listed in routes.txt with nothing to draw, such as a seasonal route, is
            // left off the map instead of being sent as an empty line.
            if (shapes.Count == 0)
            {
                continue;
            }

            lines.Add(new LineScene(route.RouteId, DisplayName(route), FormatColor(route.RouteColor), shapes));
        }

        // return LineScene objects for rendering
        return lines;
    }

    // Gets the display name of the route
    private static string DisplayName(GtfsRoute route) => string.IsNullOrWhiteSpace(route.RouteLongName) ? route.RouteShortName : route.RouteLongName;

    /// <summary>
    /// GTFS specifies six hex digits without a leading <c>#</c>. Agencies vary, so case and a
    /// stray <c>#</c> are tolerated; anything else falls back to grey.
    /// </summary>
    private static string FormatColor(string routeColor)
    {
        // trim the hex input, then return the result is valid hex
        // returns fallback color if invalid
        string hex = routeColor.Trim().TrimStart('#');
        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? $"#{hex.ToUpperInvariant()}" : FallbackColor;
    }

    /// <summary>
    /// Derived from the zip so the same feed always yields the same version. The artifact
    /// store replaces this with a dated version in slice 15.
    /// </summary>
    private static string ContentVersion(string cityId, byte[] zipBytes) => $"{cityId}@{Convert.ToHexStringLower(SHA256.HashData(zipBytes))[..8]}";
}
