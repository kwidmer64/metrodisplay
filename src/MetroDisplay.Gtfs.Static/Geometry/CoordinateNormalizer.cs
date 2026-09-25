namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Maps plane coordinates into the normalized space the renderer draws. Both axes are divided
/// by the same span, so a renderer that ignores aspect still draws correct proportions.
/// Y is flipped here, once, so no renderer can get it backwards.
/// </summary>
public static class CoordinateNormalizer
{
    /// <summary>
    /// Four places is about a fifth of a pixel at a 60 km extent on a 1920 px display:
    /// visually lossless, and roughly half the JSON of full precision.
    /// </summary>
    private const int Decimals = 4;

    /// <returns>The point in extent-relative units: the long axis spans [0, 1].</returns>
    public static (double X, double Y) Normalize(PlanePoint point, ExtentRectangle extent)
    {
        double longestSpan = extent.LongestSpan;
        if (longestSpan == 0)
        {
            return (0, 0);
        }

        double normalizedX = (point.X - extent.MinX) / longestSpan;
        double normalizedY = (extent.MaxY - point.Y) / longestSpan;

        return (Math.Round(normalizedX, Decimals), Math.Round(normalizedY, Decimals));
    }

    /// <returns>Normalized points as the wire carries them: <c>[x0, y0, x1, y1, ...]</c>.</returns>
    public static IReadOnlyList<double> Flatten(IEnumerable<PlanePoint> points, ExtentRectangle extent)
    {
        var flattened = new List<double>();
        foreach (PlanePoint point in points)
        {
            (double normalizedX, double normalizedY) = Normalize(point, extent);
            flattened.Add(normalizedX);
            flattened.Add(normalizedY);
        }
        return flattened;
    }
}
