using Xunit;

namespace MetroDisplay.Osm.Tests;

public class OverpassReaderTests
{
    [Fact]
    public void ReadsAWayWithItsTagsAndPoints()
    {
        OverpassResponse response = OverpassReader.Read("""
            {"version":0.6,"elements":[
              {"type":"way","id":1,"tags":{"natural":"water","name":"Jamaica Pond"},
               "geometry":[{"lat":42.3160,"lon":-71.1210},{"lat":42.3180,"lon":-71.1190}]}
            ]}
            """);

        OverpassElement way = Assert.Single(response.Elements);
        Assert.Equal("way", way.Type);
        Assert.Equal("water", way.Tags!["natural"]);
        Assert.Equal(new OverpassPoint(42.3180, -71.1190), way.Points![1]);
    }

    [Fact]
    public void ReadsRelationMembersWithTheirRoles()
    {
        OverpassResponse response = OverpassReader.Read("""
            {"elements":[
              {"type":"relation","id":2,"tags":{"natural":"water","type":"multipolygon"},"members":[
                {"type":"way","ref":3,"role":"outer","geometry":[{"lat":1,"lon":2},{"lat":3,"lon":4}]},
                {"type":"node","ref":4,"role":"label","lat":1,"lon":2}
              ]}
            ]}
            """);

        OverpassElement relation = Assert.Single(response.Elements);
        Assert.Equal(2, relation.Members!.Count);
        Assert.Equal("outer", relation.Members[0].Role);
        Assert.Equal(new OverpassPoint(3, 4), relation.Members[0].Points![1]);
        // A node member carries no line geometry.
        Assert.Null(relation.Members[1].Points);
    }

    [Fact]
    public void RejectsAResponseThatCarriesARemark()
    {
        var exception = Assert.Throws<InvalidDataException>(() => OverpassReader.Read(
            """{"elements":[],"remark":"runtime error: Query timed out in \"query\" at line 3 after 181 seconds."}"""));

        Assert.Contains("Query timed out", exception.Message);
    }

    [Fact]
    public void RejectsABodyThatIsNotJson()
    {
        var exception = Assert.Throws<InvalidDataException>(() => OverpassReader.Read("<html>The server is probably too busy</html>"));

        Assert.Contains("not valid JSON", exception.Message);
    }

    [Theory]
    [InlineData("""{"version":0.6}""")]
    [InlineData("""{"elements":{}}""")]
    [InlineData("[]")]
    public void RejectsJsonWithoutAnElementsArray(string json)
    {
        var exception = Assert.Throws<InvalidDataException>(() => OverpassReader.Read(json));

        Assert.Contains("no elements", exception.Message);
    }
}
