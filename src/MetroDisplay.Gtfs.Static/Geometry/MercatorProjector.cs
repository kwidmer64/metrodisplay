namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Web Mercator (EPSG:3857). Conformal, so a network keeps its real shape at city scale.
/// Plane distances are stretched by 1/cos(latitude) relative to the ground.
/// </summary>
public static class MercatorProjector
{
    /// <summary>
    /// The WGS 84 equatorial radius that EPSG:3857 is defined on.
    /// </summary>
    private const double EarthRadiusM = 6378137.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>
    /// Implements the standard Web Mercator projection equation.
    /// Maps spherical geo coordinates onto a 2D plane
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public static PlanePoint Project(GeoPoint point)
    {
        double eastingM = EarthRadiusM * point.Longitude * DegreesToRadians;
        double latitudeRadians = point.Latitude * DegreesToRadians;
        double northingM = EarthRadiusM * Math.Log(Math.Tan(Math.PI / 4.0 + latitudeRadians / 2.0));
        return new PlanePoint(eastingM, northingM);
    }

    /// <summary>
    /// Converts a plane distance to the ground distance it represents at a given latitude.
    /// </summary>
    /// <param name="planeMetres">Distance measured between projected points.</param>
    /// <param name="latitudeDegrees">Latitude the distance sits at.</param>
    public static double PlaneMetresToGround(double planeMetres, double latitudeDegrees) => planeMetres * Math.Cos(latitudeDegrees * DegreesToRadians);
}
