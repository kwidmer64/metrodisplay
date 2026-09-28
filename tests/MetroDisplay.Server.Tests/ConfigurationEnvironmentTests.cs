using Microsoft.Extensions.Configuration;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class ConfigurationEnvironmentTests
{
    [Fact]
    public void ExposesConfigurationValuesAsPlaceholderValues()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MBTA_API_KEY"] = "from-configuration",
                ["MetroDisplay:CityId"] = "mbta",
            })
            .Build();

        IReadOnlyDictionary<string, string> environment = ConfigurationEnvironment.From(configuration);

        Assert.Equal("from-configuration", environment["MBTA_API_KEY"]);
        // Keys match without regard to case, as environment variables do on Windows.
        Assert.Equal("from-configuration", environment["mbta_api_key"]);
        // A section node has no value of its own and is left out.
        Assert.False(environment.ContainsKey("MetroDisplay"));
    }
}
