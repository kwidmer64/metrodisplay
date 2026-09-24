namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Distances over the Earth's surface, for reporting real lengths. Never used for drawing.
/// </summary>
public static class GroundDistance
{
    /// <summary>
    /// Mean Earth radius (IUGG). Closer than the equatorial radius for surface distance at
    /// any latitude a city sits at.
    /// </summary>
    private const double MeanEarthRadiusM = 6371008.8;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>
    /// Calculates the great-circle distance between two points using the haversine formula.
    /// </summary>
    public static double BetweenM(GeoPoint start, GeoPoint end)
    {
        // Degree to radian conversion
        double startLatitude = start.Latitude * DegreesToRadians;
        double endLatitude = end.Latitude * DegreesToRadians;
        double latitudeChange = endLatitude - startLatitude;
        double longitudeChange = (end.Longitude - start.Longitude) * DegreesToRadians;

        // Evaluate the core Haversine equation
        double haversine = Math.Pow(Math.Sin(latitudeChange / 2), 2)
            + Math.Cos(startLatitude)
            * Math.Cos(endLatitude)
            * Math.Pow(Math.Sin(longitudeChange / 2), 2);

        return 2 * MeanEarthRadiusM * Math.Asin(Math.Sqrt(haversine));
    }

    /// <summary>
    /// Calculates the total distance (in meters) along a path formed by a list of coordinates.
    /// Returns zero if there are less than two points.
    /// </summary>
    public static double PolylineLengthM(IReadOnlyList<GeoPoint> points)
    {
        double lengthM = 0;
        for (int index = 1; index < points.Count; index++)
        {
            lengthM += BetweenM(points[index - 1], points[index]);
        }
        return lengthM;
    }
}
