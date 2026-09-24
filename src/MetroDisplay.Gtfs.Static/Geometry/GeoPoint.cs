namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// A position on the globe.
/// </summary>
/// <param name="Latitude">Degrees north of the equator.</param>
/// <param name="Longitude">Degrees east of Greenwich.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);
