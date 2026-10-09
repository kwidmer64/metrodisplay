using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace MetroDisplay.Osm;

/// <summary>
/// The water in an Overpass response, projected to the plane and sorted by how it is drawn.
/// </summary>
/// <param name="CoastlineWays">Coastline ways in OSM's direction, still open. <see cref="CoastlineCloser"/> turns them into sea.</param>
/// <param name="InlandWater">Lakes, ponds and river banks as polygons, islands already cut out.</param>
public sealed record OsmWater(IReadOnlyList<IReadOnlyList<PlanePoint>> CoastlineWays, IReadOnlyList<Geometry> InlandWater)
{
    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Parse raw OpenStreetMap data into ocean coastlines and inland water bodies
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    public static OsmWater From(OverpassResponse response)
    {
        List<IReadOnlyList<PlanePoint>> coastlineWays = new();
        List<Geometry> inlandWater = new();
        foreach (OverpassElement element in response.Elements)
        {
            string natural = Tag(element, "natural");
            if (element.Type == "way" && natural == "coastline" && element.Points is { Count: >= 2 } coast)
            {
                coastlineWays.Add(coast.Select(Project).ToList());
            }
            else if (natural == "water" || Tag(element, "waterway") == "riverbank")
            {
                Geometry? area = element.Type == "relation" ? FromRelation(element) : FromWay(element);
                if (area is { IsEmpty: false })
                {
                    // Buffer(0) repairs outlines that touch or cross themselves, which OSM contains.
                    inlandWater.Add(area.IsValid ? area : area.Buffer(0));
                }
            }
        }
        return new OsmWater(coastlineWays, inlandWater);
    }

    /// <summary>
    /// Gets the tag value for the specified element
    /// </summary>
    /// <param name="element"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    private static string Tag(OverpassElement element, string key) => element.Tags is not null && element.Tags.TryGetValue(key, out string? value) ? value : "";

    /// <summary>
    /// Projects an OverpassPoint to a PlanePoint
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    private static PlanePoint Project(OverpassPoint point) => MercatorProjector.Project(new GeoPoint(point.Latitude, point.Longitude));

    private static Coordinate[] Coordinates(IReadOnlyList<OverpassPoint> points)
        => points.Select(Project).Select(point => new Coordinate(point.X, point.Y)).ToArray();

    /// <summary>
    /// A closed way is a polygon. An open one cannot be filled and is skipped.
    /// </summary>
    private static Geometry? FromWay(OverpassElement way)
    {
        if (way.Points is not { Count: >= 4 } points)
        {
            return null;
        }
        Coordinate[] ring = Coordinates(points);
        return ring[0].Equals2D(ring[^1]) ? Factory.CreatePolygon(ring) : null;
    }

    /// <summary>
    /// A multipolygon relation: outer members form the outline, often split across several
    /// ways, and inner members are islands cut out of it.
    /// </summary>
    private static Geometry FromRelation(OverpassElement relation)
    {
        var outers = new Polygonizer();
        var inners = new Polygonizer();
        foreach (OverpassMember member in relation.Members ?? [])
        {
            if (member.Type == "way" && member.Points is { Count: >= 2 } points)
            {
                (member.Role == "inner" ? inners : outers).Add(Factory.CreateLineString(Coordinates(points)));
            }
        }

        Geometry outline = Factory.BuildGeometry(outers.GetPolygons()).Union();
        Geometry islands = Factory.BuildGeometry(inners.GetPolygons()).Union();
        return islands.IsEmpty ? outline : outline.Difference(islands);
    }
}
