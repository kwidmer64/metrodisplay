namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// An axis-aligned rectangle in Web Mercator plane units: the part of the plane the map shows.
/// </summary>
/// <param name="MinX">Western edge.</param>
/// <param name="MinY">Southern edge.</param>
/// <param name="MaxX">Eastern edge.</param>
/// <param name="MaxY">Northern edge.</param>
public readonly record struct ExtentRectangle(double MinX, double MinY, double MaxX, double MaxY)
{
    // => is a shorthand way to write a getter
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double LongestSpan => Math.Max(Width, Height);

    /// <summary>
    /// Width over height. 1 for a rectangle with no height, so no consumer divides by zero.
    /// </summary>
    public double Aspect => Height == 0 ? 1.0 : Width / Height;

    /// <summary>
    /// The smallest rectangle containing every point.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static ExtentRectangle Enclosing(IEnumerable<PlanePoint> points)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool sawPoint = false;

        // Calculate the bounding box of points
        // Loop through each point, finding max and min point in points for both X and Y
        // If loops runs at least once, flip sawPoint to true.
        // Why not .Any()? -> input may be a lazy sequence, so .Any() would enumurate over points a second time, where flipping sawPoints does it in
        foreach (PlanePoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            sawPoint = true;
        }

        if (!sawPoint)
        {
            throw new ArgumentException("Cannot bound an empty set of points.", nameof(points));
        }

        return new ExtentRectangle(minX, minY, maxX, maxY);
    }
}
