using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads a city config, resolving ${VAR} placeholders from the supplied environment.
/// Interpolation happens on the raw text before deserialization so a placeholder can
/// appear in any string value, not only in a known field.
/// </summary>
public static partial class CityConfigLoader
{
    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>
    /// The shared wire options, made strict. City configs are written by hand, so a missing or
    /// misspelled setting has to fail here instead of loading as zero or null.
    /// </summary>
    private static readonly JsonSerializerOptions StrictOptions = new(JsonDefaults.Options)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Resolves placeholders in <paramref name="json"/> and deserializes the result.
    /// Used for API keys or other secrets.
    /// </summary>
    /// <param name="json">Raw contents of a city config file.</param>
    /// <param name="environment">Values for <c>${VAR}</c> placeholders, keyed by variable name.</param>
    /// <returns>The deserialized config, with every placeholder replaced.</returns>
    /// <exception cref="InvalidOperationException">A placeholder names a variable that <paramref name="environment"/> does not contain.</exception>
    /// <exception cref="JsonException">The JSON is malformed, omits a required setting, or contains a setting that does not exist.</exception>
    public static CityConfig Load(string json, IReadOnlyDictionary<string, string> environment)
    {
        // The resolved JSON string
        string resolved = PlaceholderPattern.Replace(json, match =>
        {
            // Get the name of the variable. Groups[0] contains the entire match, ${VAR}. Groups[1] would just be VAR
            string variableName = match.Groups[1].Value;

            // Try to retrieve the value for the variable from the dictionary
            if (!environment.TryGetValue(variableName, out string? value))
            {
                throw new InvalidOperationException($"City config references ${{{variableName}}} but no such environment variable is set.");
            }

            // Encode the result to json
            return JsonEncodedText.Encode(value).ToString();
        });

        // Deserialize the JSON string into a CityConfig object
        return JsonSerializer.Deserialize<CityConfig>(resolved, StrictOptions) ?? throw new InvalidOperationException("City config deserialized to null.");
    }

    /// <summary>
    /// <see cref="Load"/> with placeholders resolved from environment variables.
    /// Pulls values from environment variables then passes to <see cref="Load"/>
    /// </summary>
    /// <param name="json">Raw contents of a city config file.</param>
    /// <returns>The deserialized config, with every placeholder replaced.</returns>
    public static CityConfig LoadFromProcessEnvironment(string json)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>() // Cast the environment variables to a dictionary
            .ToDictionary(entry => (string)entry.Key, // Casts the key object to a string
                          entry => (string?)entry.Value ?? string.Empty); // Casts the value object to nullable string / empty string if object is null
        return Load(json, environment);
    }
}
