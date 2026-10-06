using MetroDisplay.Spatial;

namespace MetroDisplay.Osm;

/// <summary>
/// A box of latitude and longitude, as Overpass takes its search area.
/// </summary>
/// <param name="South">Southern edge, degrees.</param>
/// <param name="West">Western edge, degrees.</param>
/// <param name="North">Northern edge, degrees.</param>
/// <param name="East">Eastern edge, degrees.</param>
public readonly record struct GeoBox(double South, double West, double North, double East)
{
    /// <summary>
    /// Ground metres in one degree of latitude on the mean-radius sphere, as GroundDistance uses.
    /// </summary>
    private const double MetresPerDegree = 6371008.8 * Math.PI / 180.0;

    public double CentreLatitude => (South + North) / 2.0;

    /// <summary>
    /// The box reaching <paramref name="radiusKm"/> of ground distance from a centre in each
    /// direction. A degree of longitude shrinks with latitude, so the box is wider in degrees
    /// than it is tall.
    /// </summary>
    public static GeoBox Around(GeoPoint centre, double radiusKm)
    {
        double latitudeSpan = radiusKm * 1000.0 / MetresPerDegree;
        double longitudeSpan = latitudeSpan / Math.Cos(centre.Latitude * Math.PI / 180.0);
        return new GeoBox(centre.Latitude - latitudeSpan, centre.Longitude - longitudeSpan, centre.Latitude + latitudeSpan, centre.Longitude + longitudeSpan);
    }

    /// <summary>
    /// The same box in Web Mercator plane units.
    /// </summary>
    public ExtentRectangle ToPlane()
    {
        PlanePoint southWest = MercatorProjector.Project(new GeoPoint(South, West));
        PlanePoint northEast = MercatorProjector.Project(new GeoPoint(North, East));
        return new ExtentRectangle(southWest.X, southWest.Y, northEast.X, northEast.Y);
    }
}
