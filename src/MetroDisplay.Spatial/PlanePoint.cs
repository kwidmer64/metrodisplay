namespace MetroDisplay.Spatial;

/// <summary>
/// A projected position in Web Mercator plane units. Y grows northward.
/// </summary>
/// <param name="X">Easting.</param>
/// <param name="Y">Northing.</param>
public readonly record struct PlanePoint(double X, double Y);
