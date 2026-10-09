using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace MetroDisplay.Osm;

/// <summary>
/// Turns an Overpass response into the water the renderer fills.
/// </summary>
public static class WaterLayerBuilder
{
    /// <summary>
    /// Two hectares. At about 30 m per pixel on a 1080p display that is a 5 pixel blob;
    /// anything smaller reads as speckle.
    /// </summary>
    private const double MinimumAreaM2 = 20_000;

    /// <summary>
    /// About half a pixel on a 1080p display. Given as ground distance and converted below.
    /// </summary>
    private const double SimplifyToleranceM = 15;

    private static readonly GeometryFactory Factory = new();

    /// <param name="response">The Overpass answer for the water query.</param>
    /// <param name="frame">Plane rectangle to clip water to: the square around the city core.</param>
    /// <param name="railBounds">Plane rectangle the rail is normalized in. Water uses it too, so the layers line up; where <paramref name="frame"/> is larger, water lands outside [0, 1].</param>
    /// <param name="latitudeDegrees">Latitude used to convert ground sizes into plane units.</param>
    public static IReadOnlyList<WaterArea> Build(OverpassResponse response, ExtentRectangle frame, ExtentRectangle railBounds, double latitudeDegrees)
    {
        OsmWater water = OsmWater.From(response);
        Geometry frameArea = Factory.ToGeometry(new Envelope(frame.MinX, frame.MaxX, frame.MinY, frame.MaxY));

        // Mercator stretches lengths by 1 / cos(latitude), and so areas by its square.
        double groundPerPlane = Math.Cos(latitudeDegrees * Math.PI / 180.0);
        double minimumPlaneArea = MinimumAreaM2 / (groundPerPlane * groundPerPlane);
        double toleranceInPlaneUnits = SimplifyToleranceM / groundPerPlane;

        var areas = new List<WaterArea>();
        foreach (Geometry source in CoastlineCloser.CloseSea(water.CoastlineWays, frame).Concat(water.InlandWater))
        {
            Geometry clipped = source.Intersection(frameArea);
            for (int partNumber = 0; partNumber < clipped.NumGeometries; partNumber++)
            {
                if (clipped.GetGeometryN(partNumber) is not Polygon polygon || polygon.Area < minimumPlaneArea)
                {
                    continue;
                }

                // Topology-preserving, so a ring can neither cross itself nor an island leave its lake.
                if (TopologyPreservingSimplifier.Simplify(polygon, toleranceInPlaneUnits) is not Polygon { IsEmpty: false } simplified)
                {
                    continue;
                }

                var rings = new List<IReadOnlyList<double>> { Flatten(simplified.ExteriorRing, railBounds) };
                rings.AddRange(simplified.InteriorRings.Select(ring => Flatten(ring, railBounds)));
                areas.Add(new WaterArea(rings));
            }
        }
        return areas;
    }

    /// <summary>
    /// A ring as the wire carries it. NetTopologySuite repeats the first point at the end;
    /// the contract leaves rings implicitly closed, so that last point is dropped.
    /// </summary>
    private static IReadOnlyList<double> Flatten(LineString ring, ExtentRectangle railBounds)
        => CoordinateNormalizer.Flatten(ring.Coordinates.SkipLast(1).Select(coordinate => new PlanePoint(coordinate.X, coordinate.Y)), railBounds);
}
