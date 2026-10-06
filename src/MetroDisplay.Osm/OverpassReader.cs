using System.Text.Json;

namespace MetroDisplay.Osm;

/// <summary>
/// Reads an Overpass API answer, and refuses one that cannot be trusted.
/// </summary>
public static class OverpassReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <param name="json">The response body.</param>
    /// <exception cref="InvalidDataException">
    /// The body is not JSON, has no <c>elements</c> array, or carries a <c>remark</c>. Overpass
    /// answers 200 with a remark when a query times out, with partial or no data.
    /// </exception>
    public static OverpassResponse Read(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("elements", out JsonElement elements) || elements.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Overpass response has no elements array.");
            }
            if (root.TryGetProperty("remark", out JsonElement remark))
            {
                throw new InvalidDataException($"Overpass did not finish the query: {remark.GetString()}");
            }
            return new OverpassResponse(elements.Deserialize<List<OverpassElement>>(Options) ?? []);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Overpass response is not valid JSON: {exception.Message}", exception);
        }
    }
}
