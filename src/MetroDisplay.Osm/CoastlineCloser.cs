using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Polygonize;

namespace MetroDisplay.Osm;

/// <summary>
/// Turns OpenStreetMap coastline, which is a set of open lines, into filled sea.
/// </summary>
public static class CoastlineCloser
{
    /// <summary>
    /// How far either side of a coastline segment the sea and land probes sit, in plane units.
    /// Small enough to stay inside the face the segment borders.
    /// </summary>
    private const double SideProbeOffsetM = 0.5;

    /// <summary>
    /// A face with fewer probes than this is too small to judge and is left to the vote.
    /// </summary>
    private const int MinimumVotesToJudge = 10;

    /// <summary>
    /// The share of a face's probes that may disagree with the rest before the coastline is
    /// taken to be broken. A closed coast gives faces that are all sea or all land; a few stray
    /// probes come from features narrower than the probe offset.
    /// </summary>
    private const double MixedShoreShare = 0.05;

    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// The sea inside a frame.
    /// </summary>
    /// <param name="coastlineWays">Coastline ways in OSM's direction: land on the left, sea on the right.</param>
    /// <param name="frame">The rectangle to close the coastline against.</param>
    /// <returns>Sea polygons inside the frame, with islands as holes. Empty when no coastline crosses the frame.</returns>
    /// <exception cref="InvalidDataException">The coastline does not close against the frame, so sea and land cannot be told apart.</exception>
    public static IReadOnlyList<Polygon> CloseSea(IReadOnlyList<IReadOnlyList<PlanePoint>> coastlineWays, ExtentRectangle frame)
    {
        // Generate the Envelope for the viewport, then convert to geometry
        // Envelopes are more lightweight than Geometry
        Envelope frameEnvelope = new(frame.MinX, frame.MaxX, frame.MinY, frame.MaxY);
        Geometry frameArea = Factory.ToGeometry(frameEnvelope);

        // Holds lines, curves, boundaries, etc
        List<Geometry> linework = new List<Geometry> { frameArea.Boundary };
        // Coordinates over water
        List<Coordinate> seaProbes = new List<Coordinate>();
        // Coordinates over land
        List<Coordinate> landProbes = new List<Coordinate>();
        foreach (IReadOnlyList<PlanePoint> way in coastlineWays)
        {
            // A line needs two points, skip anything shorter
            if (way.Count < 2)
            {
                continue;
            }

            // Convert to GIS coordinates
            Coordinate[] line = way.Select(point => new Coordinate(point.X, point.Y)).ToArray();

            // Convert to LineString and clip the line geometry to the frame
            linework.Add(Factory.CreateLineString(line).Intersection(frameArea));

            // Generate sample probes along both sides of the segment
            AddSideProbes(line, frameEnvelope, seaProbes, landProbes);
        }

        // Early exit check
        if (seaProbes.Count == 0)
        {
            return [];
        }

        // Create polygonizer to to find closed loops by intersecting or connecting lines
        Polygonizer polygonizer = new();

        // Merge all linework into one geometry
        polygonizer.Add(Factory.BuildGeometry(linework).Union());

        // Find every enclosed area (or 'face') and convert to a list of Polygons
        List<Polygon> faces = polygonizer.GetPolygons().Cast<Polygon>().ToList();

        int[] seaVotes = CountProbesPerFace(faces, seaProbes);
        int[] landVotes = CountProbesPerFace(faces, landProbes);

        // A coast that stops short of the frame's edge divides nothing: sea and land probes then
        // share one face, and the vote would call the whole map sea or the whole map land.
        for (int faceNumber = 0; faceNumber < faces.Count; faceNumber++)
        {
            int totalVotes = seaVotes[faceNumber] + landVotes[faceNumber];
            int minorityVotes = Math.Min(seaVotes[faceNumber], landVotes[faceNumber]);
            if (totalVotes >= MinimumVotesToJudge && minorityVotes > totalVotes * MixedShoreShare)
            {
                throw new InvalidDataException(
                    $"The coastline does not close against the map's edge: one area has {seaVotes[faceNumber]} stretches of shore facing the sea and {landVotes[faceNumber]} facing land. " +
                    "The data may not cover the whole map, or the coastline has a gap.");
            }
        }

        // If a face contains more sea probes than land probes, classify as water, return as list of sea faces
        return faces.Where((_, faceNumber) => seaVotes[faceNumber] > landVotes[faceNumber]).ToList();
    }

    /// <summary>
    /// Places sample probes on opposite sides of each segment along the coastline
    /// </summary>
    private static void AddSideProbes(Coordinate[] line, Envelope frameEnvelope, List<Coordinate> seaProbes, List<Coordinate> landProbes)
    {
        for (int index = 1; index < line.Length; index++)
        {
            Coordinate start = line[index - 1];
            Coordinate end = line[index];
            double length = start.Distance(end);
            var middle = new Coordinate((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            if (length == 0 || !frameEnvelope.Contains(middle))
            {
                continue;
            }

            // The unit vector pointing right of the direction of travel.
            double rightX = (end.Y - start.Y) / length;
            double rightY = -(end.X - start.X) / length;
            seaProbes.Add(new Coordinate(middle.X + rightX * SideProbeOffsetM, middle.Y + rightY * SideProbeOffsetM));
            landProbes.Add(new Coordinate(middle.X - rightX * SideProbeOffsetM, middle.Y - rightY * SideProbeOffsetM));
        }
    }

    private static int[] CountProbesPerFace(List<Polygon> faces, List<Coordinate> probes)
    {
        // A spatial index, so each probe is tested against the few faces near it, not all of them.
        var faceIndex = new STRtree<int>();
        var preparedFaces = new List<IPreparedGeometry>();
        for (int faceNumber = 0; faceNumber < faces.Count; faceNumber++)
        {
            faceIndex.Insert(faces[faceNumber].EnvelopeInternal, faceNumber);
            preparedFaces.Add(PreparedGeometryFactory.Prepare(faces[faceNumber]));
        }

        int[] votes = new int[faces.Count];
        foreach (Coordinate probe in probes)
        {
            Point point = Factory.CreatePoint(probe);
            foreach (int faceNumber in faceIndex.Query(new Envelope(probe)))
            {
                if (preparedFaces[faceNumber].Contains(point))
                {
                    votes[faceNumber]++;
                    break;
                }
            }
        }
        return votes;
    }
}
