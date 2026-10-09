# Slice 2: Water and Coastline Implementation Plan

**Goal:** Boston's harbour, rivers and lakes filled behind the rail lines, from OpenStreetMap
data fetched once and kept.

**Architecture:** A new `MetroDisplay.Osm` library turns a raw Overpass response into
`WaterArea`s: it overlays coastline ways on the frame's edge and closes them into sea
polygons, assembles lakes and rivers, clips, filters, simplifies and normalizes. The
Server fetches the response once per city into `.cache/osm/`, builds the layer in the rail
network's frame, and adds it to the scene. Water never stops startup: any failure yields a
map without water. The renderer fills each area before it strokes the lines.

**Tech Stack:** .NET 10, C#, xUnit 2.9.3, NetTopologySuite 2.6.0, System.Text.Json; Vite 8,
TypeScript 6, Vitest 5, canvas2d.

**Spec:** `docs/specs/2026-09-22-metrodisplay-design.md`: §14 "Water and coastline (slice 2)",
§8 (`water` in the `network` message), §10 (the sixth renderer operation, layers), §12 (the two
water rows), §13 (`Osm` testing).

**Verified before writing (2026-10-06):** every geometry step below ran in a scratch app
against the real Overpass response for Boston's core square (13.6 MB, 413 coastline ways,
1,862 water elements). Result: 254 areas, 12,629 vertices, 179 KB of JSON, about 2 s, with
the fixture expectations in Tasks 4 and 5 passing.

## Global Constraints

- Target framework `net10.0`, SDK 10.0.401. Node 24, npm 11.
- **The developer commits.** Every task ends with a hand-off step that supplies a commit
  message. No step runs `git add`, `git commit`, or any other git write.
- Commit messages: Conventional Commits prefix, imperative subject of 50 characters or
  fewer, body only when the why is not obvious, no trailers of any kind.
- **Naming:** readable words only. No single letters and no two- or three-character stubs,
  including locals, lambda parameters and generics. `X` and `Y` only as members of a
  coordinate type. OSM's wire names `lat` and `lon` appear only in `JsonPropertyName`.
- **Unit suffixes** on every unit-bearing name: `radiusKm`, `MinimumAreaM2`, `SimplifyToleranceM`.
- Wire field names come from `MetroDisplay.Contracts` through `JsonDefaults.Options`.
- Public types carry `<summary>`/`<param>` doc comments in the style of `CityConfig.cs`.
- TDD: write the failing test, watch it fail, write the minimal code, watch it pass.
- Builds finish with zero warnings.
- **Rail still fails fast. Water never does** (spec §12, §14).

## Review Focus

The five inputs most likely to bite that the spec implies but does not spell out. Each has a
pinned test in the task that owns the code.

1. **Coastline drawn the other way round.** OSM keeps land on the left of a way's direction.
   Reading it backwards floods the land and dries the sea.
   → `PutsTheSeaWestOfASouthboundCoast` (Task 4)
2. **Coastline split into many ways, in any order.** Boston's shore arrives as 413 ways,
   and they must meet up whatever order they come in. → `ClosesACoastSplitIntoWaysInAnyOrder` (Task 4)
3. **Overpass answering 200 with a `remark`.** That is how it reports a query that timed out,
   with partial or no data. Caching it would lose the water for good.
   → `CachesNothingWhenOverpassReportsARemark` (Task 6)
4. **A machine whose culture writes decimals with a comma.** The query would ask Overpass for
   `42,15765` and get nothing. → `WritesCoordinatesWithDecimalPointsWhateverTheCulture` (Task 3)
5. **Overpass unreachable, or a cached layer that cannot be read.** The map must still come
   up, without water, and say which file to delete.
   → `StartsWithoutWaterWhenOverpassIsUnreachable`, `StartsWithoutWaterWhenTheCachedLayerIsUnreadable` (Task 6)

## File Structure

```
src/MetroDisplay.Spatial/             new; the six files moved out of Gtfs.Static/Geometry
  GeoPoint.cs  PlanePoint.cs  MercatorProjector.cs  GroundDistance.cs
  ExtentRectangle.cs  CoordinateNormalizer.cs

src/MetroDisplay.Contracts/
  NetworkScene.cs                     + WaterArea, NetworkScene.Water

src/MetroDisplay.Gtfs.Static/
  Pipeline/NetworkSceneBuilder.cs     + RailLayer, BuildLayer

src/MetroDisplay.Osm/                 new
  Overpass.cs                         OverpassResponse, OverpassElement, OverpassMember, OverpassPoint
  OverpassReader.cs                   JSON -> OverpassResponse; rejects a remark
  GeoBox.cs                           a latitude/longitude box around a point
  OverpassQuery.cs                    the water query for a box
  CoastlineCloser.cs                  coastline ways + frame -> sea polygons
  OsmWater.cs                         response -> coastline ways + inland water polygons
  WaterLayerBuilder.cs                response + frames -> WaterArea list

src/MetroDisplay.Server/
  Feeds/OsmLayerCache.cs              fetch a layer once, keep it
  WaterLayerLoader.cs                 fetch + build, degrading to no water
  ServerSettings.cs                   + OsmCacheDirectory, OverpassUrl
  Program.cs  appsettings.json

tests/MetroDisplay.Spatial.Tests/     new; the four geometry test files moved here
tests/MetroDisplay.Osm.Tests/         new
  OverpassReaderTests.cs  GeoBoxTests.cs  OverpassQueryTests.cs
  CoastlineCloserTests.cs  OsmWaterTests.cs  WaterLayerBuilderTests.cs
tests/MetroDisplay.Server.Tests/
  OsmLayerCacheTests.cs               new
  NetworkEndpointTests.cs             + three water tests

web/src/
  contract.ts                         + WaterArea, water
  draw.ts  draw.test.ts               + drawWater and its tests
  main.ts                             water first, then lines; attribution
```

---

### Task 1: Shared spatial project

**Files:**
- Create: `src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj`
- Move: `src/MetroDisplay.Gtfs.Static/Geometry/*.cs` → `src/MetroDisplay.Spatial/`
- Create: `tests/MetroDisplay.Spatial.Tests/MetroDisplay.Spatial.Tests.csproj`
- Move: `MercatorProjectorTests.cs`, `GroundDistanceTests.cs`, `ExtentRectangleTests.cs`,
  `CoordinateNormalizerTests.cs` → `tests/MetroDisplay.Spatial.Tests/`
- Modify: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs` (one `using`)
- Modify: `MetroDisplay.slnx`, `docs/specs/2026-09-22-metrodisplay-design.md` §3

**Interfaces:**
- Consumes: nothing new.
- Produces: namespace `MetroDisplay.Spatial` holding `GeoPoint`, `PlanePoint`,
  `MercatorProjector`, `GroundDistance`, `ExtentRectangle`, `CoordinateNormalizer`, unchanged.

This is a move, not a change. `MetroDisplay.Osm` needs projection and normalization, and the
spec keeps it from depending on the GTFS project. The name is `Spatial`, not `Geometry`:
NetTopologySuite's central type is `Geometry`, and a `MetroDisplay.Geometry` namespace would
shadow it inside `MetroDisplay.Osm`. There is no new test; the existing 65 are the safety net.

- [x] **Step 1: Confirm the starting point**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 65 tests (56 in `MetroDisplay.Gtfs.Static.Tests`, 9 in `MetroDisplay.Server.Tests`).

- [x] **Step 2: Create the projects and move the files**

```bash
dotnet new classlib -o src/MetroDisplay.Spatial
rm src/MetroDisplay.Spatial/Class1.cs
dotnet sln MetroDisplay.slnx add src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj
mv src/MetroDisplay.Gtfs.Static/Geometry/*.cs src/MetroDisplay.Spatial/
rmdir src/MetroDisplay.Gtfs.Static/Geometry
dotnet add src/MetroDisplay.Gtfs.Static reference src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj

dotnet new xunit -o tests/MetroDisplay.Spatial.Tests
rm tests/MetroDisplay.Spatial.Tests/UnitTest1.cs
dotnet sln MetroDisplay.slnx add tests/MetroDisplay.Spatial.Tests/MetroDisplay.Spatial.Tests.csproj
dotnet add tests/MetroDisplay.Spatial.Tests reference src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj
for name in MercatorProjector GroundDistance ExtentRectangle CoordinateNormalizer; do
  mv "tests/MetroDisplay.Gtfs.Static.Tests/${name}Tests.cs" tests/MetroDisplay.Spatial.Tests/
done
```

- [x] **Step 3: Rename the namespaces**

```bash
sed -i 's/^namespace MetroDisplay\.Gtfs\.Static\.Geometry;/namespace MetroDisplay.Spatial;/' src/MetroDisplay.Spatial/*.cs
sed -i 's/^using MetroDisplay\.Gtfs\.Static\.Geometry;/using MetroDisplay.Spatial;/; s/^namespace MetroDisplay\.Gtfs\.Static\.Tests;/namespace MetroDisplay.Spatial.Tests;/' tests/MetroDisplay.Spatial.Tests/*Tests.cs
sed -i 's/^using MetroDisplay\.Gtfs\.Static\.Geometry;/using MetroDisplay.Spatial;/' src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs
```

Check that nothing still names the old namespace:

```bash
grep -rn "Gtfs.Static.Geometry" src tests --include=*.cs
```

Expected: no output.

- [x] **Step 4: Run the suite and confirm nothing changed but where tests live**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, still 65 tests: 20 in `MetroDisplay.Spatial.Tests`, 36 in
`MetroDisplay.Gtfs.Static.Tests`, 9 in `MetroDisplay.Server.Tests`.

- [x] **Step 5: Record the project in spec §3**

In the Projects block of `docs/specs/2026-09-22-metrodisplay-design.md`, add after the
`MetroDisplay.Contracts` line:

```
MetroDisplay.Spatial        Projection, ground distance, bounds, normalization. Zero dependencies.
```

- [x] **Step 6: Hand off for commit**

```
refactor: move shared geometry into Spatial

The OpenStreetMap layers need the same projection and normalization
as the rail, without depending on the GTFS project. Named Spatial
because a Geometry namespace would shadow NetTopologySuite's type.
```

---

### Task 2: Water in the contract, and the rail frame exposed

**Files:**
- Modify: `src/MetroDisplay.Contracts/NetworkScene.cs`
- Modify: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneSerializationTests.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneBuilderTests.cs`

**Interfaces:**
- Consumes: `ExtentRectangle`, `MercatorProjector`, `GeoPoint`, `PlanePoint` (`MetroDisplay.Spatial`).
- Produces: `WaterArea(IReadOnlyList<IReadOnlyList<double>> Rings)`; `NetworkScene` gains a
  last parameter `IReadOnlyList<WaterArea> Water`;
  `RailLayer(NetworkScene Scene, ExtentRectangle Bounds)`;
  `NetworkSceneBuilder.BuildLayer(byte[] zipBytes, CityConfig config) -> RailLayer`.
  `NetworkSceneBuilder.Build` stays and returns `BuildLayer(...).Scene`.

- [x] **Step 1: Write the failing tests**

In `NetworkSceneSerializationTests.cs`, add a `Water` argument to the scene and one assertion:

```csharp
            Stations: [new StationMarker(0.412, 0.331, "Park St", 2)],
            EdgeLabels: [new EdgeLabel(0.998, 0.402, "TO ALEWIFE", -12.4, "Red")],
            Water: [new WaterArea([[0.61, 0.2, 0.64, 0.21, 0.62, 0.25], [0.62, 0.21, 0.63, 0.21, 0.62, 0.22]])]);
```

```csharp
        Assert.Contains("\"water\":[{\"rings\":[[0.61,0.2,0.64,0.21,0.62,0.25],[0.62,0.21,0.63,0.21,0.62,0.22]]}]", json);
```

In `NetworkSceneBuilderTests.cs`, add `using MetroDisplay.Spatial;` and this test:

```csharp
    [Fact]
    public void ExposesTheBoundsItsCoordinatesAreNormalizedIn()
    {
        // The fixture's rail runs 42.32..42.36 N and 71.12..71.06 W. Other layers are
        // normalized with these same bounds so they line up with the rail.
        RailLayer layer = NetworkSceneBuilder.BuildLayer(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), TestConfig());

        PlanePoint southWest = MercatorProjector.Project(new GeoPoint(42.32, -71.12));
        PlanePoint northEast = MercatorProjector.Project(new GeoPoint(42.36, -71.06));
        Assert.Equal(new ExtentRectangle(southWest.X, southWest.Y, northEast.X, northEast.Y), layer.Bounds);
        Assert.Empty(layer.Scene.Water);
    }
```

- [x] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: build errors: CS0246 `The type or namespace name 'WaterArea' could not be found`,
and likewise `'RailLayer'`.

- [x] **Step 3: Add water to the contract**

In `src/MetroDisplay.Contracts/NetworkScene.cs`, add a `<param>` line and a last parameter to
`NetworkScene`:

```csharp
/// <param name="Water">Water bodies drawn behind the lines. Empty when water is unavailable.</param>
public sealed record NetworkScene(string ArtifactVersion, CityMetadata City, ExtentInfo Extent, IReadOnlyList<LineScene> Lines, IReadOnlyList<StationMarker> Stations, IReadOnlyList<EdgeLabel> EdgeLabels, IReadOnlyList<WaterArea> Water);
```

and at the end of the file:

```csharp

/// <summary>
/// One body of water: its outline and any islands inside it.
/// </summary>
/// <param name="Rings">The outline first, then one ring per island. Each ring is a flat [x0,y0,x1,y1,...] array in the normalized extent space and is implicitly closed. Values fall outside [0,1] where water runs past the extent.</param>
public sealed record WaterArea(IReadOnlyList<IReadOnlyList<double>> Rings);
```

- [x] **Step 4: Expose the rail frame**

In `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`:

Add the record above the `NetworkSceneBuilder` class:

```csharp
/// <summary>
/// The rail scene together with the frame its coordinates are normalized in.
/// </summary>
/// <param name="Scene">The scene, with no water yet.</param>
/// <param name="Bounds">Plane rectangle every normalized coordinate in <paramref name="Scene"/> is relative to. Other layers normalize with it so they line up with the rail.</param>
public sealed record RailLayer(NetworkScene Scene, ExtentRectangle Bounds);
```

Rename the existing `Build` method to `BuildLayer`, change its return type to `RailLayer`,
and change its final statement from `return new NetworkScene(` … `EdgeLabels: []);` to:

```csharp
        var scene = new NetworkScene(
            ArtifactVersion: ContentVersion(config.Id, zipBytes),
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: extent,
            Lines: BuildLines(selection, geoPointsByShapeId, planePointsByShapeId, bounds),
            Stations: [],
            EdgeLabels: [],
            Water: []);

        return new RailLayer(scene, bounds);
```

Keep the existing doc comment on `BuildLayer`, and add `Build` back above it:

```csharp
    /// <summary>
    /// The rail scene alone, for callers that need no other layer.
    /// </summary>
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column.</exception>
    public static NetworkScene Build(byte[] zipBytes, CityConfig config) => BuildLayer(zipBytes, config).Scene;
```

- [x] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 66 tests (20 Spatial, 37 Gtfs.Static, 9 Server).

- [x] **Step 6: Hand off for commit**

```
feat: add water to the scene contract

NetworkScene carries a list of water areas, each an outline plus
island rings. The rail builder also returns the bounds it
normalized with, so other layers can share its frame.
```

---

### Task 3: Reading Overpass

**Files:**
- Create: `src/MetroDisplay.Osm/MetroDisplay.Osm.csproj`
- Create: `src/MetroDisplay.Osm/Overpass.cs`, `OverpassReader.cs`, `GeoBox.cs`, `OverpassQuery.cs`
- Create: `tests/MetroDisplay.Osm.Tests/MetroDisplay.Osm.Tests.csproj`
- Test: `tests/MetroDisplay.Osm.Tests/OverpassReaderTests.cs`, `GeoBoxTests.cs`, `OverpassQueryTests.cs`

**Interfaces:**
- Consumes: `GeoPoint`, `MercatorProjector`, `ExtentRectangle` (`MetroDisplay.Spatial`).
- Produces:
  `OverpassResponse(IReadOnlyList<OverpassElement> Elements)`;
  `OverpassElement(string Type, IReadOnlyDictionary<string, string>? Tags = null, IReadOnlyList<OverpassPoint>? Points = null, IReadOnlyList<OverpassMember>? Members = null)`;
  `OverpassMember(string Type, string Role, IReadOnlyList<OverpassPoint>? Points = null)`;
  `OverpassPoint(double Latitude, double Longitude)`;
  `OverpassReader.Read(string json) -> OverpassResponse`, throwing `InvalidDataException`;
  `GeoBox(double South, double West, double North, double East)` with `CentreLatitude`,
  `static GeoBox Around(GeoPoint centre, double radiusKm)` and `ExtentRectangle ToPlane()`;
  `OverpassQuery.Water(GeoBox box) -> string`.

- [x] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/MetroDisplay.Osm
rm src/MetroDisplay.Osm/Class1.cs
dotnet sln MetroDisplay.slnx add src/MetroDisplay.Osm/MetroDisplay.Osm.csproj
dotnet add src/MetroDisplay.Osm reference src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj src/MetroDisplay.Contracts/MetroDisplay.Contracts.csproj
dotnet add src/MetroDisplay.Osm package NetTopologySuite --version 2.6.0

dotnet new xunit -o tests/MetroDisplay.Osm.Tests
rm tests/MetroDisplay.Osm.Tests/UnitTest1.cs
dotnet sln MetroDisplay.slnx add tests/MetroDisplay.Osm.Tests/MetroDisplay.Osm.Tests.csproj
dotnet add tests/MetroDisplay.Osm.Tests reference src/MetroDisplay.Osm/MetroDisplay.Osm.csproj
```

- [x] **Step 2: Write the failing tests**

`tests/MetroDisplay.Osm.Tests/OverpassReaderTests.cs`:

```csharp
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
```

`tests/MetroDisplay.Osm.Tests/GeoBoxTests.cs`:

```csharp
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class GeoBoxTests
{
    [Fact]
    public void ReachesTheRadiusOnTheGroundInEveryDirection()
    {
        // 22 km is 0.19785 degrees of latitude. At Boston's latitude a degree of longitude is
        // shorter by cos(42.3555), so the same 22 km is 0.26773 degrees of longitude.
        GeoBox box = GeoBox.Around(new GeoPoint(42.3555, -71.0605), radiusKm: 22);

        Assert.Equal(42.15765, box.South, precision: 5);
        Assert.Equal(42.55335, box.North, precision: 5);
        Assert.Equal(-71.32823, box.West, precision: 5);
        Assert.Equal(-70.79277, box.East, precision: 5);
        Assert.Equal(42.3555, box.CentreLatitude, precision: 5);
    }

    [Fact]
    public void IsSquareOnTheProjectedPlane()
    {
        ExtentRectangle plane = GeoBox.Around(new GeoPoint(42.3555, -71.0605), radiusKm: 22).ToPlane();

        Assert.Equal(1.0, plane.Aspect, precision: 3);
        // 44 km on the ground is about 59.5 km of plane distance at this latitude. The 100 m
        // allowance covers the two radii in play: the box is sized with the mean Earth radius,
        // and Web Mercator projects with the equatorial one, 0.11% larger (about 67 m here).
        Assert.Equal(44_000 / Math.Cos(42.3555 * Math.PI / 180), plane.Width, tolerance: 100);
    }
}
```

`tests/MetroDisplay.Osm.Tests/OverpassQueryTests.cs`:

```csharp
using System.Globalization;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class OverpassQueryTests
{
    private static readonly GeoBox BostonBox = new(South: 42.15765, West: -71.32823, North: 42.55335, East: -70.79277);
    private const string BostonArea = "(42.15765,-71.32823,42.55335,-70.79277)";

    [Fact]
    public void AsksForCoastlineAndWaterInsideTheBoxWithGeometry()
    {
        string query = OverpassQuery.Water(BostonBox);

        Assert.Contains($"way[\"natural\"=\"coastline\"]{BostonArea};", query);
        Assert.Contains($"way[\"natural\"=\"water\"]{BostonArea};", query);
        Assert.Contains($"relation[\"natural\"=\"water\"]{BostonArea};", query);
        Assert.Contains($"way[\"waterway\"=\"riverbank\"]{BostonArea};", query);
        Assert.Contains($"relation[\"waterway\"=\"riverbank\"]{BostonArea};", query);
        // Overpass gives up after 25 s unless told otherwise, and a city's water takes longer.
        Assert.Contains("[out:json][timeout:180];", query);
        Assert.Contains("out geom;", query);
    }

    [Fact]
    public void WritesCoordinatesWithDecimalPointsWhateverTheCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // German writes 42,15765. Overpass would read that as two numbers.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Contains(BostonArea, OverpassQuery.Water(BostonBox));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
```

- [x] **Step 3: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Osm.Tests
```

Expected: build error CS0246, `The type or namespace name 'GeoBox' could not be found`.
The compiler stops at the field declarations before it reaches the method bodies.

- [x] **Step 4: Write the response records and the reader**

`src/MetroDisplay.Osm/Overpass.cs`:

```csharp
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
```

`src/MetroDisplay.Osm/OverpassReader.cs`:

```csharp
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
```

- [x] **Step 5: Write the box and the query**

`src/MetroDisplay.Osm/GeoBox.cs`:

```csharp
using MetroDisplay.Spatial;

namespace MetroDisplay.Osm;

/// <summary>
/// A box of latitude and longitude, as Overpass takes its search area.
/// </summary>
/// <param name="South">Southern edge, degrees.</param>
/// <param name="West">Western edge, degrees.</param>
/// <param name="North">Northern edge, degrees.</param>
/// <param name="East">Eastern edge, degrees.</param>
public readonly record struct GeoBox(double South, double West, double North, double East)
{
    /// <summary>
    /// Ground metres in one degree of latitude on the mean-radius sphere, as GroundDistance uses.
    /// </summary>
    private const double MetresPerDegree = 6371008.8 * Math.PI / 180.0;

    public double CentreLatitude => (South + North) / 2.0;

    /// <summary>
    /// The box reaching <paramref name="radiusKm"/> of ground distance from a centre in each
    /// direction. A degree of longitude shrinks with latitude, so the box is wider in degrees
    /// than it is tall.
    /// </summary>
    public static GeoBox Around(GeoPoint centre, double radiusKm)
    {
        double latitudeSpan = radiusKm * 1000.0 / MetresPerDegree;
        double longitudeSpan = latitudeSpan / Math.Cos(centre.Latitude * Math.PI / 180.0);
        return new GeoBox(centre.Latitude - latitudeSpan, centre.Longitude - longitudeSpan, centre.Latitude + latitudeSpan, centre.Longitude + longitudeSpan);
    }

    /// <summary>
    /// The same box in Web Mercator plane units.
    /// </summary>
    public ExtentRectangle ToPlane()
    {
        PlanePoint southWest = MercatorProjector.Project(new GeoPoint(South, West));
        PlanePoint northEast = MercatorProjector.Project(new GeoPoint(North, East));
        return new ExtentRectangle(southWest.X, southWest.Y, northEast.X, northEast.Y);
    }
}
```

`src/MetroDisplay.Osm/OverpassQuery.cs`:

```csharp
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
```

- [x] **Step 6: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Osm.Tests
```

Expected: PASS, 11 tests.

- [x] **Step 7: Hand off for commit**

```
feat: read Overpass responses and build the water query

A response carrying a remark is refused, since that is how
Overpass reports a timed-out query with partial data. The query
is written with invariant numbers so it means the same everywhere.
```

---

### Task 4: Closing the coastline into sea

**Files:**
- Create: `src/MetroDisplay.Osm/CoastlineCloser.cs`
- Test: `tests/MetroDisplay.Osm.Tests/CoastlineCloserTests.cs`

**Interfaces:**
- Consumes: `PlanePoint`, `ExtentRectangle` (`MetroDisplay.Spatial`); NetTopologySuite.
- Produces: `CoastlineCloser.CloseSea(IReadOnlyList<IReadOnlyList<PlanePoint>> coastlineWays, ExtentRectangle frame) -> IReadOnlyList<Polygon>`
  (`NetTopologySuite.Geometries.Polygon`), every polygon inside the frame.

How it works. OSM coastline is a set of open ways with **land on the left** of their
direction. The ways, cut to the frame, are overlaid with the frame's edge and polygonized,
which splits the frame into faces: some land, some sea. The overlay joins ways wherever they
share an end point, so they need no stitching first: joining them into chains beforehand
gave byte-identical output on the real Boston response. To tell which is which, every coastline segment inside the
frame drops two probes half a plane unit either side of its midpoint: the one on its right is
in the sea, the one on its left is on land. A face is sea when more sea probes than land
probes fall in it, and a face no probe reaches stays land. The probes come from the original
ways, never from the clipped pieces, because clipping does not promise to keep a line's
direction.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Osm.Tests/CoastlineCloserTests.cs`:

```csharp
using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class CoastlineCloserTests
{
    /// <summary>A 100 by 100 map, so areas read as percentages of ten thousand.</summary>
    private static readonly ExtentRectangle Frame = new(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

    private static IReadOnlyList<PlanePoint> Way(params (double X, double Y)[] points) =>
        points.Select(point => new PlanePoint(point.X, point.Y)).ToList();

    private static Point At(double planeX, double planeY) => new(planeX, planeY);

    [Fact]
    public void PutsTheSeaEastOfANorthboundCoast()
    {
        // Heading north, land is on the left, the west.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea([Way((50, -10), (50, 110))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(75, 50)));
    }

    [Fact]
    public void PutsTheSeaWestOfASouthboundCoast()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea([Way((50, 110), (50, -10))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(25, 50)));
    }

    [Fact]
    public void ClosesACoastSplitIntoWaysInAnyOrder()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, 40), (50, 110)), Way((50, -10), (50, 40))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000, area.Area, precision: 6);
        Assert.True(area.Contains(At(75, 50)));
    }

    [Fact]
    public void LeavesAnIslandAsAHoleInTheSea()
    {
        // A closed way running counter-clockwise keeps its land, the inside, on the left.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((40, 40), (60, 40), (60, 60), (40, 60), (40, 40))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(10000 - 400, area.Area, precision: 6);
        Assert.Equal(1, area.NumInteriorRings);
    }

    [Fact]
    public void FollowsAPeninsulaOutAndBack()
    {
        // Land to the west, with a 30 by 20 peninsula reaching east.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 40), (80, 40), (80, 60), (50, 60), (50, 110))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000 - 600, area.Area, precision: 6);
        Assert.False(area.Contains(At(65, 50)));
    }

    [Fact]
    public void FindsASeaOnEachSideOfALandBridge()
    {
        // Land between x = 30 and x = 70, sea to both sides.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((30, 110), (30, -10)), Way((70, -10), (70, 110))], Frame);

        Assert.Equal(2, sea.Count);
        Assert.Equal(3000 + 3000, sea.Sum(area => area.Area), precision: 6);
    }

    [Fact]
    public void TreatsAFaceNoProbeReachesAsLand()
    {
        // A second coast cuts the north-east corner, heading south-east with its land on the
        // corner side. Its one segment is so long that its midpoint lies outside the frame, so
        // it drops no probes and the corner gets no votes either way.
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 110)), Way((60, 130), (260, -70))], Frame);

        Polygon area = Assert.Single(sea);
        Assert.Equal(5000 - 50, area.Area, precision: 6);
        Assert.False(area.Contains(At(98, 98)));
    }

    [Fact]
    public void IgnoresAWayTooShortToBeALine()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way(), Way((20, 20)), Way((50, -10), (50, 110))], Frame);

        Assert.Equal(5000, Assert.Single(sea).Area, precision: 6);
    }

    [Fact]
    public void ToleratesAPointRepeatedAlongAWay()
    {
        IReadOnlyList<Polygon> sea = CoastlineCloser.CloseSea(
            [Way((50, -10), (50, 40), (50, 40), (50, 110))], Frame);

        Assert.Equal(5000, Assert.Single(sea).Area, precision: 6);
    }

    [Fact]
    public void FindsNoSeaWithoutACoastline()
    {
        Assert.Empty(CoastlineCloser.CloseSea([], Frame));
    }

    [Fact]
    public void FindsNoSeaWhenTheCoastlineMissesTheFrame()
    {
        Assert.Empty(CoastlineCloser.CloseSea([Way((500, -10), (500, 110))], Frame));
    }
}
```

- [x] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Osm.Tests --filter "FullyQualifiedName~CoastlineCloserTests"
```

Expected: build error CS0103, `The name 'CoastlineCloser' does not exist in the current context`.

- [x] **Step 3: Write the closer**

`src/MetroDisplay.Osm/CoastlineCloser.cs`:

```csharp
using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Polygonize;

namespace MetroDisplay.Osm;

/// <summary>
/// Turns OpenStreetMap coastline, which is a set of open lines, into filled sea.
/// </summary>
public static class CoastlineCloser
{
    /// <summary>
    /// How far either side of a coastline segment the sea and land probes sit, in plane units.
    /// Small enough to stay inside the face the segment borders.
    /// </summary>
    private const double SideProbeOffset = 0.5;

    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// The sea inside a frame.
    /// </summary>
    /// <param name="coastlineWays">Coastline ways in OSM's direction: land on the left, sea on the right.</param>
    /// <param name="frame">The rectangle to close the coastline against.</param>
    /// <returns>Sea polygons inside the frame, with islands as holes. Empty when no coastline crosses the frame.</returns>
    public static IReadOnlyList<Polygon> CloseSea(IReadOnlyList<IReadOnlyList<PlanePoint>> coastlineWays, ExtentRectangle frame)
    {
        var frameEnvelope = new Envelope(frame.MinX, frame.MaxX, frame.MinY, frame.MaxY);
        Geometry frameArea = Factory.ToGeometry(frameEnvelope);

        var linework = new List<Geometry> { frameArea.Boundary };
        var seaProbes = new List<Coordinate>();
        var landProbes = new List<Coordinate>();
        foreach (IReadOnlyList<PlanePoint> way in coastlineWays)
        {
            // A line needs two points. Anything shorter borders nothing.
            if (way.Count < 2)
            {
                continue;
            }

            Coordinate[] line = way.Select(point => new Coordinate(point.X, point.Y)).ToArray();
            linework.Add(Factory.CreateLineString(line).Intersection(frameArea));
            AddSideProbes(line, frameEnvelope, seaProbes, landProbes);
        }

        if (seaProbes.Count == 0)
        {
            return [];
        }

        // Overlaying the coastline on the frame's edge splits the frame into faces. The union
        // also joins ways that meet end to end, in whatever order they arrived.
        var polygonizer = new Polygonizer();
        polygonizer.Add(Factory.BuildGeometry(linework).Union());
        List<Polygon> faces = polygonizer.GetPolygons().Cast<Polygon>().ToList();

        int[] seaVotes = CountProbesPerFace(faces, seaProbes);
        int[] landVotes = CountProbesPerFace(faces, landProbes);
        // A face no probe reaches stays land: leaving water out is the quieter mistake.
        return faces.Where((_, faceNumber) => seaVotes[faceNumber] > landVotes[faceNumber]).ToList();
    }

    /// <summary>
    /// For every segment of a way inside the frame, one probe just to its right (sea) and one
    /// just to its left (land).
    /// </summary>
    private static void AddSideProbes(Coordinate[] line, Envelope frameEnvelope, List<Coordinate> seaProbes, List<Coordinate> landProbes)
    {
        for (int index = 1; index < line.Length; index++)
        {
            Coordinate start = line[index - 1];
            Coordinate end = line[index];
            double length = start.Distance(end);
            var middle = new Coordinate((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            if (length == 0 || !frameEnvelope.Contains(middle))
            {
                continue;
            }

            // The unit vector pointing right of the direction of travel.
            double rightX = (end.Y - start.Y) / length;
            double rightY = -(end.X - start.X) / length;
            seaProbes.Add(new Coordinate(middle.X + rightX * SideProbeOffset, middle.Y + rightY * SideProbeOffset));
            landProbes.Add(new Coordinate(middle.X - rightX * SideProbeOffset, middle.Y - rightY * SideProbeOffset));
        }
    }

    private static int[] CountProbesPerFace(List<Polygon> faces, List<Coordinate> probes)
    {
        // A spatial index, so each probe is tested against the few faces near it, not all of them.
        var faceIndex = new STRtree<int>();
        var preparedFaces = new List<IPreparedGeometry>();
        for (int faceNumber = 0; faceNumber < faces.Count; faceNumber++)
        {
            faceIndex.Insert(faces[faceNumber].EnvelopeInternal, faceNumber);
            preparedFaces.Add(PreparedGeometryFactory.Prepare(faces[faceNumber]));
        }

        int[] votes = new int[faces.Count];
        foreach (Coordinate probe in probes)
        {
            Point point = Factory.CreatePoint(probe);
            foreach (int faceNumber in faceIndex.Query(new Envelope(probe)))
            {
                if (preparedFaces[faceNumber].Contains(point))
                {
                    votes[faceNumber]++;
                    break;
                }
            }
        }
        return votes;
    }
}
```

- [x] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Osm.Tests --filter "FullyQualifiedName~CoastlineCloserTests"
```

Expected: PASS, 11 tests.

- [x] **Step 5: Hand off for commit**

```
feat: close OSM coastline into sea polygons

Coastline ways are overlaid on the frame's edge, which splits the
frame into faces. Each face is sea or land by which side of the
coastline it lies on: OSM keeps land on the left of a way's
direction.
```

---

### Task 5: Lakes, rivers, and the water layer

**Files:**
- Create: `src/MetroDisplay.Osm/OsmWater.cs`
- Create: `src/MetroDisplay.Osm/WaterLayerBuilder.cs`
- Test: `tests/MetroDisplay.Osm.Tests/OsmWaterTests.cs`
- Test: `tests/MetroDisplay.Osm.Tests/WaterLayerBuilderTests.cs`

**Interfaces:**
- Consumes: `OverpassResponse` and friends (Task 3), `CoastlineCloser.CloseSea` (Task 4),
  `GeoBox` (Task 3), `WaterArea` (Task 2), `ExtentRectangle`, `CoordinateNormalizer`,
  `MercatorProjector` (`MetroDisplay.Spatial`).
- Produces:
  `OsmWater(IReadOnlyList<IReadOnlyList<PlanePoint>> CoastlineWays, IReadOnlyList<Geometry> InlandWater)`
  with `static OsmWater From(OverpassResponse response)`;
  `WaterLayerBuilder.Build(OverpassResponse response, ExtentRectangle frame, ExtentRectangle railBounds, double latitudeDegrees) -> IReadOnlyList<WaterArea>`.

Fixtures sit at the equator, where a degree is about 111.2 km both ways and the plane equals
the ground: a 0.002° square is 4.96 ha and a 0.001° square is 1.24 ha.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Osm.Tests/OsmWaterTests.cs`:

```csharp
using NetTopologySuite.Geometries;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class OsmWaterTests
{
    private static Dictionary<string, string> Tags(string key, string value) => new() { [key] = value };

    private static List<OverpassPoint> Ring(double south, double west, double sizeDegrees) =>
    [
        new(south, west), new(south, west + sizeDegrees), new(south + sizeDegrees, west + sizeDegrees),
        new(south + sizeDegrees, west), new(south, west),
    ];

    private static OsmWater From(params OverpassElement[] elements) => OsmWater.From(new OverpassResponse(elements));

    [Fact]
    public void KeepsCoastlineApartFromInlandWater()
    {
        OsmWater water = From(
            new OverpassElement("way", Tags("natural", "coastline"), [new(-1, 0.05), new(1, 0.05)]),
            new OverpassElement("way", Tags("natural", "water"), Ring(0, 0, 0.002)));

        Assert.Single(water.CoastlineWays);
        Assert.Single(water.InlandWater);
    }

    [Fact]
    public void BuildsAPolygonFromAClosedWay()
    {
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), Ring(0, 0, 0.002)));

        // 0.002 degrees is 222.64 plane metres at the equator: 49,568 square metres.
        Assert.Equal(49568, Assert.Single(water.InlandWater).Area, tolerance: 1);
    }

    [Fact]
    public void JoinsARelationsSplitOutlineAndCutsItsIsland()
    {
        var relation = new OverpassElement("relation", Tags("natural", "water"), Points: null, Members:
        [
            new OverpassMember("way", "outer", [new(0.01, 0.01), new(0.01, 0.02), new(0.02, 0.02)]),
            new OverpassMember("way", "outer", [new(0.02, 0.02), new(0.02, 0.01), new(0.01, 0.01)]),
            new OverpassMember("way", "inner", Ring(0.014, 0.014, 0.002)),
            new OverpassMember("node", "label"),
        ]);

        Geometry area = Assert.Single(From(relation).InlandWater);

        Polygon polygon = Assert.IsType<Polygon>(area);
        Assert.Equal(1, polygon.NumInteriorRings);
    }

    [Fact]
    public void ReadsRiverbanksAsWater()
    {
        OsmWater water = From(new OverpassElement("way", Tags("waterway", "riverbank"), Ring(0, 0, 0.002)));

        Assert.Single(water.InlandWater);
    }

    [Fact]
    public void SkipsAWaterWayThatDoesNotClose()
    {
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), [new(0, 0), new(0, 0.002), new(0.002, 0.002), new(0.002, 0)]));

        Assert.Empty(water.InlandWater);
    }

    [Fact]
    public void SkipsAWaterWayTooShortToEncloseAnything()
    {
        // Out and back along one line: closed, but with no inside.
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"), [new(0, 0), new(0, 0.002), new(0, 0)]));

        Assert.Empty(water.InlandWater);
    }

    [Fact]
    public void SkipsARelationWithNoOutline()
    {
        var relation = new OverpassElement("relation", Tags("natural", "water"), Points: null, Members:
        [
            new OverpassMember("way", "outer", [new(0.01, 0.01)]),
            new OverpassMember("node", "label"),
        ]);

        Assert.Empty(From(relation).InlandWater);
    }

    [Fact]
    public void RepairsAnOutlineThatCrossesItself()
    {
        // A bow tie: the outline crosses itself in the middle. Left as it is, clipping it fails.
        OsmWater water = From(new OverpassElement("way", Tags("natural", "water"),
            [new(0, 0), new(0.002, 0.002), new(0.002, 0), new(0, 0.002), new(0, 0)]));

        Geometry area = Assert.Single(water.InlandWater);
        Assert.True(area.IsValid);
        Assert.False(area.IsEmpty);
    }

    [Fact]
    public void IgnoresElementsThatAreNotWater()
    {
        OsmWater water = From(
            new OverpassElement("way", Tags("natural", "wood"), Ring(0, 0, 0.002)),
            new OverpassElement("way", Tags: null, Ring(0, 0, 0.002)));

        Assert.Empty(water.InlandWater);
        Assert.Empty(water.CoastlineWays);
    }
}
```

`tests/MetroDisplay.Osm.Tests/WaterLayerBuilderTests.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Osm.Tests;

public class WaterLayerBuilderTests
{
    /// <summary>A frame about 2.2 km across, centred on the equator.</summary>
    private static readonly ExtentRectangle Frame = new GeoBox(South: -0.01, West: -0.01, North: 0.01, East: 0.01).ToPlane();

    private static Dictionary<string, string> Water() => new() { ["natural"] = "water" };

    private static List<OverpassPoint> Ring(double south, double west, double sizeDegrees) =>
    [
        new(south, west), new(south, west + sizeDegrees), new(south + sizeDegrees, west + sizeDegrees),
        new(south + sizeDegrees, west), new(south, west),
    ];

    private static OverpassElement Lake(double south, double west, double sizeDegrees) => new("way", Water(), Ring(south, west, sizeDegrees));

    /// <summary>A 0.004 degree square lake whose south shore bulges outward at its middle.</summary>
    private static OverpassElement BumpedLake(double bumpDegrees) => new("way", Water(),
    [
        new(0, 0), new(-bumpDegrees, 0.002), new(0, 0.004), new(0.004, 0.004), new(0.004, 0), new(0, 0),
    ]);

    /// <summary>Builds with the rail frame equal to the clip frame unless a test says otherwise.</summary>
    private static IReadOnlyList<WaterArea> Build(ExtentRectangle railBounds, params OverpassElement[] elements) =>
        WaterLayerBuilder.Build(new OverpassResponse(elements), Frame, railBounds, latitudeDegrees: 0);

    private static List<double> XValues(WaterArea area) =>
        area.Rings.SelectMany(ring => ring.Where((_, index) => index % 2 == 0)).ToList();

    [Fact]
    public void KeepsWaterOfAtLeastTwoHectares()
    {
        // 4.96 ha stays; 1.24 ha is too small to read as water on screen.
        IReadOnlyList<WaterArea> water = Build(Frame, Lake(0, 0, 0.002), Lake(0.005, 0.005, 0.001));

        Assert.Single(water);
    }

    [Fact]
    public void ClipsWaterToTheFrame()
    {
        // The lake runs from 0.005 to 0.015 degrees east; the frame ends at 0.01.
        WaterArea area = Assert.Single(Build(Frame, Lake(0, 0.005, 0.01)));

        Assert.Equal(1.0, XValues(area).Max());
        Assert.Equal(0.75, XValues(area).Min());
    }

    [Fact]
    public void NormalizesInTheRailFrameEvenBeyondIt()
    {
        // The rail covers only the north-east quarter; the lake lies west of it.
        ExtentRectangle railBounds = new GeoBox(South: 0, West: 0, North: 0.01, East: 0.01).ToPlane();

        WaterArea area = Assert.Single(Build(railBounds, Lake(0, -0.008, 0.004)));

        Assert.Equal(-0.8, XValues(area).Min(), precision: 3);
        Assert.Equal(-0.4, XValues(area).Max(), precision: 3);
    }

    [Fact]
    public void KeepsAnIslandAsASecondRing()
    {
        var lakeWithIsland = new OverpassElement("relation", Water(), Points: null, Members:
        [
            new OverpassMember("way", "outer", Ring(-0.005, -0.005, 0.01)),
            new OverpassMember("way", "inner", Ring(-0.002, -0.002, 0.004)),
        ]);

        WaterArea area = Assert.Single(Build(Frame, lakeWithIsland));

        Assert.Equal(2, area.Rings.Count);
    }

    [Fact]
    public void LeavesRingsImplicitlyClosed()
    {
        IReadOnlyList<double> ring = Assert.Single(Build(Frame, Lake(0, 0, 0.002))).Rings[0];

        // Four corners, without the first repeated at the end.
        Assert.Equal(8, ring.Count);
        Assert.False(ring[0] == ring[^2] && ring[1] == ring[^1]);
    }

    [Fact]
    public void DropsABumpSmallerThanFifteenMetres()
    {
        // 0.00009 degrees is 10 m at the equator.
        WaterArea area = Assert.Single(Build(Frame, BumpedLake(bumpDegrees: 0.00009)));

        Assert.Equal(8, area.Rings[0].Count);
    }

    [Fact]
    public void KeepsABumpLargerThanFifteenMetres()
    {
        // 0.00036 degrees is 40 m at the equator.
        WaterArea area = Assert.Single(Build(Frame, BumpedLake(bumpDegrees: 0.00036)));

        Assert.Equal(10, area.Rings[0].Count);
    }

    [Fact]
    public void MeasuresTheSimplifyToleranceOnTheGround()
    {
        // At 60 degrees north the plane is stretched to twice the ground. A 20 m bump on the
        // plane is 10 m on the ground, under the tolerance, though it would stay at the equator.
        var response = new OverpassResponse([BumpedLake(bumpDegrees: 0.00018)]);

        Assert.Equal(10, Assert.Single(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 0)).Rings[0].Count);
        Assert.Equal(8, Assert.Single(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 60)).Rings[0].Count);
    }

    [Fact]
    public void MeasuresTheTwoHectaresOnTheGround()
    {
        // 4.96 ha on the plane is 1.24 ha on the ground at 60 degrees north, where areas are
        // stretched fourfold.
        var response = new OverpassResponse([Lake(0, 0, 0.002)]);

        Assert.Empty(WaterLayerBuilder.Build(response, Frame, Frame, latitudeDegrees: 60));
    }

    [Fact]
    public void FillsTheSeaBesideACoastline()
    {
        // A coast running north through the middle: land west, sea east.
        var coast = new OverpassElement("way", new Dictionary<string, string> { ["natural"] = "coastline" }, [new(-0.02, 0), new(0.02, 0)]);

        WaterArea sea = Assert.Single(Build(Frame, coast));

        Assert.Equal(0.5, XValues(sea).Min());
        Assert.Equal(1.0, XValues(sea).Max());
    }
}
```

- [x] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Osm.Tests --filter "FullyQualifiedName~OsmWaterTests|FullyQualifiedName~WaterLayerBuilderTests"
```

Expected: build error CS0246, `The type or namespace name 'OsmWater' could not be found`.

- [x] **Step 3: Write the assembler**

`src/MetroDisplay.Osm/OsmWater.cs`:

```csharp
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

    public static OsmWater From(OverpassResponse response)
    {
        var coastlineWays = new List<IReadOnlyList<PlanePoint>>();
        var inlandWater = new List<Geometry>();
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

    private static string Tag(OverpassElement element, string key)
        => element.Tags is not null && element.Tags.TryGetValue(key, out string? value) ? value : "";

    private static PlanePoint Project(OverpassPoint point)
        => MercatorProjector.Project(new GeoPoint(point.Latitude, point.Longitude));

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
```

- [x] **Step 4: Write the layer builder**

`src/MetroDisplay.Osm/WaterLayerBuilder.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace MetroDisplay.Osm;

/// <summary>
/// Turns an Overpass response into the water the renderer fills.
/// </summary>
public static class WaterLayerBuilder
{
    /// <summary>
    /// Two hectares. At about 30 m per pixel on a 1080p display that is a 5 pixel blob;
    /// anything smaller reads as speckle.
    /// </summary>
    private const double MinimumAreaM2 = 20_000;

    /// <summary>
    /// About half a pixel on a 1080p display. Given as ground distance and converted below.
    /// </summary>
    private const double SimplifyToleranceM = 15;

    private static readonly GeometryFactory Factory = new();

    /// <param name="response">The Overpass answer for the water query.</param>
    /// <param name="frame">Plane rectangle to clip water to: the square around the city core.</param>
    /// <param name="railBounds">Plane rectangle the rail is normalized in. Water uses it too, so the layers line up; where <paramref name="frame"/> is larger, water lands outside [0, 1].</param>
    /// <param name="latitudeDegrees">Latitude used to convert ground sizes into plane units.</param>
    public static IReadOnlyList<WaterArea> Build(OverpassResponse response, ExtentRectangle frame, ExtentRectangle railBounds, double latitudeDegrees)
    {
        OsmWater water = OsmWater.From(response);
        Geometry frameArea = Factory.ToGeometry(new Envelope(frame.MinX, frame.MaxX, frame.MinY, frame.MaxY));

        // Mercator stretches lengths by 1 / cos(latitude), and so areas by its square.
        double groundPerPlane = Math.Cos(latitudeDegrees * Math.PI / 180.0);
        double minimumPlaneArea = MinimumAreaM2 / (groundPerPlane * groundPerPlane);
        double toleranceInPlaneUnits = SimplifyToleranceM / groundPerPlane;

        var areas = new List<WaterArea>();
        foreach (Geometry source in CoastlineCloser.CloseSea(water.CoastlineWays, frame).Concat(water.InlandWater))
        {
            Geometry clipped = source.Intersection(frameArea);
            for (int partNumber = 0; partNumber < clipped.NumGeometries; partNumber++)
            {
                if (clipped.GetGeometryN(partNumber) is not Polygon polygon || polygon.Area < minimumPlaneArea)
                {
                    continue;
                }

                // Topology-preserving, so a ring can neither cross itself nor an island leave its lake.
                if (TopologyPreservingSimplifier.Simplify(polygon, toleranceInPlaneUnits) is not Polygon { IsEmpty: false } simplified)
                {
                    continue;
                }

                var rings = new List<IReadOnlyList<double>> { Flatten(simplified.ExteriorRing, railBounds) };
                rings.AddRange(simplified.InteriorRings.Select(ring => Flatten(ring, railBounds)));
                areas.Add(new WaterArea(rings));
            }
        }
        return areas;
    }

    /// <summary>
    /// A ring as the wire carries it. NetTopologySuite repeats the first point at the end;
    /// the contract leaves rings implicitly closed, so that last point is dropped.
    /// </summary>
    private static IReadOnlyList<double> Flatten(LineString ring, ExtentRectangle railBounds)
        => CoordinateNormalizer.Flatten(ring.Coordinates.SkipLast(1).Select(coordinate => new PlanePoint(coordinate.X, coordinate.Y)), railBounds);
}
```

- [x] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Osm.Tests
```

Expected: PASS, 41 tests (11 from Task 3, 11 from Task 4, 9 and 10 here).

- [x] **Step 6: Run the whole suite**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 107 tests (20 Spatial, 37 Gtfs.Static, 41 Osm, 9 Server).

- [x] **Step 7: Hand off for commit**

```
feat: build the water layer from OSM data

Lakes and rivers come from closed ways and multipolygon relations,
the sea from the closed coastline. Areas are clipped to the core
square, dropped under 2 ha, simplified at 15 m, and normalized in
the rail's frame so both layers line up.
```

---

### Task 6: Fetch once, and serve water with the scene

**Files:**
- Create: `src/MetroDisplay.Server/Feeds/OsmLayerCache.cs`
- Create: `src/MetroDisplay.Server/WaterLayerLoader.cs`
- Modify: `src/MetroDisplay.Server/ServerSettings.cs`, `Program.cs`, `appsettings.json`,
  `MetroDisplay.Server.csproj`
- Test: `tests/MetroDisplay.Server.Tests/OsmLayerCacheTests.cs`
- Test: `tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`

**Interfaces:**
- Consumes: `OverpassReader`, `OverpassQuery`, `GeoBox`, `WaterLayerBuilder` (Tasks 3–5);
  `NetworkSceneBuilder.BuildLayer`, `RailLayer`, `WaterArea` (Task 2); `StubHttpHandler`,
  `TemporaryDirectory` (existing test helpers).
- Produces:
  `OsmLayerCache(HttpClient httpClient, string cacheDirectory, string overpassUrl)` with
  `CachePathFor(string cityId, string layer) -> string` and
  `GetAsync(string cityId, string layer, string query, CancellationToken cancellationToken = default) -> Task<string>`;
  `WaterLayerLoader.LoadAsync(OsmLayerCache cache, CityConfig config, ExtentRectangle railBounds, ILogger logger, CancellationToken cancellationToken = default) -> Task<IReadOnlyList<WaterArea>>`;
  `ServerSettings` gains `OsmCacheDirectory` and `OverpassUrl`;
  `GET /api/network` carries `water`.

The cache takes a layer name so slice 7's place names get their own file
(`<cityId>-places.json`) and never refetch water.

- [ ] **Step 1: Reference the new projects**

```bash
dotnet add src/MetroDisplay.Server reference src/MetroDisplay.Osm/MetroDisplay.Osm.csproj src/MetroDisplay.Spatial/MetroDisplay.Spatial.csproj
```

- [ ] **Step 2: Write the failing cache tests**

`tests/MetroDisplay.Server.Tests/OsmLayerCacheTests.cs`:

```csharp
using System.Net;
using MetroDisplay.Server.Feeds;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class OsmLayerCacheTests
{
    private const string OverpassUrl = "https://overpass.example.test/api/interpreter";
    private const string Query = "[out:json];way[\"natural\"=\"water\"](1,2,3,4);out geom;";
    private const string LayerJson = """{"elements":[{"type":"way","id":1,"tags":{"natural":"water"},"geometry":[{"lat":1,"lon":2}]}]}""";

    private static StubHttpHandler Answering(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public async Task DownloadsAndKeepsTheLayerWhenNothingIsCached()
    {
        using var workspace = new TemporaryDirectory();
        string cacheDirectory = Path.Combine(workspace.FullPath, "osm");
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.OK, LayerJson)), cacheDirectory, OverpassUrl);

        string result = await cache.GetAsync("test", "water", Query);

        Assert.Equal(LayerJson, result);
        Assert.Equal(LayerJson, await File.ReadAllTextAsync(cache.CachePathFor("test", "water")));
        // Nothing but the finished file: no partial download left behind.
        Assert.Equal(new[] { "test-water.json" }, Directory.EnumerateFiles(cacheDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ReusesTheCachedLayerWithoutDownloading()
    {
        using var workspace = new TemporaryDirectory();
        StubHttpHandler handler = Answering(HttpStatusCode.OK, "{}");
        var cache = new OsmLayerCache(new HttpClient(handler), workspace.FullPath, OverpassUrl);
        await File.WriteAllTextAsync(cache.CachePathFor("test", "water"), LayerJson);

        string result = await cache.GetAsync("test", "water", Query);

        Assert.Equal(LayerJson, result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostsTheQueryAsFormData()
    {
        using var workspace = new TemporaryDirectory();
        string? sentBody = null;
        var handler = new StubHttpHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LayerJson) };
        });
        var cache = new OsmLayerCache(new HttpClient(handler), workspace.FullPath, OverpassUrl);

        await cache.GetAsync("test", "water", Query);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(OverpassUrl, request.RequestUri!.ToString());
        // Decoded, because form encoding writes a space as "+" where a URL would write "%20".
        Assert.Equal(Query, System.Web.HttpUtility.ParseQueryString(sentBody!)["data"]);
    }

    [Fact]
    public async Task CachesNothingWhenOverpassFails()
    {
        using var workspace = new TemporaryDirectory();
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.TooManyRequests, "slow down")), workspace.FullPath, OverpassUrl);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("test", "water", Query));

        Assert.Contains("429", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }

    [Fact]
    public async Task CachesNothingWhenOverpassReportsARemark()
    {
        using var workspace = new TemporaryDirectory();
        const string timedOut = """{"elements":[],"remark":"runtime error: Query timed out"}""";
        var cache = new OsmLayerCache(new HttpClient(Answering(HttpStatusCode.OK, timedOut)), workspace.FullPath, OverpassUrl);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("test", "water", Query));

        Assert.Contains("timed out", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~OsmLayerCacheTests"
```

Expected: build error CS0246, `The type or namespace name 'OsmLayerCache' could not be found`.

- [ ] **Step 4: Write the cache**

`src/MetroDisplay.Server/Feeds/OsmLayerCache.cs`:

```csharp
using MetroDisplay.Osm;

namespace MetroDisplay.Server.Feeds;

/// <summary>
/// Fetches an OpenStreetMap layer from Overpass once per city and keeps it for good.
/// Coastlines and lakes change on a scale of years, so unlike the GTFS feed a layer is never
/// refreshed: delete its file to fetch it again.
/// </summary>
/// <param name="httpClient">Client used for the query. Overpass asks callers to identify themselves with a User-Agent.</param>
/// <param name="cacheDirectory">Where layers are kept, one file per city and layer. Created on first download.</param>
/// <param name="overpassUrl">The Overpass API interpreter endpoint.</param>
public sealed class OsmLayerCache(HttpClient httpClient, string cacheDirectory, string overpassUrl)
{
    /// <summary>
    /// Where a layer is kept, so a warning about it can name the file to delete.
    /// </summary>
    public string CachePathFor(string cityId, string layer) => Path.Combine(cacheDirectory, $"{cityId}-{layer}.json");

    /// <returns>The raw Overpass response. Raw, so later changes to how a layer is processed need no refetch.</returns>
    /// <exception cref="HttpRequestException">Overpass could not be reached or answered with an error status. Nothing is cached.</exception>
    /// <exception cref="InvalidDataException">Overpass answered with something unusable, such as a timed-out query. Nothing is cached.</exception>
    public async Task<string> GetAsync(string cityId, string layer, string query, CancellationToken cancellationToken = default)
    {
        string cachePath = CachePathFor(cityId, layer);
        if (File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath, cancellationToken);
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
        using HttpResponseMessage response = await httpClient.PostAsync(overpassUrl, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Overpass returned HTTP {(int)response.StatusCode} for the '{layer}' layer of '{cityId}'.",
                inner: null,
                response.StatusCode);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Checked before it is kept: a cached answer is trusted on every later start.
        OverpassReader.Read(body);

        // Written beside the target and moved into place, so an interrupted write never
        // leaves a partial file under the real name.
        Directory.CreateDirectory(cacheDirectory);
        string downloadPath = cachePath + ".download";
        await File.WriteAllTextAsync(downloadPath, body, cancellationToken);
        File.Move(downloadPath, cachePath, overwrite: true);
        return body;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~OsmLayerCacheTests"
```

Expected: PASS, 5 tests.

- [ ] **Step 6: Write the failing endpoint tests**

In `tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`:

Add two constants beside `TestCityJson`:

```csharp
    /// <summary>A pond of about 91 ha inside the fixture network's area.</summary>
    private const string PondLayerJson = """
        {"elements":[{"type":"way","id":1,"tags":{"natural":"water"},"geometry":[
          {"lat":42.33,"lon":-71.10},{"lat":42.33,"lon":-71.09},{"lat":42.34,"lon":-71.09},
          {"lat":42.34,"lon":-71.10},{"lat":42.33,"lon":-71.10}]}]}
        """;

    private const string EmptyLayerJson = """{"elements":[]}""";
```

Give `CreateFactory` a water layer and the two new settings. Replace its signature, and add
the lines marked below:

```csharp
    private WebApplicationFactory<Program> CreateFactory(string cityId, string cityJson = TestCityJson, string? waterLayerJson = EmptyLayerJson)
    {
        string citiesDirectory = Path.Combine(workspace.FullPath, "cities");
        string cacheDirectory = Path.Combine(workspace.FullPath, "cache");
        string osmDirectory = Path.Combine(workspace.FullPath, "osm");                       // new
        Directory.CreateDirectory(citiesDirectory);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(osmDirectory);                                             // new
        File.WriteAllText(Path.Combine(citiesDirectory, "test.json"), cityJson);
        File.WriteAllBytes(Path.Combine(cacheDirectory, "test.zip"), GtfsFixtureBuilder.TwoRailLinesAndABus().Build());
        // A cached layer keeps startup off the network. Null leaves the cache empty.
        if (waterLayerJson is not null)                                                      // new
        {
            File.WriteAllText(Path.Combine(osmDirectory, "test-water.json"), waterLayerJson);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("MetroDisplay:CityId", cityId)
            .UseSetting("MetroDisplay:CitiesDirectory", citiesDirectory)
            .UseSetting("MetroDisplay:FeedCacheDirectory", cacheDirectory)
            .UseSetting("MetroDisplay:OsmCacheDirectory", osmDirectory)                      // new
            // Nothing listens here, so a fetch fails at once instead of reaching the internet.
            .UseSetting("MetroDisplay:OverpassUrl", "http://127.0.0.1:9/api/interpreter"));  // new
    }
```

Add a helper and three tests:

```csharp
    private static async Task<JsonElement> GetNetworkAsync(WebApplicationFactory<Program> factory)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/network");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task ServesWaterBesideTheLines()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: PondLayerJson);

        JsonElement network = await GetNetworkAsync(factory);

        JsonElement pond = Assert.Single(network.GetProperty("water").EnumerateArray());
        Assert.Equal(8, Assert.Single(pond.GetProperty("rings").EnumerateArray()).GetArrayLength());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task StartsWithoutWaterWhenOverpassIsUnreachable()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: null);

        JsonElement network = await GetNetworkAsync(factory);

        Assert.Equal(0, network.GetProperty("water").GetArrayLength());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
        // Nothing was cached, so the next start tries again.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(workspace.FullPath, "osm")));
    }

    [Fact]
    public async Task StartsWithoutWaterWhenTheCachedLayerIsUnreadable()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test", waterLayerJson: "<html>Sign in to continue</html>");

        JsonElement network = await GetNetworkAsync(factory);

        Assert.Equal(0, network.GetProperty("water").GetArrayLength());
        Assert.Equal(2, network.GetProperty("lines").GetArrayLength());
    }
```

- [ ] **Step 7: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~NetworkEndpointTests"
```

Expected: `ServesWaterBesideTheLines` FAILS with `Assert.Single() Failure: The collection was empty`
(the scene's `water` is always `[]`). The other two new tests pass already for the same
reason; they are there to stay green once water is wired in.

- [ ] **Step 8: Wire water into startup**

`src/MetroDisplay.Server/ServerSettings.cs`: add two `<param>` lines and two parameters.

```csharp
/// <param name="OsmCacheDirectory">Where OpenStreetMap layers are kept once fetched. A relative path resolves against the content root.</param>
/// <param name="OverpassUrl">The Overpass API endpoint water is fetched from.</param>
public sealed record ServerSettings(string CityId, string CitiesDirectory, string FeedCacheDirectory, string OsmCacheDirectory, string OverpassUrl);
```

`src/MetroDisplay.Server/appsettings.json`: add to the `MetroDisplay` section.

```json
    "FeedCacheDirectory": "../../.cache/gtfs",
    "OsmCacheDirectory": "../../.cache/osm",
    "OverpassUrl": "https://overpass-api.de/api/interpreter"
```

`src/MetroDisplay.Server/WaterLayerLoader.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Osm;
using MetroDisplay.Server.Feeds;
using MetroDisplay.Spatial;

namespace MetroDisplay.Server;

/// <summary>
/// Loads a city's water. Water is decoration from a volunteer-run service, so unlike the rail
/// it never stops startup: whatever goes wrong, the map comes up without it.
/// </summary>
public static class WaterLayerLoader
{
    public const string LayerName = "water";

    /// <param name="cache">Where the raw layer is fetched from and kept.</param>
    /// <param name="config">The city, for its core point and radius.</param>
    /// <param name="railBounds">The rail's frame, so water lines up with it.</param>
    /// <param name="logger">Receives the warning when water is left out.</param>
    /// <returns>The water areas, or an empty list when the layer could not be fetched or read.</returns>
    public static async Task<IReadOnlyList<WaterArea>> LoadAsync(OsmLayerCache cache, CityConfig config, ExtentRectangle railBounds, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            GeoBox box = GeoBox.Around(new GeoPoint(config.Extent.Core[0], config.Extent.Core[1]), config.Extent.CoreRadiusKm);
            string json = await cache.GetAsync(config.Id, LayerName, OverpassQuery.Water(box), cancellationToken);
            return WaterLayerBuilder.Build(OverpassReader.Read(json), box.ToPlane(), railBounds, box.CentreLatitude);
        }
        catch (Exception exception)
        {
            // Deliberately broad: no failure in an optional layer may take the rail down with it.
            logger.LogWarning(
                exception,
                "Starting without water: {Reason} If {CachePath} exists, delete it to fetch the layer again.",
                exception.Message,
                cache.CachePathFor(config.Id, LayerName));
            return [];
        }
    }
}
```

`src/MetroDisplay.Server/Program.cs`:

Register a named client for Overpass after `builder.Services.AddHttpClient();`:

```csharp
builder.Services.AddHttpClient("overpass", client =>
{
    // The first fetch for a city can take most of a minute; Overpass asks callers to say who they are.
    client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MetroDisplay/0.1");
});
```

Replace the block from `NetworkScene scene;` through the `app.Logger.LogInformation(...)` line with:

```csharp
RailLayer rail;
try
{
    rail = NetworkSceneBuilder.BuildLayer(zipBytes, config);
}
catch (InvalidDataException exception)
{
    throw new InvalidDataException(
        $"The static feed cached at {feedCache.CachePathFor(config.Id)} could not be read: {exception.Message} Delete it to download the feed again.",
        exception);
}

// Water is fetched once per city and then kept. It never stops startup.
var osmCache = new OsmLayerCache(
    app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("overpass"),
    Path.GetFullPath(Path.Combine(contentRoot, settings.OsmCacheDirectory)),
    settings.OverpassUrl);
IReadOnlyList<WaterArea> water = await WaterLayerLoader.LoadAsync(osmCache, config, rail.Bounds, app.Logger);
NetworkScene scene = rail.Scene with { Water = water };

app.Logger.LogInformation(
    "Built {ArtifactVersion}: {LineCount} lines, {ShapeCount} shapes, {WaterCount} water areas",
    scene.ArtifactVersion,
    scene.Lines.Count,
    scene.Lines.Sum(line => line.Shapes.Count),
    scene.Water.Count);
```

Keep the existing comment above the `try` about the cached feed.

- [ ] **Step 9: Run the Server tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Server.Tests
```

Expected: PASS, 17 tests.

- [ ] **Step 10: Run the whole suite**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 115 tests (20 Spatial, 37 Gtfs.Static, 41 Osm, 17 Server).

- [ ] **Step 11: Run it against the real Overpass**

```bash
dotnet run --project src/MetroDisplay.Server
```

Expected: the first run takes 15 to 60 seconds, writes about 13.6 MB to
`.cache/osm/mbta-water.json`, and logs
`Built mbta@<8 hex>: 8 lines, 65 shapes, 254 water areas`. The water count was measured on
2026-10-06 and will drift as OSM is edited. From a second terminal:

```bash
curl -s http://localhost:5180/api/network | wc -c
```

Expected: about 425,000 bytes (about 246 KB of rail plus about 179 KB of water). Stop the
Server and run it again: it starts in a few seconds without fetching, and logs the same line.

If Overpass is busy, the Server still starts and logs `Starting without water: …`. Run it
again later; nothing was cached.

- [ ] **Step 12: Hand off for commit**

```
feat: fetch water once and serve it with the scene

The Overpass response for a city's core square is cached on the
first start and kept. Water never stops startup: if it cannot be
fetched or read, the map comes up without it and the warning
names the file to delete.
```

---

### Task 7: Drawing water

**Files:**
- Modify: `web/src/contract.ts`, `web/src/draw.ts`, `web/src/main.ts`
- Test: `web/src/draw.test.ts`

**Interfaces:**
- Consumes: `GET /api/network` with `water` (Task 6); `toPixel`, `Fit` (`web/src/fit.ts`).
- Produces: `drawWater(context, scene, fit)` in `web/src/draw.ts`; `WaterArea` and
  `NetworkScene.water` in `web/src/contract.ts`.

- [ ] **Step 1: Write the failing tests**

`web/src/draw.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { NetworkScene } from './contract';
import { drawWater } from './draw';

/** A stand-in canvas context that records the calls drawWater makes, in order. */
function recordingContext(): { context: CanvasRenderingContext2D; calls: string[] } {
  const calls: string[] = [];
  const context = {
    fillStyle: '',
    beginPath: () => calls.push('beginPath'),
    moveTo: (pixelX: number, pixelY: number) => calls.push(`moveTo ${pixelX},${pixelY}`),
    lineTo: (pixelX: number, pixelY: number) => calls.push(`lineTo ${pixelX},${pixelY}`),
    closePath: () => calls.push('closePath'),
    fill: (rule?: string) => calls.push(`fill ${rule}`),
  };
  return { context: context as unknown as CanvasRenderingContext2D, calls };
}

function sceneWith(water: NetworkScene['water']): NetworkScene {
  return { artifactVersion: 'test@00000000', extent: { aspect: 1, coreRadiusKm: 22, spanKm: 10 }, lines: [], water };
}

/** One pixel per hundredth of a unit, no offset: 0.5 lands on pixel 50. */
const fit = { scale: 100, originX: 0, originY: 0 };

describe('drawWater', () => {
  it('fills each area on its own, so overlapping areas never cut holes in each other', () => {
    const { context, calls } = recordingContext();
    const square = [0.1, 0.1, 0.2, 0.1, 0.2, 0.2];

    drawWater(context, sceneWith([{ rings: [square] }, { rings: [square] }]), fit);

    expect(calls.filter((call) => call === 'beginPath')).toHaveLength(2);
    expect(calls.filter((call) => call === 'fill evenodd')).toHaveLength(2);
  });

  it('draws an island as a second closed ring in the same fill', () => {
    const { context, calls } = recordingContext();
    const lake = [0, 0, 1, 0, 1, 1];
    // Quarters and halves are exact in binary, so the expected pixels are exact too.
    const island = [0.25, 0.25, 0.75, 0.25, 0.5, 0.75];

    drawWater(context, sceneWith([{ rings: [lake, island] }]), fit);

    expect(calls).toEqual([
      'beginPath',
      'moveTo 0,0', 'lineTo 100,0', 'lineTo 100,100', 'closePath',
      'moveTo 25,25', 'lineTo 75,25', 'lineTo 50,75', 'closePath',
      'fill evenodd',
    ]);
  });
});
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd web && npm test
```

Expected: FAIL. `draw.test.ts` cannot run: `drawWater` is not exported from `./draw`.

- [ ] **Step 3: Extend the contract types**

In `web/src/contract.ts`, add `water` to `NetworkScene` and a new interface:

```ts
export interface NetworkScene {
  artifactVersion: string;
  extent: ExtentInfo;
  lines: LineScene[];
  water: WaterArea[];
}
```

```ts
/** One body of water: its outline first, then one ring per island. Rings are implicitly closed. */
export interface WaterArea {
  rings: number[][];
}
```

- [ ] **Step 4: Draw the water**

In `web/src/draw.ts`, add below `LINE_WIDTH_PX`:

```ts
/** Dim enough that the rail stays the subject. */
const WATER_COLOR = '#0e1a24';

/**
 * Fills every water area. Each area is its own path and its own fill: the even-odd rule then
 * only ever sees one area's outline and islands, and two areas that overlap (a river meeting
 * the harbour) paint the same colour twice instead of cutting a hole where they cross.
 */
export function drawWater(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit): void {
  context.fillStyle = WATER_COLOR;
  for (const area of scene.water) {
    context.beginPath();
    for (const ring of area.rings) {
      if (ring.length < 6) {
        continue;
      }
      const [startX, startY] = toPixel(fit, ring[0], ring[1]);
      context.moveTo(startX, startY);
      for (let index = 2; index < ring.length; index += 2) {
        const [pixelX, pixelY] = toPixel(fit, ring[index], ring[index + 1]);
        context.lineTo(pixelX, pixelY);
      }
      context.closePath();
    }
    context.fill('evenodd');
  }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
cd web && npm test
```

Expected: PASS, 7 tests (5 fit, 2 draw).

- [ ] **Step 6: Draw water under the lines, and credit OpenStreetMap**

In `web/src/main.ts`:

Change the import and the end of `render`:

```ts
import { drawNetwork, drawWater } from './draw';
```

```ts
  const fit = containFit(widthPx, heightPx, scene.extent.aspect, MARGIN_PX);
  drawWater(drawing, scene, fit);
  drawNetwork(drawing, scene, fit);
```

Change the status line so the attribution shows whenever water is drawn, as ODbL requires:

```ts
    const attribution = scene.water.length > 0 ? ' · © OpenStreetMap contributors' : '';
    statusLine.textContent = `${scene.lines.length} lines · ${scene.artifactVersion}${attribution}`;
```

- [ ] **Step 7: Type-check and build**

```bash
cd web && npm run build
```

Expected: `tsc` reports nothing, and Vite writes `web/dist/`.

- [ ] **Step 8: Look at it**

Run the Server and `npm run dev` (two terminals, as in slice 1) and open
`http://localhost:5173`. Expected:
- Boston Harbor and Massachusetts Bay filled to the right edge of the screen, with the
  harbour islands as gaps; the Charles River basin under the Red and Green lines; the Mystic
  to the north and the Neponset to the south; Fresh Pond, Spy Pond, Jamaica Pond and the
  lakes west of the network.
- Water is clearly dimmer than every rail line, the Blue Line included.
- The status line ends with `· © OpenStreetMap contributors`.
- Resize the window: water and rail stay locked together.
- Rename `.cache/osm/mbta-water.json`, set `OverpassUrl` to `http://127.0.0.1:9/` in
  `appsettings.Development.json`, and restart: the rail draws alone and the attribution is
  gone. Undo both afterwards.

- [ ] **Step 9: Hand off for commit**

```
feat: fill water behind the rail lines

Each water area is filled on its own with the even-odd rule, so
islands stay dry and overlapping areas never cut holes in each
other. OpenStreetMap is credited whenever water is drawn.
```

---

## Done when

- `dotnet test MetroDisplay.slnx` passes with 115 tests.
- `npm test` in `web/` passes with 7 tests, and `npm run build` succeeds.
- With the Server and `npm run dev` running, `http://localhost:5173` shows Boston's rail over
  its filled harbour, rivers and lakes, with the OpenStreetMap credit.
- A second Server start reuses `.cache/osm/mbta-water.json` and makes no request to Overpass.

## Carried into later slices

- **Water colour** is one constant in `draw.ts`. Tune it by eye; a per-city palette is spec §8's
  seam if it ever needs to vary.
- **Payload** is about 179 KB of water beside 246 KB of rail. If it matters, raise the 2 ha
  floor or the 15 m tolerance: both are single constants, measured against the real feed.
- **Slice 3, generated types,** replaces the hand-written `contract.ts`, which this slice
  extended once more.
- **Slice 4, the extent,** frames the rail on the same core square, after which water
  coordinates return to `[0, 1]`.
- **Slice 7, place names,** adds a second layer through `OsmLayerCache` (`<cityId>-places.json`)
  and a second query in `OverpassQuery`; the reader and box are reused as they are.
- **Rivers drawn as lines.** OSM maps narrow streams as `waterway=river` lines with no area.
  They are not fetched. Wide rivers (the Charles, the Mystic) are areas and are.
- **Slice 1's deferred minors** are still open and untouched by this plan.
