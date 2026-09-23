using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroDisplay.Contracts;

/// <summary>
/// The one serializer configuration in the system. Every artifact written and every
/// message sent uses it, so wire field names are decided in exactly one place.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
