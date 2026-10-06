using System.Text.Json.Serialization;

namespace MetroDisplay.Osm;

/// <summary>
/// The body of an Overpass API answer.
/// </summary>
/// <param name="Elements">Every way and relation the query matched.</param>
public sealed record OverpassResponse(IReadOnlyList<OverpassElement> Elements);

/// <summary>
/// One OpenStreetMap way or relation.
/// </summary>
/// <param name="Type"><c>way</c> or <c>relation</c>.</param>
/// <param name="Tags">OSM tags, such as <c>natural=water</c>. Absent on untagged elements.</param>
/// <param name="Points">A way's vertices in order. Absent on relations.</param>
/// <param name="Members">A relation's parts. Absent on ways.</param>
public sealed record OverpassElement(string Type, IReadOnlyDictionary<string, string>? Tags = null, [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassPoint>? Points = null, IReadOnlyList<OverpassMember>? Members = null);

/// <summary>
/// One part of a relation.
/// </summary>
/// <param name="Type"><c>way</c>, <c>node</c> or <c>relation</c>. Only ways carry a line.</param>
/// <param name="Role"><c>outer</c> for an outline, <c>inner</c> for a hole.</param>
/// <param name="Points">The member way's vertices in order. Absent on node members.</param>
public sealed record OverpassMember(string Type, string Role, [property: JsonPropertyName("geometry")] IReadOnlyList<OverpassPoint>? Points = null);

/// <summary>
/// A vertex as OpenStreetMap stores it.
/// </summary>
/// <param name="Latitude">Degrees north of the equator.</param>
/// <param name="Longitude">Degrees east of Greenwich.</param>
public sealed record OverpassPoint([property: JsonPropertyName("lat")] double Latitude, [property: JsonPropertyName("lon")] double Longitude);
