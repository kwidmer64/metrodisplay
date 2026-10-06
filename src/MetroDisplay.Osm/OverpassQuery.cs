using System.Globalization;

namespace MetroDisplay.Osm;

/// <summary>
/// Overpass QL queries, one per layer.
/// </summary>
public static class OverpassQuery
{
    /// <summary>
    /// Coastline, lakes and rivers touching a box, with every vertex included (<c>out geom</c>).
    /// Overpass returns whole ways, so coastline runs past the box and is clipped later.
    /// </summary>
    public static string Water(GeoBox box)
    {
        // Invariant culture: a comma as the decimal separator would change the query's meaning.
        string area = string.Create(CultureInfo.InvariantCulture, $"({box.South:F5},{box.West:F5},{box.North:F5},{box.East:F5})");
        return $"""
            [out:json][timeout:180];
            (
              way["natural"="coastline"]{area};
              way["natural"="water"]{area};
              relation["natural"="water"]{area};
              way["waterway"="riverbank"]{area};
              relation["waterway"="riverbank"]{area};
            );
            out geom;
            """;
    }
}
