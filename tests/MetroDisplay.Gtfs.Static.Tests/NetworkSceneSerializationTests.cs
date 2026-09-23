using System.Text.Json;
using MetroDisplay.Contracts;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class NetworkSceneSerializationTests
{
    [Fact]
    public void SerializesWithTheFieldNamesTheRendererExpects()
    {
        var scene = new NetworkScene(
            ArtifactVersion: "mbta@2026-09-01.a3f1",
            City: new CityMetadata("mbta", "BOSTON", "MBTA", "America/New_York"),
            Extent: new ExtentInfo(Aspect: 1.34, CoreRadiusKm: 22, SpanKm: 44),
            Lines:
            [
                new LineScene("Red", "RED", "#DA291C",
                [
                    new ShapeGeometry("931_0009", [0.1043, 0.8812, 0.1121, 0.8790], 28140)
                ])
            ],
            Stations: [new StationMarker(0.412, 0.331, "Park St", 2)],
            EdgeLabels: [new EdgeLabel(0.998, 0.402, "TO ALEWIFE", -12.4, "Red")]);

        string json = JsonSerializer.Serialize(scene, JsonDefaults.Options);

        Assert.Contains("\"artifactVersion\":\"mbta@2026-09-01.a3f1\"", json);
        Assert.Contains("\"city\":{\"id\":\"mbta\",\"name\":\"BOSTON\"", json);
        Assert.Contains("\"extent\":{\"aspect\":1.34,\"coreRadiusKm\":22,\"spanKm\":44}", json);
        Assert.Contains("\"points\":[0.1043,0.8812,0.1121,0.879]", json);
        Assert.Contains("\"lengthM\":28140", json);
        Assert.Contains("\"edgeLabels\":[{\"x\":0.998,\"y\":0.402,\"text\":\"TO ALEWIFE\"", json);
        Assert.DoesNotContain("\"frame\"", json);
    }
}
