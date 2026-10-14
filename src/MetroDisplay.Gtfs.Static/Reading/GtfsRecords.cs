using CsvHelper.Configuration.Attributes;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// The parts of a GTFS feed this system reads whole. <c>stop_times.txt</c> is too large for
/// that and is read separately, see <see cref="GtfsArchiveReader.ReadStopTimes"/>.
/// </summary>
/// <param name="Routes">Every row of <c>routes.txt</c>, buses included; filtering happens later.</param>
/// <param name="Trips">Every row of <c>trips.txt</c>.</param>
/// <param name="ShapePoints">Every row of <c>shapes.txt</c>, in file order.</param>
/// <param name="Stops">Every row of <c>stops.txt</c>: stations, platforms, entrances and bus stops alike.</param>
public sealed record GtfsArchive(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlyList<GtfsShapePoint> ShapePoints, IReadOnlyList<GtfsStop> Stops);

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

/// <summary>
/// A single row of <c>stops.txt</c>. A row is a station, a platform inside one, a stop standing
/// alone, or an entrance or other node that has no position of its own.
/// </summary>
public sealed class GtfsStop
{
    [Name("stop_id")]
    public string StopId { get; set; } = "";

    [Name("stop_name")]
    [Optional]
    public string StopName { get; set; } = "";

    /// <summary>
    /// Null where the feed leaves it empty, as it may for entrances and generic nodes.
    /// </summary>
    [Name("stop_lat")]
    [Optional]
    public double? Latitude { get; set; }

    [Name("stop_lon")]
    [Optional]
    public double? Longitude { get; set; }

    /// <summary>
    /// The <c>stop_id</c> of the station this platform belongs to. Empty for a station and for
    /// a stop that stands alone.
    /// </summary>
    [Name("parent_station")]
    [Optional]
    public string ParentStation { get; set; } = "";
}

/// <summary>
/// One call a trip makes at a stop: the three columns of <c>stop_times.txt</c> this system uses.
/// </summary>
/// <param name="TripId">The trip making the call.</param>
/// <param name="StopId">The stop or platform called at.</param>
/// <param name="Sequence">Order of the call within its trip. Increases along the trip; the values need not be consecutive.</param>
public sealed record GtfsStopTime(string TripId, string StopId, int Sequence);
