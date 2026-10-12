namespace MetroDisplay.Spatial;

/// <summary>
/// Cuts a polyline down to the parts that lie inside a rectangle.
/// </summary>
public static class PolylineClipper
{
    /// <summary>
    /// Cuts mid-segment wherever the polyline crosses the rectangle's edge. The edge itself
    /// counts as inside.
    /// </summary>
    /// <param name="points">The polyline, in order.</param>
    /// <param name="rectangle">The area to keep.</param>
    /// <returns>
    /// One run per visit to the rectangle, in the order the polyline makes them, each with at
    /// least two points. Empty when the polyline never enters. A polyline wholly inside comes
    /// back as one run holding its own points, unchanged.
    /// </returns>
    public static IReadOnlyList<IReadOnlyList<PlanePoint>> Clip(IReadOnlyList<PlanePoint> points, ExtentRectangle rectangle)
    {
        var runs = new List<IReadOnlyList<PlanePoint>>();
        List<PlanePoint>? openRun = null;

        for (int index = 1; index < points.Count; index++)
        {
            PlanePoint start = points[index - 1];
            PlanePoint end = points[index];

            if (!TryClipSegment(start, end, rectangle, out double entryFraction, out double exitFraction))
            {
                openRun = null;
                continue;
            }

            // A segment that begins inside carries on the run the last one left open.
            if (openRun is null || entryFraction > 0)
            {
                openRun = [Along(start, end, entryFraction)];
                runs.Add(openRun);
            }
            openRun.Add(Along(start, end, exitFraction));

            if (exitFraction < 1)
            {
                openRun = null;
            }
        }

        return runs;
    }

    /// <summary>
    /// The point a fraction of the way from start to end. The ends themselves are returned as
    /// they are, so a vertex that is kept is never moved by rounding.
    /// </summary>
    private static PlanePoint Along(PlanePoint start, PlanePoint end, double fraction)
    {
        if (fraction == 0)
        {
            return start;
        }
        if (fraction == 1)
        {
            return end;
        }
        return new PlanePoint(start.X + fraction * (end.X - start.X), start.Y + fraction * (end.Y - start.Y));
    }

    /// <summary>
    /// Liang–Barsky: each edge narrows the range of fractions along the segment that lie
    /// inside the rectangle.
    /// </summary>
    /// <returns>False when no part of the segment is worth drawing.</returns>
    private static bool TryClipSegment(PlanePoint start, PlanePoint end, ExtentRectangle rectangle, out double entryFraction, out double exitFraction)
    {
        entryFraction = 0;
        exitFraction = 1;
        double changeX = end.X - start.X;
        double changeY = end.Y - start.Y;

        bool reaches =
            Narrow(-changeX, start.X - rectangle.MinX, ref entryFraction, ref exitFraction) &&
            Narrow(changeX, rectangle.MaxX - start.X, ref entryFraction, ref exitFraction) &&
            Narrow(-changeY, start.Y - rectangle.MinY, ref entryFraction, ref exitFraction) &&
            Narrow(changeY, rectangle.MaxY - start.Y, ref entryFraction, ref exitFraction);
        if (!reaches)
        {
            return false;
        }

        // A segment with length that meets the rectangle at one point only grazes it. A
        // repeated point has no length and is kept, so a line inside comes back unchanged.
        bool hasLength = changeX != 0 || changeY != 0;
        return !(hasLength && entryFraction == exitFraction);
    }

    /// <summary>
    /// Applies one edge. <paramref name="direction"/> is how fast the segment moves toward
    /// the outside of that edge, and <paramref name="distance"/> how far inside it starts.
    /// </summary>
    /// <returns>False when the edge puts the whole segment outside.</returns>
    private static bool Narrow(double direction, double distance, ref double entryFraction, ref double exitFraction)
    {
        // Parallel to the edge: inside or outside it for the segment's whole length.
        if (direction == 0)
        {
            return distance >= 0;
        }

        double fraction = distance / direction;
        if (direction < 0)
        {
            // Moving inward across this edge: the crossing is where the segment enters.
            if (fraction > exitFraction)
            {
                return false;
            }
            entryFraction = Math.Max(entryFraction, fraction);
        }
        else
        {
            // Moving outward across this edge: the crossing is where the segment exits.
            if (fraction < entryFraction)
            {
                return false;
            }
            exitFraction = Math.Min(exitFraction, fraction);
        }
        return true;
    }
}
