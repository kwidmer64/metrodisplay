using CsvHelper.Configuration.Attributes;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// The parts of a GTFS feed this system reads.
/// </summary>
/// <param name="Routes">Every row of <c>routes.txt</c>, buses included; filtering happens later.</param>
/// <param name="Trips">Every row of <c>trips.txt</c>.</param>
/// <param name="ShapePoints">Every row of <c>shapes.txt</c>, in file order.</param>
public sealed record GtfsArchive(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlyList<GtfsShapePoint> ShapePoints);

/// <summary>
/// A single row of <c>routes.txt</c>.
/// </summary>
public sealed class GtfsRoute
{
    [Name("route_id")]
    public string RouteId { get; set; } = "";

    [Name("route_short_name")]
    [Optional]
    public string RouteShortName { get; set; } = "";

    [Name("route_long_name")]
    [Optional]
    public string RouteLongName { get; set; } = "";

    [Name("route_type")]
    public int RouteType { get; set; }

    [Name("route_color")]
    [Optional]
    public string RouteColor { get; set; } = "";
}

/// <summary>
/// A single row of <c>trips.txt</c>.
/// </summary>
public sealed class GtfsTrip
{
    [Name("route_id")]
    public string RouteId { get; set; } = "";

    [Name("trip_id")]
    public string TripId { get; set; } = "";

    [Name("shape_id")]
    [Optional]
    public string ShapeId { get; set; } = "";
}

/// <summary>
/// A single row of <c>shapes.txt</c>, which is a single vertex of a shape's polyline.
/// </summary>
public sealed class GtfsShapePoint
{
    [Name("shape_id")]
    public string ShapeId { get; set; } = "";

    [Name("shape_pt_lat")]
    public double Latitude { get; set; }

    [Name("shape_pt_lon")]
    public double Longitude { get; set; }

    [Name("shape_pt_sequence")]
    public int Sequence { get; set; }
}
