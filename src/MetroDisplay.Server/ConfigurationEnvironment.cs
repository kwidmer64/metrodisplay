namespace MetroDisplay.Server;

/// <summary>
/// Adapts configuration into the placeholder values <c>CityConfigLoader.Load</c> takes.
/// Configuration already merges environment variables, user-secrets in Development, and app
/// settings, so a key set in any of them resolves a <c>${KEY}</c> placeholder (spec §11).
/// </summary>
public static class ConfigurationEnvironment
{
    public static IReadOnlyDictionary<string, string> From(IConfiguration configuration)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string? value) in configuration.AsEnumerable())
        {
            if (value is not null)
            {
                values[key] = value;
            }
        }
        return values;
    }
}
