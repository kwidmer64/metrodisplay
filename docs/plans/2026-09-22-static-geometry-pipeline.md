# Static Geometry Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn a city's GTFS zip into a validated, versioned `NetworkArtifact` on disk, invoked by a CLI command.

**Architecture:** A pipeline of small pure transforms over bytes — read archive, filter to rail, deduplicate shapes, project to Web Mercator, clip to a real-distance extent, simplify, normalize. Fetching is injected at the edge so every transform stage is testable without a network. The artifact is written through `INetworkArtifactStore`, whose only implementation here writes JSON to disk.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit, CsvHelper, System.IO.Compression, System.Text.Json.

**Spec:** `docs/specs/2026-09-22-metrodisplay-design.md` — this plan implements §6 (static pipeline), §4 (coordinate system), §5 (extent and clipping), and the artifact half of §3.

## Global Constraints

- Target framework `net10.0`. SDK 10.0.401 confirmed present.
- **Claude never commits.** Every task ends with a handoff step supplying a commit message; the developer reviews and commits. Per `CLAUDE.md`.
- **Naming:** readable words only. No single letters, no two/three-character stubs. `point.x`/`point.y` allowed only as members of a coordinate type. Per `CLAUDE.md`.
- **No attribution trailers** in commit messages. No reference to Claude, AI, or tooling anywhere in code, comments, tests, fixtures, or messages.
- **Unit suffixes required** on every unit-bearing name: `intervalMs`, `dwellMs`, `coreRadiusKm`, `toleranceM`, `lengthM`.
- **Wire field names are load-bearing.** The JSON produced must match §8 of the spec exactly. Task 1 pins this with a test.
- TDD throughout: failing test first, minimal implementation, passing test, handoff.

## File Structure

```
MetroDisplay.slnx

src/MetroDisplay.Contracts/
  NetworkScene.cs          Wire DTOs: NetworkScene, CityMetadata, ExtentInfo,
                           LineScene, ShapeGeometry, StationMarker, EdgeLabel
  NetworkArtifact.cs       NetworkArtifact, ArtifactManifest, TripIndex
  CityConfig.cs            CityConfig and its nested config records
  JsonDefaults.cs          The single JsonSerializerOptions used everywhere

src/MetroDisplay.Gtfs.Static/
  Reading/GtfsArchiveReader.cs      zip bytes -> typed GTFS records
  Reading/GtfsRecords.cs            GtfsRoute, GtfsTrip, GtfsShapePoint, GtfsStop, GtfsFeedInfo
  Reading/CityConfigLoader.cs       JSON + ${ENV_VAR} interpolation
  Geometry/GeoPoint.cs              lat/lon pair
  Geometry/PlanePoint.cs            projected x/y pair
  Geometry/MercatorProjector.cs     lat/lon -> Web Mercator metres
  Geometry/ExtentRectangle.cs       projected bounding rectangle
  Geometry/ExtentCalculator.cs      core + coreRadiusKm ∩ content bounds
  Geometry/PolylineClipper.cs       clip to extent, emit exit points
  Geometry/PolylineSimplifier.cs    Douglas-Peucker
  Geometry/CoordinateNormalizer.cs  extent-relative [0,1], y-flip, quantize
  Pipeline/RailRouteSelector.cs     route_type filter + exclusions
  Pipeline/ShapeDeduplicator.cs     collapse identical geometry
  Pipeline/TripIndexBuilder.cs      trip -> shape, route -> primary shape
  Pipeline/NetworkArtifactBuilder.cs   orchestrates the stages
  Pipeline/ArtifactValidator.cs     rejects empty artifacts
  Storage/INetworkArtifactStore.cs
  Storage/FileNetworkArtifactStore.cs
  Feeds/IStaticFeedClient.cs
  Feeds/HttpStaticFeedClient.cs     conditional GET
  Feeds/StaticFeedResult.cs

src/MetroDisplay.Tools/
  Program.cs                CLI entry: build-artifact <cityId>

tests/MetroDisplay.Gtfs.Static.Tests/
  GtfsFixtureBuilder.cs     builds synthetic GTFS zips in memory
  <one test file per production file under test>

cities/mbta.json
```

**Why a synthetic fixture rather than a trimmed real zip:** the fixture is written as code, so a test reads as "three routes, one of which is a bus" instead of "trust this binary". It is deterministic, diffable, and carries no agency licensing question. Real feeds get exercised in Task 14 against the live URL, by hand.

---

### Task 1: Solution scaffold and wire contracts

**Files:**
- Create: `MetroDisplay.slnx` (the .NET 10 SDK default; `dotnet new sln` produces the XML format)
- Create: `src/MetroDisplay.Contracts/MetroDisplay.Contracts.csproj`
- Create: `src/MetroDisplay.Contracts/NetworkScene.cs`
- Create: `src/MetroDisplay.Contracts/JsonDefaults.cs`
- Create: `tests/MetroDisplay.Gtfs.Static.Tests/MetroDisplay.Gtfs.Static.Tests.csproj`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneSerializationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NetworkScene`, `CityMetadata`, `ExtentInfo`, `LineScene`, `ShapeGeometry`, `StationMarker`, `EdgeLabel`, and `JsonDefaults.Options`. Every later task serializes through `JsonDefaults.Options`.

- [x] **Step 1: Create the solution and projects**

```bash
dotnet new sln -n MetroDisplay
dotnet new classlib -n MetroDisplay.Contracts -o src/MetroDisplay.Contracts -f net10.0
dotnet new classlib -n MetroDisplay.Gtfs.Static -o src/MetroDisplay.Gtfs.Static -f net10.0
dotnet new xunit -n MetroDisplay.Gtfs.Static.Tests -o tests/MetroDisplay.Gtfs.Static.Tests -f net10.0
dotnet sln add src/MetroDisplay.Contracts src/MetroDisplay.Gtfs.Static tests/MetroDisplay.Gtfs.Static.Tests
dotnet add src/MetroDisplay.Gtfs.Static reference src/MetroDisplay.Contracts
dotnet add tests/MetroDisplay.Gtfs.Static.Tests reference src/MetroDisplay.Gtfs.Static
rm src/MetroDisplay.Contracts/Class1.cs src/MetroDisplay.Gtfs.Static/Class1.cs
```

- [x] **Step 2: Write the failing test**

This test is the contract lock. It asserts the exact JSON field names from spec §8 — if a property is ever renamed, this fails before the renderer does.

`tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneSerializationTests.cs`:

```csharp
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
```

- [x] **Step 3: Run the test and confirm it fails**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter NetworkSceneSerializationTests
```

Expected: compile error — `NetworkScene` does not exist.

- [x] **Step 4: Write the contracts**

`src/MetroDisplay.Contracts/NetworkScene.cs`:

```csharp
namespace MetroDisplay.Contracts;

/// <summary>The `network` message: everything a renderer needs to draw one city's map.</summary>
public sealed record NetworkScene(
    string ArtifactVersion,
    CityMetadata City,
    ExtentInfo Extent,
    IReadOnlyList<LineScene> Lines,
    IReadOnlyList<StationMarker> Stations,
    IReadOnlyList<EdgeLabel> EdgeLabels);

public sealed record CityMetadata(string Id, string Name, string Agency, string Timezone);

/// <summary>Geographic window. Aspect is width/height; spanKm is ground distance across.</summary>
public sealed record ExtentInfo(double Aspect, double CoreRadiusKm, double SpanKm);

public sealed record LineScene(
    string Id,
    string Name,
    string Color,
    IReadOnlyList<ShapeGeometry> Shapes);

/// <summary>Points are a flat [x0,y0,x1,y1,...] array in normalized extent space.</summary>
public sealed record ShapeGeometry(string Id, IReadOnlyList<double> Points, double LengthM);

public sealed record StationMarker(double X, double Y, string Name, int Rank);

/// <summary>Drawn where a clipped line leaves the extent. Angle is degrees, screen convention.</summary>
public sealed record EdgeLabel(double X, double Y, string Text, double Angle, string Line);
```

`src/MetroDisplay.Contracts/JsonDefaults.cs`:

```csharp
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
```

- [x] **Step 5: Run the test and confirm it passes**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter NetworkSceneSerializationTests
```

Expected: PASS, 1 test.

- [x] **Step 6: Hand off for commit**

Report to the developer, with this message:

```
feat: add solution scaffold and wire contracts

NetworkScene and friends serialize to the exact field names in
spec section 8. The serialization test pins those names so a
rename fails here rather than silently in the renderer.
```

---

### Task 2: City config loading with environment interpolation

> **As built:** the loader deserializes with strict options, so a missing required setting or an unknown one throws `JsonException` instead of loading as zero or null. `RouteFilter` is the last `CityConfig` parameter, with a null default, so it stays optional. The placeholder pattern is a `[GeneratedRegex]`. The task has 7 tests instead of 2: the plan's pair, split so each covers one behaviour, plus escaping, missing-setting, unknown-setting and optional-filter cases.

**Files:**
- Create: `src/MetroDisplay.Contracts/CityConfig.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Reading/CityConfigLoader.cs`
- Create: `cities/mbta.json`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/CityConfigLoaderTests.cs`

**Interfaces:**
- Consumes: `JsonDefaults.Options` (Task 1).
- Produces: `CityConfig` record; `CityConfigLoader.Load(string json, IReadOnlyDictionary<string, string> environment) -> CityConfig`.

- [x] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class CityConfigLoaderTests
{
    private const string SampleJson = """
    {
      "id": "mbta",
      "name": "BOSTON",
      "agency": "MBTA",
      "timezone": "America/New_York",
      "staticFeed": { "url": "https://example.test/MBTA_GTFS.zip" },
      "realtime": {
        "vehiclePositions": { "url": "https://example.test/vp",
                              "intervalMs": 15000,
                              "headers": { "x-api-key": "${MBTA_API_KEY}" } },
        "alerts": { "url": "https://example.test/alerts", "intervalMs": 60000 }
      },
      "routeTypes": [0, 1, 2],
      "routeFilter": { "exclude": ["CapeFlyer"] },
      "extent": { "core": [42.3555, -71.0605], "coreRadiusKm": 22 },
      "simplify": { "toleranceM": 25 },
      "dwellMs": 300000
    }
    """;

    [Fact]
    public void ResolvesEnvironmentPlaceholdersInHeaders()
    {
        var environment = new Dictionary<string, string> { ["MBTA_API_KEY"] = "secret-value" };

        CityConfig config = CityConfigLoader.Load(SampleJson, environment);

        Assert.Equal("secret-value", config.Realtime.VehiclePositions.Headers["x-api-key"]);
        Assert.Equal(22, config.Extent.CoreRadiusKm);
        Assert.Equal(42.3555, config.Extent.Core[0]);
        Assert.Equal(300000, config.DwellMs);
        Assert.Equal(25, config.Simplify.ToleranceM);
    }

    [Fact]
    public void ThrowsWhenAPlaceholderHasNoEnvironmentValue()
    {
        var environment = new Dictionary<string, string>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CityConfigLoader.Load(SampleJson, environment));

        Assert.Contains("MBTA_API_KEY", exception.Message);
    }
}
```

- [x] **Step 2: Run the test and confirm it fails**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter CityConfigLoaderTests
```

Expected: compile error — `CityConfig` does not exist.

- [x] **Step 3: Write the config records**

`src/MetroDisplay.Contracts/CityConfig.cs`:

```csharp
namespace MetroDisplay.Contracts;

public sealed record CityConfig(
    string Id,
    string Name,
    string Agency,
    string Timezone,
    FeedSource StaticFeed,
    RealtimeSources Realtime,
    IReadOnlyList<int> RouteTypes,
    RouteFilter? RouteFilter,
    ExtentConfig Extent,
    SimplifyConfig Simplify,
    int DwellMs);

public sealed record FeedSource(
    string Url,
    IReadOnlyDictionary<string, string>? Headers = null,
    int IntervalMs = 0);

public sealed record RealtimeSources(FeedSource VehiclePositions, FeedSource Alerts);

public sealed record RouteFilter(
    IReadOnlyList<string>? Include = null,
    IReadOnlyList<string>? Exclude = null);

/// <summary>Core is [latitude, longitude]. CoreRadiusKm is ground distance, not Mercator.</summary>
public sealed record ExtentConfig(IReadOnlyList<double> Core, double CoreRadiusKm);

public sealed record SimplifyConfig(double ToleranceM);
```

- [x] **Step 4: Write the loader**

`src/MetroDisplay.Gtfs.Static/Reading/CityConfigLoader.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads a city config, resolving ${VAR} placeholders from the supplied environment.
/// Interpolation happens on the raw text before deserialization so a placeholder can
/// appear in any string value, not only in a known field.
/// </summary>
public static class CityConfigLoader
{
    private static readonly Regex PlaceholderPattern = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}");

    public static CityConfig Load(string json, IReadOnlyDictionary<string, string> environment)
    {
        string resolved = PlaceholderPattern.Replace(json, match =>
        {
            string variableName = match.Groups[1].Value;
            if (!environment.TryGetValue(variableName, out string? value))
            {
                throw new InvalidOperationException(
                    $"City config references ${{{variableName}}} but no such environment variable is set.");
            }
            return JsonEncodedText.Encode(value).ToString();
        });

        return JsonSerializer.Deserialize<CityConfig>(resolved, JsonDefaults.Options)
            ?? throw new InvalidOperationException("City config deserialized to null.");
    }

    public static CityConfig LoadFromProcessEnvironment(string json)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? string.Empty);
        return Load(json, environment);
    }
}
```

- [x] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter CityConfigLoaderTests
```

Expected: PASS, 2 tests.

- [x] **Step 6: Write the real city config**

`cities/mbta.json` — same shape as the test sample. Leave the realtime URLs as the MBTA's documented endpoints; they are not exercised until Plan 3.

```json
{
  "id": "mbta",
  "name": "BOSTON",
  "agency": "MBTA",
  "timezone": "America/New_York",
  "staticFeed": { "url": "https://cdn.mbta.com/MBTA_GTFS.zip" },
  "realtime": {
    "vehiclePositions": { "url": "https://cdn.mbta.com/realtime/VehiclePositions.pb", "intervalMs": 15000 },
    "alerts": { "url": "https://cdn.mbta.com/realtime/Alerts.pb", "intervalMs": 60000 }
  },
  "routeTypes": [0, 1],
  "routeFilter": { "exclude": [] },
  "extent": { "core": [42.3555, -71.0605], "coreRadiusKm": 22 },
  "simplify": { "toleranceM": 25 },
  "dwellMs": 300000
}
```

Note: `routeTypes` is `[0, 1]` — light rail and subway. Commuter rail (type 2) sprawls far past a 22 km extent and is excluded for now; revisit once the map is on screen in Plan 2.

- [x] **Step 7: Hand off for commit**

```
feat: load city config with environment interpolation

Placeholders resolve before deserialization so credentials can
appear in any string field. A missing variable fails loudly at
load rather than producing a config with a literal ${VAR} in it.
```

---

### Task 3: GTFS archive reader

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Reading/GtfsRecords.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Reading/GtfsArchiveReader.cs`
- Create: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsFixtureBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsArchiveReaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `GtfsArchive` (a record holding `Routes`, `Trips`, `ShapePoints`, `Stops`, `FeedInfo`), `GtfsArchiveReader.Read(byte[] zipBytes) -> GtfsArchive`, and the test helper `GtfsFixtureBuilder` used by Tasks 4–12.

- [ ] **Step 1: Add the CSV dependency**

```bash
dotnet add src/MetroDisplay.Gtfs.Static package CsvHelper
```

GTFS files are RFC 4180 CSV with quoted fields containing commas — stop names like `"Park St, Level 2"` are common. Hand-rolled splitting on `,` breaks on real feeds.

- [ ] **Step 2: Write the fixture builder**

`tests/MetroDisplay.Gtfs.Static.Tests/GtfsFixtureBuilder.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace MetroDisplay.Gtfs.Static.Tests;

/// <summary>
/// Builds a GTFS zip in memory. Tests read as a description of a feed rather than
/// depending on a committed binary, and each test can vary one thing.
/// </summary>
public sealed class GtfsFixtureBuilder
{
    private readonly Dictionary<string, string> filesByName = [];

    public static GtfsFixtureBuilder ThreeLineRailSystem()
    {
        return new GtfsFixtureBuilder()
            .WithFile("routes.txt", """
                route_id,route_short_name,route_long_name,route_type,route_color
                Red,RED,Red Line,1,DA291C
                Green,GRN,Green Line,0,00843D
                Bus7,7,Bus Route 7,3,FFC72C
                """)
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Red,weekday,red-north-1,Alewife,0,shape-red
                Red,weekday,red-north-2,Alewife,0,shape-red
                Red,weekday,red-south-1,Ashmont,1,shape-red-rev
                Green,weekday,green-west-1,Boston College,0,shape-green
                Bus7,weekday,bus-1,Nowhere,0,shape-bus
                """)
            .WithFile("shapes.txt", """
                shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
                shape-red,42.3200,-71.0900,1
                shape-red,42.3400,-71.0800,2
                shape-red,42.3600,-71.0700,3
                shape-red-rev,42.3600,-71.0700,1
                shape-red-rev,42.3400,-71.0800,2
                shape-red-rev,42.3200,-71.0900,3
                shape-green,42.3500,-71.1200,1
                shape-green,42.3520,-71.0900,2
                shape-green,42.3540,-71.0600,3
                shape-bus,42.3000,-71.2000,1
                shape-bus,42.3100,-71.1900,2
                """)
            .WithFile("stops.txt", """
                stop_id,stop_name,stop_lat,stop_lon,location_type
                place-alfcl,"Alewife",42.3600,-71.0700,1
                place-pktrm,"Park St, Level 2",42.3400,-71.0800,1
                place-asmnl,"Ashmont",42.3200,-71.0900,1
                """)
            .WithFile("feed_info.txt", """
                feed_publisher_name,feed_publisher_url,feed_lang,feed_start_date,feed_end_date
                Test Transit,https://example.test,en,20260901,20261201
                """);
    }

    public GtfsFixtureBuilder WithFile(string name, string contents)
    {
        filesByName[name] = contents;
        return this;
    }

    public GtfsFixtureBuilder WithoutFile(string name)
    {
        filesByName.Remove(name);
        return this;
    }

    public byte[] Build()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string contents) in filesByName)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(contents);
            }
        }
        return buffer.ToArray();
    }
}
```

- [ ] **Step 3: Write the failing test**

`tests/MetroDisplay.Gtfs.Static.Tests/GtfsArchiveReaderTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class GtfsArchiveReaderTests
{
    [Fact]
    public void ReadsEveryFileThePipelineNeeds()
    {
        byte[] zipBytes = GtfsFixtureBuilder.ThreeLineRailSystem().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Equal(3, archive.Routes.Count);
        Assert.Equal(5, archive.Trips.Count);
        Assert.Equal(11, archive.ShapePoints.Count);
        Assert.Equal(3, archive.Stops.Count);
        Assert.Equal(new DateOnly(2026, 12, 1), archive.FeedInfo?.FeedEndDate);
    }

    [Fact]
    public void KeepsCommasInsideQuotedStopNames()
    {
        byte[] zipBytes = GtfsFixtureBuilder.ThreeLineRailSystem().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Contains(archive.Stops, stop => stop.StopName == "Park St, Level 2");
    }

    [Fact]
    public void TreatsMissingFeedInfoAsAbsentRatherThanFailing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.ThreeLineRailSystem()
            .WithoutFile("feed_info.txt")
            .Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Null(archive.FeedInfo);
        Assert.Equal(3, archive.Routes.Count);
    }
}
```

`feed_info.txt` is optional in the GTFS spec and plenty of agencies omit it. Treating its absence as a hard failure would reject valid feeds.

- [ ] **Step 4: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter GtfsArchiveReaderTests
```

Expected: compile error — `GtfsArchive` does not exist.

- [ ] **Step 5: Write the records**

`src/MetroDisplay.Gtfs.Static/Reading/GtfsRecords.cs`:

```csharp
using CsvHelper.Configuration.Attributes;

namespace MetroDisplay.Gtfs.Static.Reading;

public sealed record GtfsArchive(
    IReadOnlyList<GtfsRoute> Routes,
    IReadOnlyList<GtfsTrip> Trips,
    IReadOnlyList<GtfsShapePoint> ShapePoints,
    IReadOnlyList<GtfsStop> Stops,
    GtfsFeedInfo? FeedInfo);

public sealed class GtfsRoute
{
    [Name("route_id")] public string RouteId { get; set; } = "";
    [Name("route_short_name")] [Optional] public string RouteShortName { get; set; } = "";
    [Name("route_long_name")] [Optional] public string RouteLongName { get; set; } = "";
    [Name("route_type")] public int RouteType { get; set; }
    [Name("route_color")] [Optional] public string RouteColor { get; set; } = "";
}

public sealed class GtfsTrip
{
    [Name("route_id")] public string RouteId { get; set; } = "";
    [Name("trip_id")] public string TripId { get; set; } = "";
    [Name("trip_headsign")] [Optional] public string TripHeadsign { get; set; } = "";
    [Name("shape_id")] [Optional] public string ShapeId { get; set; } = "";
}

public sealed class GtfsShapePoint
{
    [Name("shape_id")] public string ShapeId { get; set; } = "";
    [Name("shape_pt_lat")] public double Latitude { get; set; }
    [Name("shape_pt_lon")] public double Longitude { get; set; }
    [Name("shape_pt_sequence")] public int Sequence { get; set; }
}

public sealed class GtfsStop
{
    [Name("stop_id")] public string StopId { get; set; } = "";
    [Name("stop_name")] public string StopName { get; set; } = "";
    [Name("stop_lat")] public double Latitude { get; set; }
    [Name("stop_lon")] public double Longitude { get; set; }
    [Name("location_type")] [Optional] public int LocationType { get; set; }
}

public sealed class GtfsFeedInfo
{
    [Name("feed_end_date")] [Optional] public string FeedEndDateRaw { get; set; } = "";

    public DateOnly? FeedEndDate =>
        DateOnly.TryParseExact(FeedEndDateRaw, "yyyyMMdd", out DateOnly parsed) ? parsed : null;
}
```

- [ ] **Step 6: Write the reader**

`src/MetroDisplay.Gtfs.Static/Reading/GtfsArchiveReader.cs`:

```csharp
using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads the five GTFS files this pipeline needs out of a zip. stop_times.txt is
/// deliberately never opened — it is the bulk of a typical archive and nothing here
/// uses it.
/// </summary>
public static class GtfsArchiveReader
{
    private static readonly CsvConfiguration CsvSettings = new(CultureInfo.InvariantCulture)
    {
        HeaderValidated = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    public static GtfsArchive Read(byte[] zipBytes)
    {
        using var buffer = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        return new GtfsArchive(
            Routes: ReadEntries<GtfsRoute>(archive, "routes.txt"),
            Trips: ReadEntries<GtfsTrip>(archive, "trips.txt"),
            ShapePoints: ReadEntries<GtfsShapePoint>(archive, "shapes.txt"),
            Stops: ReadEntries<GtfsStop>(archive, "stops.txt"),
            FeedInfo: ReadEntries<GtfsFeedInfo>(archive, "feed_info.txt").FirstOrDefault());
    }

    private static List<TRecord> ReadEntries<TRecord>(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return [];
        }

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvSettings);
        return csv.GetRecords<TRecord>().ToList();
    }
}
```

- [ ] **Step 7: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter GtfsArchiveReaderTests
```

Expected: PASS, 3 tests.

- [ ] **Step 8: Hand off for commit**

```
feat: read GTFS archives from zip bytes

Reads only the five files the pipeline needs; stop_times.txt is
never opened. Missing feed_info.txt is tolerated since the GTFS
spec makes it optional. Fixtures are built in memory as code.
```

---

### Task 4: Rail route selection

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/RailRouteSelector.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/RailRouteSelectorTests.cs`

**Interfaces:**
- Consumes: `GtfsArchive` (Task 3), `CityConfig` (Task 2).
- Produces: `RailRouteSelector.Select(GtfsArchive archive, IReadOnlyList<int> routeTypes, RouteFilter? filter) -> RailSelection`, where `RailSelection` holds `Routes`, `Trips`, and `ShapeIds`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class RailRouteSelectorTests
{
    private static GtfsArchive SampleArchive() =>
        GtfsArchiveReader.Read(GtfsFixtureBuilder.ThreeLineRailSystem().Build());

    [Fact]
    public void KeepsRailRouteTypesAndDropsBuses()
    {
        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter: null);

        Assert.Equal(["Green", "Red"], selection.Routes.Select(route => route.RouteId).Order());
        Assert.DoesNotContain(selection.Trips, trip => trip.RouteId == "Bus7");
    }

    [Fact]
    public void CollectsOnlyShapeIdsReachableFromKeptTrips()
    {
        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter: null);

        Assert.Equal(
            ["shape-green", "shape-red", "shape-red-rev"],
            selection.ShapeIds.Order());
        Assert.DoesNotContain("shape-bus", selection.ShapeIds);
    }

    [Fact]
    public void AppliesExcludeListAfterRouteTypeFilter()
    {
        var filter = new RouteFilter(Exclude: ["Green"]);

        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter);

        Assert.Equal(["Red"], selection.Routes.Select(route => route.RouteId));
        Assert.DoesNotContain("shape-green", selection.ShapeIds);
    }

    [Fact]
    public void IncludeListWhenPresentWinsOverEverythingElse()
    {
        var filter = new RouteFilter(Include: ["Green"]);

        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter);

        Assert.Equal(["Green"], selection.Routes.Select(route => route.RouteId));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter RailRouteSelectorTests
```

Expected: compile error — `RailRouteSelector` does not exist.

- [ ] **Step 3: Write the selector**

`src/MetroDisplay.Gtfs.Static/Pipeline/RailRouteSelector.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

public sealed record RailSelection(
    IReadOnlyList<GtfsRoute> Routes,
    IReadOnlyList<GtfsTrip> Trips,
    IReadOnlySet<string> ShapeIds);

/// <summary>
/// Narrows a whole-agency archive down to the rail routes this city renders, and to the
/// shapes those routes actually reference. Everything downstream works on this subset.
/// </summary>
public static class RailRouteSelector
{
    public static RailSelection Select(
        GtfsArchive archive,
        IReadOnlyList<int> routeTypes,
        RouteFilter? filter)
    {
        var wantedTypes = routeTypes.ToHashSet();

        IEnumerable<GtfsRoute> candidates = archive.Routes
            .Where(route => wantedTypes.Contains(route.RouteType));

        if (filter?.Include is { Count: > 0 } include)
        {
            var included = include.ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(route => included.Contains(route.RouteId));
        }
        else if (filter?.Exclude is { Count: > 0 } exclude)
        {
            var excluded = exclude.ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(route => !excluded.Contains(route.RouteId));
        }

        List<GtfsRoute> routes = candidates.ToList();
        var routeIds = routes.Select(route => route.RouteId).ToHashSet(StringComparer.Ordinal);

        List<GtfsTrip> trips = archive.Trips
            .Where(trip => routeIds.Contains(trip.RouteId))
            .ToList();

        HashSet<string> shapeIds = trips
            .Select(trip => trip.ShapeId)
            .Where(shapeId => !string.IsNullOrEmpty(shapeId))
            .ToHashSet(StringComparer.Ordinal);

        return new RailSelection(routes, trips, shapeIds);
    }
}
```

An `include` list wins outright rather than intersecting with `exclude` — having both set is a config mistake, and picking one deterministically beats silently applying a confusing intersection.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter RailRouteSelectorTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Hand off for commit**

```
feat: select rail routes and their shapes

Filters by route_type, then applies the config's include or
exclude list, then collects only the shape ids trips actually
reference. Include wins over exclude when both are set.
```

---

### Task 5: Mercator projection

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/GeoPoint.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/PlanePoint.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/MercatorProjector.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/MercatorProjectorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GeoPoint(double Latitude, double Longitude)`, `PlanePoint(double X, double Y)`, `MercatorProjector.Project(GeoPoint) -> PlanePoint`, `MercatorProjector.GroundMetresToPlane(double groundMetres, double latitudeDegrees) -> double`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class MercatorProjectorTests
{
    [Fact]
    public void ProjectsTheOriginToZero()
    {
        PlanePoint projected = MercatorProjector.Project(new GeoPoint(0, 0));

        Assert.Equal(0, projected.X, precision: 6);
        Assert.Equal(0, projected.Y, precision: 6);
    }

    [Fact]
    public void ProjectsLongitudeLinearlyAcrossTheEquator()
    {
        PlanePoint projected = MercatorProjector.Project(new GeoPoint(0, 180));

        // Half the equatorial circumference: pi * 6378137.
        Assert.Equal(20037508.34, projected.X, precision: 2);
    }

    [Fact]
    public void ProjectsNorthwardLatitudeToIncreasingY()
    {
        PlanePoint south = MercatorProjector.Project(new GeoPoint(42.32, -71.09));
        PlanePoint north = MercatorProjector.Project(new GeoPoint(42.36, -71.09));

        Assert.True(north.Y > south.Y,
            "Mercator Y must grow northward; the screen flip happens later, in normalization.");
    }

    [Fact]
    public void ConvertsGroundDistanceToStretchedPlaneDistance()
    {
        // At latitude 42, Mercator stretches by 1/cos(42) ~= 1.346.
        double planeMetres = MercatorProjector.GroundMetresToPlane(1000, latitudeDegrees: 42);

        Assert.Equal(1000 / Math.Cos(42 * Math.PI / 180), planeMetres, precision: 3);
    }

    [Fact]
    public void GroundConversionIsIdentityAtTheEquator()
    {
        Assert.Equal(1000, MercatorProjector.GroundMetresToPlane(1000, latitudeDegrees: 0), precision: 6);
    }
}
```

The last two tests guard the subtlest bug in this pipeline. Web Mercator's units are *not* ground metres — they are stretched by `1/cos(latitude)`. A `coreRadiusKm` of 22 at Boston's latitude is about 29.6 km of Mercator distance. Skipping this correction silently shrinks every extent by a third, and the map still looks plausible.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter MercatorProjectorTests
```

Expected: compile error — `MercatorProjector` does not exist.

- [ ] **Step 3: Write the geometry primitives**

`src/MetroDisplay.Gtfs.Static/Geometry/GeoPoint.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>A position on the globe, in degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);
```

`src/MetroDisplay.Gtfs.Static/Geometry/PlanePoint.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>A projected position in Web Mercator metres. Y grows northward.</summary>
public readonly record struct PlanePoint(double X, double Y);
```

`X` and `Y` here are members of a coordinate type, which is the one case `CLAUDE.md` permits.

- [ ] **Step 4: Write the projector**

`src/MetroDisplay.Gtfs.Static/Geometry/MercatorProjector.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Web Mercator (EPSG:3857). Conformal, so the network keeps its real shape at the scale
/// of a city. Distances are stretched by 1/cos(latitude), which matters whenever a real
/// ground distance has to be expressed in plane units.
/// </summary>
public static class MercatorProjector
{
    private const double EarthRadiusM = 6378137.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    public static PlanePoint Project(GeoPoint point)
    {
        double x = EarthRadiusM * point.Longitude * DegreesToRadians;
        double latitudeRadians = point.Latitude * DegreesToRadians;
        double y = EarthRadiusM * Math.Log(Math.Tan(Math.PI / 4.0 + latitudeRadians / 2.0));
        return new PlanePoint(x, y);
    }

    /// <summary>
    /// Converts a real ground distance into Web Mercator plane units at a given latitude.
    /// </summary>
    public static double GroundMetresToPlane(double groundMetres, double latitudeDegrees)
        => groundMetres / Math.Cos(latitudeDegrees * DegreesToRadians);

    /// <summary>Inverse of <see cref="GroundMetresToPlane"/>, for reporting spanKm.</summary>
    public static double PlaneMetresToGround(double planeMetres, double latitudeDegrees)
        => planeMetres * Math.Cos(latitudeDegrees * DegreesToRadians);
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter MercatorProjectorTests
```

Expected: PASS, 5 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: project coordinates to Web Mercator

Includes the ground-to-plane distance conversion. Mercator units
are stretched by 1/cos(latitude), so a radius given in real
kilometres has to be converted before it can bound a plane
rectangle.
```

---

### Task 6: Extent calculation

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/ExtentRectangle.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/ExtentCalculator.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/ExtentCalculatorTests.cs`

**Interfaces:**
- Consumes: `PlanePoint`, `GeoPoint`, `MercatorProjector` (Task 5), `ExtentConfig` (Task 2).
- Produces: `ExtentRectangle(double MinX, double MinY, double MaxX, double MaxY)` with `Width`, `Height`, `Aspect`, `Contains(PlanePoint)`; and `ExtentCalculator.Calculate(ExtentConfig config, IEnumerable<PlanePoint> contentPoints) -> ExtentRectangle`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class ExtentCalculatorTests
{
    private static readonly ExtentConfig BostonConfig =
        new(Core: [42.3555, -71.0605], CoreRadiusKm: 22);

    private static PlanePoint Project(double latitude, double longitude)
        => MercatorProjector.Project(new GeoPoint(latitude, longitude));

    [Fact]
    public void ClipsContentThatReachesBeyondTheCoreRadius()
    {
        // A point roughly 80 km west of the core, far outside a 22 km radius.
        PlanePoint farAway = Project(42.3555, -72.0);
        PlanePoint nearby = Project(42.3555, -71.05);

        ExtentRectangle extent = ExtentCalculator.Calculate(BostonConfig, [farAway, nearby]);

        Assert.False(extent.Contains(farAway));
        Assert.True(extent.Contains(nearby));
    }

    [Fact]
    public void ShrinksToContentWhenTheSystemIsSmallerThanTheRadius()
    {
        PlanePoint west = Project(42.3555, -71.07);
        PlanePoint east = Project(42.3555, -71.05);

        ExtentRectangle extent = ExtentCalculator.Calculate(BostonConfig, [west, east]);

        // Content spans ~1.6 km, far less than the 44 km square, so the extent follows it.
        Assert.Equal(west.X, extent.MinX, precision: 6);
        Assert.Equal(east.X, extent.MaxX, precision: 6);
    }

    [Fact]
    public void KeepsEverythingInsideTheRadiusVisible()
    {
        PlanePoint core = Project(42.3555, -71.0605);
        // ~20 km north of the core, inside the 22 km radius.
        PlanePoint northern = Project(42.5355, -71.0605);

        ExtentRectangle extent = ExtentCalculator.Calculate(BostonConfig, [core, northern]);

        Assert.True(extent.Contains(northern));
    }

    [Fact]
    public void ReportsAspectAsWidthOverHeight()
    {
        var extent = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

        Assert.Equal(2.0, extent.Aspect, precision: 6);
    }

    [Fact]
    public void ThrowsWhenThereIsNoContentToFrame()
    {
        Assert.Throws<InvalidOperationException>(
            () => ExtentCalculator.Calculate(BostonConfig, []));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter ExtentCalculatorTests
```

Expected: compile error — `ExtentRectangle` does not exist.

- [ ] **Step 3: Write the rectangle**

`src/MetroDisplay.Gtfs.Static/Geometry/ExtentRectangle.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>An axis-aligned rectangle in Web Mercator metres. Y grows northward.</summary>
public readonly record struct ExtentRectangle(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double LongestSpan => Math.Max(Width, Height);
    public double Aspect => Height == 0 ? 1.0 : Width / Height;

    public bool Contains(PlanePoint point)
        => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}
```

- [ ] **Step 4: Write the calculator**

`src/MetroDisplay.Gtfs.Static/Geometry/ExtentCalculator.cs`:

```csharp
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// The extent is the intersection of the content bounds with a square of side
/// 2 * coreRadiusKm centred on the configured core point. Everything within the radius is
/// always visible; a system smaller than the square is not padded out to fill it.
/// </summary>
public static class ExtentCalculator
{
    public static ExtentRectangle Calculate(ExtentConfig config, IEnumerable<PlanePoint> contentPoints)
    {
        double coreLatitude = config.Core[0];
        PlanePoint core = MercatorProjector.Project(new GeoPoint(coreLatitude, config.Core[1]));

        double radiusInPlaneUnits =
            MercatorProjector.GroundMetresToPlane(config.CoreRadiusKm * 1000.0, coreLatitude);

        double contentMinX = double.MaxValue, contentMinY = double.MaxValue;
        double contentMaxX = double.MinValue, contentMaxY = double.MinValue;
        bool sawAnyPoint = false;

        foreach (PlanePoint point in contentPoints)
        {
            sawAnyPoint = true;
            contentMinX = Math.Min(contentMinX, point.X);
            contentMinY = Math.Min(contentMinY, point.Y);
            contentMaxX = Math.Max(contentMaxX, point.X);
            contentMaxY = Math.Max(contentMaxY, point.Y);
        }

        if (!sawAnyPoint)
        {
            throw new InvalidOperationException(
                "Cannot calculate an extent with no content points. The rail filter probably matched nothing.");
        }

        return new ExtentRectangle(
            MinX: Math.Max(contentMinX, core.X - radiusInPlaneUnits),
            MinY: Math.Max(contentMinY, core.Y - radiusInPlaneUnits),
            MaxX: Math.Min(contentMaxX, core.X + radiusInPlaneUnits),
            MaxY: Math.Min(contentMaxY, core.Y + radiusInPlaneUnits));
    }

    /// <summary>Ground distance across the extent's longest axis, for ExtentInfo.SpanKm.</summary>
    public static double SpanKm(ExtentRectangle extent, double coreLatitude)
        => MercatorProjector.PlaneMetresToGround(extent.LongestSpan, coreLatitude) / 1000.0;
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter ExtentCalculatorTests
```

Expected: PASS, 5 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: calculate the map extent from a real-distance radius

Intersects content bounds with a square of side 2*coreRadiusKm
around the core point, converting the radius into plane units
first. An empty content set fails loudly rather than producing a
degenerate rectangle.
```

---

### Task 7: Polyline clipping and edge labels

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/PolylineClipper.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/PolylineClipperTests.cs`

**Interfaces:**
- Consumes: `PlanePoint`, `ExtentRectangle` (Tasks 5–6).
- Produces: `ClippedPolyline(IReadOnlyList<PlanePoint> Points, bool ExitsAtEnd, bool ExitsAtStart)`, and `PolylineClipper.Clip(IReadOnlyList<PlanePoint> points, ExtentRectangle extent) -> IReadOnlyList<ClippedPolyline>`, plus `PolylineClipper.ExitAngleDegrees(PlanePoint inside, PlanePoint boundary) -> double`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class PolylineClipperTests
{
    private static readonly ExtentRectangle UnitBox = new(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

    [Fact]
    public void LeavesAFullyContainedLineUntouched()
    {
        List<PlanePoint> points = [new(10, 10), new(50, 50), new(90, 90)];

        IReadOnlyList<ClippedPolyline> clipped = PolylineClipper.Clip(points, UnitBox);

        Assert.Single(clipped);
        Assert.Equal(3, clipped[0].Points.Count);
        Assert.False(clipped[0].ExitsAtEnd);
    }

    [Fact]
    public void CutsAtTheBoundaryAndMarksTheExit()
    {
        List<PlanePoint> points = [new(50, 50), new(150, 50)];

        IReadOnlyList<ClippedPolyline> clipped = PolylineClipper.Clip(points, UnitBox);

        Assert.Single(clipped);
        Assert.Equal(2, clipped[0].Points.Count);
        Assert.Equal(100, clipped[0].Points[1].X, precision: 6);
        Assert.Equal(50, clipped[0].Points[1].Y, precision: 6);
        Assert.True(clipped[0].ExitsAtEnd);
    }

    [Fact]
    public void SplitsALineThatLeavesAndReturns()
    {
        List<PlanePoint> points = [new(50, 50), new(150, 50), new(150, 80), new(50, 80)];

        IReadOnlyList<ClippedPolyline> clipped = PolylineClipper.Clip(points, UnitBox);

        Assert.Equal(2, clipped.Count);
        Assert.True(clipped[0].ExitsAtEnd);
        Assert.True(clipped[1].ExitsAtStart);
    }

    [Fact]
    public void DropsASegmentEntirelyOutside()
    {
        List<PlanePoint> points = [new(150, 150), new(200, 200)];

        IReadOnlyList<ClippedPolyline> clipped = PolylineClipper.Clip(points, UnitBox);

        Assert.Empty(clipped);
    }

    [Fact]
    public void ReportsExitAngleInScreenDegrees()
    {
        // Travelling due east in plane space. Screen Y is flipped, so east stays 0 degrees.
        double angle = PolylineClipper.ExitAngleDegrees(new PlanePoint(0, 0), new PlanePoint(10, 0));
        Assert.Equal(0, angle, precision: 6);

        // Travelling due north in plane space becomes upward on screen: -90 degrees.
        double northward = PolylineClipper.ExitAngleDegrees(new PlanePoint(0, 0), new PlanePoint(0, 10));
        Assert.Equal(-90, northward, precision: 6);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter PolylineClipperTests
```

Expected: compile error — `PolylineClipper` does not exist.

- [ ] **Step 3: Write the clipper**

`src/MetroDisplay.Gtfs.Static/Geometry/PolylineClipper.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>A run of a polyline that lies inside the extent, with its exit flags.</summary>
public sealed record ClippedPolyline(
    IReadOnlyList<PlanePoint> Points,
    bool ExitsAtStart,
    bool ExitsAtEnd);

/// <summary>
/// Clips polylines to the extent with Liang-Barsky, splitting into separate runs wherever a
/// line leaves and returns. Runs that end at the boundary are flagged so the caller can
/// attach a "TO ..." label there.
/// </summary>
public static class PolylineClipper
{
    public static IReadOnlyList<ClippedPolyline> Clip(
        IReadOnlyList<PlanePoint> points,
        ExtentRectangle extent)
    {
        var results = new List<ClippedPolyline>();
        var current = new List<PlanePoint>();
        bool currentStartedAtBoundary = false;

        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            PlanePoint segmentStart = points[segmentIndex];
            PlanePoint segmentEnd = points[segmentIndex + 1];

            if (!TryClipSegment(segmentStart, segmentEnd, extent,
                    out PlanePoint clippedStart, out PlanePoint clippedEnd,
                    out bool startWasMoved, out bool endWasMoved))
            {
                FlushRun(results, current, currentStartedAtBoundary, exitsAtEnd: true);
                current = [];
                currentStartedAtBoundary = false;
                continue;
            }

            if (current.Count == 0)
            {
                current.Add(clippedStart);
                currentStartedAtBoundary = startWasMoved;
            }
            else if (startWasMoved)
            {
                // The previous run ended at the boundary; start a fresh one here.
                FlushRun(results, current, currentStartedAtBoundary, exitsAtEnd: true);
                current = [clippedStart];
                currentStartedAtBoundary = true;
            }

            current.Add(clippedEnd);

            if (endWasMoved)
            {
                FlushRun(results, current, currentStartedAtBoundary, exitsAtEnd: true);
                current = [];
                currentStartedAtBoundary = false;
            }
        }

        FlushRun(results, current, currentStartedAtBoundary, exitsAtEnd: false);
        return results;
    }

    private static void FlushRun(
        List<ClippedPolyline> results,
        List<PlanePoint> run,
        bool startedAtBoundary,
        bool exitsAtEnd)
    {
        if (run.Count >= 2)
        {
            results.Add(new ClippedPolyline(run.ToArray(), startedAtBoundary, exitsAtEnd));
        }
    }

    /// <summary>Liang-Barsky segment clip. Reports whether each endpoint had to be moved.</summary>
    private static bool TryClipSegment(
        PlanePoint start,
        PlanePoint end,
        ExtentRectangle extent,
        out PlanePoint clippedStart,
        out PlanePoint clippedEnd,
        out bool startWasMoved,
        out bool endWasMoved)
    {
        clippedStart = start;
        clippedEnd = end;
        startWasMoved = false;
        endWasMoved = false;

        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double entering = 0.0;
        double leaving = 1.0;

        ReadOnlySpan<double> directions = [-deltaX, deltaX, -deltaY, deltaY];
        ReadOnlySpan<double> distances =
        [
            start.X - extent.MinX,
            extent.MaxX - start.X,
            start.Y - extent.MinY,
            extent.MaxY - start.Y,
        ];

        for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
        {
            double direction = directions[edgeIndex];
            double distance = distances[edgeIndex];

            if (direction == 0)
            {
                if (distance < 0)
                {
                    return false;   // Parallel to this edge and outside it.
                }
                continue;
            }

            double crossing = distance / direction;
            if (direction < 0)
            {
                entering = Math.Max(entering, crossing);
            }
            else
            {
                leaving = Math.Min(leaving, crossing);
            }
        }

        if (entering > leaving)
        {
            return false;
        }

        if (entering > 0)
        {
            clippedStart = new PlanePoint(start.X + entering * deltaX, start.Y + entering * deltaY);
            startWasMoved = true;
        }

        if (leaving < 1)
        {
            clippedEnd = new PlanePoint(start.X + leaving * deltaX, start.Y + leaving * deltaY);
            endWasMoved = true;
        }

        return true;
    }

    /// <summary>
    /// Bearing of travel at an exit point, in degrees, already converted to screen
    /// convention (Y down) so the renderer can rotate a label without transforming it.
    /// </summary>
    public static double ExitAngleDegrees(PlanePoint inside, PlanePoint boundary)
    {
        double deltaX = boundary.X - inside.X;
        double deltaY = boundary.Y - inside.Y;
        return Math.Atan2(-deltaY, deltaX) * 180.0 / Math.PI;
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter PolylineClipperTests
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Hand off for commit**

```
feat: clip polylines to the extent with exit flags

Liang-Barsky per segment, splitting into runs wherever a line
leaves and re-enters. Runs ending at the boundary are flagged so
a terminus label can be attached there. Exit angles are already
in screen convention.
```

---

### Task 8: Douglas-Peucker simplification

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/PolylineSimplifier.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/PolylineSimplifierTests.cs`

**Interfaces:**
- Consumes: `PlanePoint` (Task 5).
- Produces: `PolylineSimplifier.Simplify(IReadOnlyList<PlanePoint> points, double toleranceM) -> IReadOnlyList<PlanePoint>`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class PolylineSimplifierTests
{
    [Fact]
    public void CollapsesCollinearPointsToTheEndpoints()
    {
        List<PlanePoint> straightRun =
            [new(0, 0), new(10, 0), new(20, 0), new(30, 0), new(40, 0)];

        IReadOnlyList<PlanePoint> simplified = PolylineSimplifier.Simplify(straightRun, toleranceM: 1);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(new PlanePoint(0, 0), simplified[0]);
        Assert.Equal(new PlanePoint(40, 0), simplified[1]);
    }

    [Fact]
    public void KeepsAVertexThatDeviatesMoreThanTheTolerance()
    {
        List<PlanePoint> withCorner = [new(0, 0), new(20, 30), new(40, 0)];

        IReadOnlyList<PlanePoint> simplified = PolylineSimplifier.Simplify(withCorner, toleranceM: 5);

        Assert.Equal(3, simplified.Count);
    }

    [Fact]
    public void DropsAVertexThatDeviatesLessThanTheTolerance()
    {
        List<PlanePoint> withJitter = [new(0, 0), new(20, 2), new(40, 0)];

        IReadOnlyList<PlanePoint> simplified = PolylineSimplifier.Simplify(withJitter, toleranceM: 5);

        Assert.Equal(2, simplified.Count);
    }

    [Fact]
    public void NeverExceedsTheToleranceForAnyDroppedPoint()
    {
        var random = new Random(Seed: 20260922);
        List<PlanePoint> noisyLine = Enumerable.Range(0, 400)
            .Select(step => new PlanePoint(step * 10, random.NextDouble() * 4 - 2))
            .ToList();

        IReadOnlyList<PlanePoint> simplified = PolylineSimplifier.Simplify(noisyLine, toleranceM: 3);

        foreach (PlanePoint original in noisyLine)
        {
            double closest = double.MaxValue;
            for (int index = 0; index < simplified.Count - 1; index++)
            {
                closest = Math.Min(closest,
                    PolylineSimplifier.PerpendicularDistance(original, simplified[index], simplified[index + 1]));
            }
            Assert.True(closest <= 3.0 + 1e-9,
                $"Point {original} ended up {closest} from the simplified line, over the 3 m tolerance.");
        }
    }

    [Fact]
    public void ReturnsShortInputsUnchanged()
    {
        List<PlanePoint> pair = [new(0, 0), new(10, 10)];

        Assert.Equal(pair, PolylineSimplifier.Simplify(pair, toleranceM: 100));
    }
}
```

The fourth test is the one worth having: it asserts the algorithm's actual guarantee across a noisy input rather than checking one hand-picked case.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter PolylineSimplifierTests
```

Expected: compile error — `PolylineSimplifier` does not exist.

- [ ] **Step 3: Write the simplifier**

`src/MetroDisplay.Gtfs.Static/Geometry/PolylineSimplifier.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Douglas-Peucker. Guarantees no discarded point lies further than the tolerance from the
/// retained line, which is what lets the tolerance be stated in metres and reasoned about.
/// </summary>
public static class PolylineSimplifier
{
    public static IReadOnlyList<PlanePoint> Simplify(
        IReadOnlyList<PlanePoint> points,
        double toleranceM)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(points, 0, points.Count - 1, toleranceM, keep);

        var result = new List<PlanePoint>(points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            if (keep[index])
            {
                result.Add(points[index]);
            }
        }
        return result;
    }

    private static void SimplifyRange(
        IReadOnlyList<PlanePoint> points,
        int firstIndex,
        int lastIndex,
        double toleranceM,
        bool[] keep)
    {
        if (lastIndex <= firstIndex + 1)
        {
            return;
        }

        double worstDistance = 0;
        int worstIndex = firstIndex;

        for (int index = firstIndex + 1; index < lastIndex; index++)
        {
            double distance = PerpendicularDistance(points[index], points[firstIndex], points[lastIndex]);
            if (distance > worstDistance)
            {
                worstDistance = distance;
                worstIndex = index;
            }
        }

        if (worstDistance <= toleranceM)
        {
            return;
        }

        keep[worstIndex] = true;
        SimplifyRange(points, firstIndex, worstIndex, toleranceM, keep);
        SimplifyRange(points, worstIndex, lastIndex, toleranceM, keep);
    }

    /// <summary>Distance from a point to the segment between two others.</summary>
    public static double PerpendicularDistance(PlanePoint point, PlanePoint lineStart, PlanePoint lineEnd)
    {
        double deltaX = lineEnd.X - lineStart.X;
        double deltaY = lineEnd.Y - lineStart.Y;
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;

        if (lengthSquared == 0)
        {
            return Distance(point, lineStart);
        }

        double projection =
            ((point.X - lineStart.X) * deltaX + (point.Y - lineStart.Y) * deltaY) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);

        var closest = new PlanePoint(
            lineStart.X + projection * deltaX,
            lineStart.Y + projection * deltaY);

        return Distance(point, closest);
    }

    public static double Distance(PlanePoint first, PlanePoint second)
        => Math.Sqrt(
            (first.X - second.X) * (first.X - second.X) +
            (first.Y - second.Y) * (first.Y - second.Y));
}
```

The recursion is bounded by the input length; rail shapes run to a few thousand points at most, well inside the default stack.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter PolylineSimplifierTests
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Hand off for commit**

```
feat: simplify polylines with Douglas-Peucker

Tolerance is in metres and the property test asserts the
algorithm's real guarantee: no discarded point ends up further
than the tolerance from the retained line.
```

---

### Task 9: Shape deduplication

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/ShapeDeduplicator.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/ShapeDeduplicatorTests.cs`

**Interfaces:**
- Consumes: `GtfsShapePoint` (Task 3), `GeoPoint` (Task 5).
- Produces: `RawShape(string ShapeId, IReadOnlyList<GeoPoint> Points)`, `ShapeDeduplicator.Group(IEnumerable<GtfsShapePoint> shapePoints, IReadOnlySet<string> wantedShapeIds) -> IReadOnlyList<RawShape>`, and `ShapeDeduplicator.Deduplicate(IReadOnlyList<RawShape> shapes) -> DeduplicationResult` exposing `Shapes` and `CanonicalShapeIdByOriginalId`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class ShapeDeduplicatorTests
{
    private static GtfsShapePoint Point(string shapeId, double latitude, double longitude, int sequence)
        => new() { ShapeId = shapeId, Latitude = latitude, Longitude = longitude, Sequence = sequence };

    [Fact]
    public void GroupsPointsIntoShapesOrderedBySequence()
    {
        List<GtfsShapePoint> points =
        [
            Point("a", 42.2, -71.0, 3),
            Point("a", 42.0, -71.0, 1),
            Point("a", 42.1, -71.0, 2),
        ];

        IReadOnlyList<RawShape> shapes = ShapeDeduplicator.Group(points, new HashSet<string> { "a" });

        Assert.Single(shapes);
        Assert.Equal([42.0, 42.1, 42.2], shapes[0].Points.Select(point => point.Latitude));
    }

    [Fact]
    public void IgnoresShapesNotInTheWantedSet()
    {
        List<GtfsShapePoint> points = [Point("a", 42.0, -71.0, 1), Point("bus", 42.0, -71.0, 1)];

        IReadOnlyList<RawShape> shapes = ShapeDeduplicator.Group(points, new HashSet<string> { "a" });

        Assert.Single(shapes);
        Assert.Equal("a", shapes[0].ShapeId);
    }

    [Fact]
    public void CollapsesShapesWithIdenticalGeometry()
    {
        List<GeoPoint> geometry = [new(42.0, -71.0), new(42.1, -71.1)];
        List<RawShape> shapes =
        [
            new("pattern-1", geometry),
            new("pattern-2", geometry),
            new("different", [new GeoPoint(43.0, -72.0), new GeoPoint(43.1, -72.1)]),
        ];

        DeduplicationResult result = ShapeDeduplicator.Deduplicate(shapes);

        Assert.Equal(2, result.Shapes.Count);
        Assert.Equal("pattern-1", result.CanonicalShapeIdByOriginalId["pattern-2"]);
        Assert.Equal("different", result.CanonicalShapeIdByOriginalId["different"]);
    }

    [Fact]
    public void KeepsShapesThatDifferBeyondTheRoundingThreshold()
    {
        List<RawShape> shapes =
        [
            new("first", [new GeoPoint(42.000000, -71.0), new GeoPoint(42.1, -71.1)]),
            new("second", [new GeoPoint(42.000900, -71.0), new GeoPoint(42.1, -71.1)]),
        ];

        DeduplicationResult result = ShapeDeduplicator.Deduplicate(shapes);

        Assert.Equal(2, result.Shapes.Count);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter ShapeDeduplicatorTests
```

Expected: compile error — `ShapeDeduplicator` does not exist.

- [ ] **Step 3: Write the deduplicator**

`src/MetroDisplay.Gtfs.Static/Pipeline/ShapeDeduplicator.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MetroDisplay.Gtfs.Static.Geometry;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

public sealed record RawShape(string ShapeId, IReadOnlyList<GeoPoint> Points);

public sealed record DeduplicationResult(
    IReadOnlyList<RawShape> Shapes,
    IReadOnlyDictionary<string, string> CanonicalShapeIdByOriginalId);

/// <summary>
/// Agencies routinely publish one shape per trip pattern, so a single rail line can carry
/// dozens of near-identical polylines. Collapsing them by rounded geometry is the largest
/// size reduction in the pipeline, and it happens before any expensive stage runs.
/// </summary>
public static class ShapeDeduplicator
{
    /// <summary>Five decimal places is roughly a metre — below any real shape difference.</summary>
    private const int HashPrecision = 5;

    public static IReadOnlyList<RawShape> Group(
        IEnumerable<GtfsShapePoint> shapePoints,
        IReadOnlySet<string> wantedShapeIds)
    {
        return shapePoints
            .Where(point => wantedShapeIds.Contains(point.ShapeId))
            .GroupBy(point => point.ShapeId, StringComparer.Ordinal)
            .Select(group => new RawShape(
                group.Key,
                group.OrderBy(point => point.Sequence)
                     .Select(point => new GeoPoint(point.Latitude, point.Longitude))
                     .ToList()))
            .ToList();
    }

    public static DeduplicationResult Deduplicate(IReadOnlyList<RawShape> shapes)
    {
        var canonicalByHash = new Dictionary<string, RawShape>(StringComparer.Ordinal);
        var canonicalIdByOriginalId = new Dictionary<string, string>(StringComparer.Ordinal);
        var kept = new List<RawShape>();

        foreach (RawShape shape in shapes)
        {
            string hash = HashGeometry(shape.Points);
            if (canonicalByHash.TryGetValue(hash, out RawShape? existing))
            {
                canonicalIdByOriginalId[shape.ShapeId] = existing.ShapeId;
                continue;
            }

            canonicalByHash[hash] = shape;
            canonicalIdByOriginalId[shape.ShapeId] = shape.ShapeId;
            kept.Add(shape);
        }

        return new DeduplicationResult(kept, canonicalIdByOriginalId);
    }

    private static string HashGeometry(IReadOnlyList<GeoPoint> points)
    {
        var builder = new StringBuilder(points.Count * 24);
        foreach (GeoPoint point in points)
        {
            builder.Append(Math.Round(point.Latitude, HashPrecision).ToString("F5", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(Math.Round(point.Longitude, HashPrecision).ToString("F5", CultureInfo.InvariantCulture));
            builder.Append(';');
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(digest);
    }
}
```

`CanonicalShapeIdByOriginalId` matters beyond deduplication: Task 11's trip index maps every original shape id onto the surviving one, so a realtime trip referencing a collapsed pattern still resolves.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter ShapeDeduplicatorTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Hand off for commit**

```
feat: group and deduplicate shapes by geometry

Collapses trip-pattern duplicates by hashing rounded coordinates,
and records which original id maps to each surviving shape so
trips referencing a collapsed pattern still resolve.
```

---

### Task 10: Coordinate normalization

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/CoordinateNormalizer.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/CoordinateNormalizerTests.cs`

**Interfaces:**
- Consumes: `PlanePoint`, `ExtentRectangle` (Tasks 5–6).
- Produces: `CoordinateNormalizer.Normalize(PlanePoint point, ExtentRectangle extent) -> (double X, double Y)` and `CoordinateNormalizer.Flatten(IEnumerable<PlanePoint> points, ExtentRectangle extent) -> IReadOnlyList<double>`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class CoordinateNormalizerTests
{
    private static readonly ExtentRectangle WideExtent =
        new(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

    [Fact]
    public void DividesBothAxesByTheLongestSpanSoNothingIsDistorted()
    {
        (double x, double y) = CoordinateNormalizer.Normalize(new PlanePoint(200, 0), WideExtent);

        Assert.Equal(1.0, x, precision: 6);
        Assert.Equal(0.5, y, precision: 6);
    }

    [Fact]
    public void FlipsYIntoScreenConvention()
    {
        (double _, double northY) = CoordinateNormalizer.Normalize(new PlanePoint(0, 100), WideExtent);
        (double _, double southY) = CoordinateNormalizer.Normalize(new PlanePoint(0, 0), WideExtent);

        Assert.Equal(0.0, northY, precision: 6);
        Assert.True(southY > northY, "A northern point must land nearer the top of the screen.");
    }

    [Fact]
    public void QuantizesToFourDecimalPlaces()
    {
        var extent = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 3, MaxY: 3);

        (double x, double _) = CoordinateNormalizer.Normalize(new PlanePoint(1, 0), extent);

        Assert.Equal(0.3333, x);
    }

    [Fact]
    public void FlattensPointsIntoInterleavedPairs()
    {
        IReadOnlyList<double> flattened = CoordinateNormalizer.Flatten(
            [new PlanePoint(0, 0), new PlanePoint(200, 100)], WideExtent);

        Assert.Equal([0.0, 0.5, 1.0, 0.0], flattened);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter CoordinateNormalizerTests
```

Expected: compile error — `CoordinateNormalizer` does not exist.

- [ ] **Step 3: Write the normalizer**

`src/MetroDisplay.Gtfs.Static/Geometry/CoordinateNormalizer.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Maps projected metres into the normalized space the renderer consumes. Both axes are
/// divided by the same span, so a renderer that ignores aspect still draws correct
/// proportions. Y is flipped here, once, so no renderer can get it backwards.
/// </summary>
public static class CoordinateNormalizer
{
    /// <summary>
    /// Four places is about a fifth of a pixel at a 60 km extent on a 1920 px display —
    /// visually lossless, and roughly halves the JSON size.
    /// </summary>
    private const int Decimals = 4;

    public static (double X, double Y) Normalize(PlanePoint point, ExtentRectangle extent)
    {
        double longestSpan = extent.LongestSpan;
        if (longestSpan == 0)
        {
            return (0, 0);
        }

        double normalizedX = (point.X - extent.MinX) / longestSpan;
        double normalizedY = (extent.MaxY - point.Y) / longestSpan;

        return (Math.Round(normalizedX, Decimals), Math.Round(normalizedY, Decimals));
    }

    public static IReadOnlyList<double> Flatten(
        IEnumerable<PlanePoint> points,
        ExtentRectangle extent)
    {
        var flattened = new List<double>();
        foreach (PlanePoint point in points)
        {
            (double x, double y) = Normalize(point, extent);
            flattened.Add(x);
            flattened.Add(y);
        }
        return flattened;
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter CoordinateNormalizerTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Hand off for commit**

```
feat: normalize projected coordinates for the renderer

Both axes divide by the same span so distortion is impossible,
Y flips to screen convention once here, and values quantize to
four decimals.
```

---

### Task 11: Trip index

**Files:**
- Create: `src/MetroDisplay.Contracts/NetworkArtifact.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/TripIndexBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/TripIndexBuilderTests.cs`

**Interfaces:**
- Consumes: `GtfsTrip` (Task 3), `DeduplicationResult` (Task 9).
- Produces: `NetworkArtifact`, `ArtifactManifest`, `TripIndex`; and `TripIndexBuilder.Build(IReadOnlyList<GtfsTrip> trips, IReadOnlyDictionary<string, string> canonicalShapeIdByOriginalId) -> TripIndex`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class TripIndexBuilderTests
{
    private static GtfsTrip Trip(string tripId, string routeId, string shapeId, string headsign = "")
        => new() { TripId = tripId, RouteId = routeId, ShapeId = shapeId, TripHeadsign = headsign };

    [Fact]
    public void MapsEachTripToItsCanonicalShape()
    {
        List<GtfsTrip> trips = [Trip("trip-1", "Red", "pattern-2")];
        var canonical = new Dictionary<string, string> { ["pattern-2"] = "pattern-1" };

        TripIndex index = TripIndexBuilder.Build(trips, canonical);

        Assert.Equal("pattern-1", index.ShapeIdByTripId["trip-1"]);
    }

    [Fact]
    public void ChoosesTheMostUsedShapeAsARoutesPrimary()
    {
        List<GtfsTrip> trips =
        [
            Trip("trip-1", "Red", "shape-a"),
            Trip("trip-2", "Red", "shape-a"),
            Trip("trip-3", "Red", "shape-b"),
        ];
        var canonical = new Dictionary<string, string>
        {
            ["shape-a"] = "shape-a",
            ["shape-b"] = "shape-b",
        };

        TripIndex index = TripIndexBuilder.Build(trips, canonical);

        Assert.Equal("shape-a", index.PrimaryShapeIdByRouteId["Red"]);
    }

    [Fact]
    public void RecordsTheHeadsignForEachShape()
    {
        List<GtfsTrip> trips = [Trip("trip-1", "Red", "shape-a", headsign: "Alewife")];
        var canonical = new Dictionary<string, string> { ["shape-a"] = "shape-a" };

        TripIndex index = TripIndexBuilder.Build(trips, canonical);

        Assert.Equal("Alewife", index.HeadsignByShapeId["shape-a"]);
    }

    [Fact]
    public void SkipsTripsWithNoShape()
    {
        List<GtfsTrip> trips = [Trip("trip-1", "Red", shapeId: "")];

        TripIndex index = TripIndexBuilder.Build(trips, new Dictionary<string, string>());

        Assert.Empty(index.ShapeIdByTripId);
        Assert.Empty(index.PrimaryShapeIdByRouteId);
    }
}
```

`HeadsignByShapeId` is what Task 12 turns into the `TO ALEWIFE` edge labels, which is why the trip index carries it rather than the label builder re-reading `trips.txt`.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter TripIndexBuilderTests
```

Expected: compile error — `TripIndex` does not exist.

- [ ] **Step 3: Write the artifact records**

`src/MetroDisplay.Contracts/NetworkArtifact.cs`:

```csharp
namespace MetroDisplay.Contracts;

/// <summary>
/// A build output, not a database record: derived, immutable, versioned, and always
/// reproducible from its source feed. The server ships Scene verbatim; the realtime
/// pipeline uses TripIndex to resolve vehicles onto shapes.
/// </summary>
public sealed record NetworkArtifact(
    NetworkScene Scene,
    TripIndex TripIndex,
    ArtifactManifest Manifest);

public sealed record ArtifactManifest(
    string Version,
    string CityId,
    string SourceETag,
    DateOnly? FeedEndDate,
    DateTimeOffset BuiltAt);

public sealed record TripIndex(
    IReadOnlyDictionary<string, string> ShapeIdByTripId,
    IReadOnlyDictionary<string, string> PrimaryShapeIdByRouteId,
    IReadOnlyDictionary<string, string> HeadsignByShapeId);
```

- [ ] **Step 4: Write the builder**

`src/MetroDisplay.Gtfs.Static/Pipeline/TripIndexBuilder.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// Builds the lookups the realtime pipeline needs. Resolving these once at build time is
/// what keeps GTFS CSV knowledge out of the component that only speaks protobuf.
/// </summary>
public static class TripIndexBuilder
{
    public static TripIndex Build(
        IReadOnlyList<GtfsTrip> trips,
        IReadOnlyDictionary<string, string> canonicalShapeIdByOriginalId)
    {
        var shapeIdByTripId = new Dictionary<string, string>(StringComparer.Ordinal);
        var headsignByShapeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var tripCountsByRouteAndShape =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (GtfsTrip trip in trips)
        {
            if (string.IsNullOrEmpty(trip.ShapeId))
            {
                continue;
            }

            if (!canonicalShapeIdByOriginalId.TryGetValue(trip.ShapeId, out string? canonicalShapeId))
            {
                continue;
            }

            shapeIdByTripId[trip.TripId] = canonicalShapeId;

            if (!string.IsNullOrEmpty(trip.TripHeadsign))
            {
                headsignByShapeId.TryAdd(canonicalShapeId, trip.TripHeadsign);
            }

            if (!tripCountsByRouteAndShape.TryGetValue(trip.RouteId, out Dictionary<string, int>? counts))
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                tripCountsByRouteAndShape[trip.RouteId] = counts;
            }
            counts[canonicalShapeId] = counts.GetValueOrDefault(canonicalShapeId) + 1;
        }

        var primaryShapeIdByRouteId = tripCountsByRouteAndShape.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .OrderByDescending(shapeCount => shapeCount.Value)
                .ThenBy(shapeCount => shapeCount.Key, StringComparer.Ordinal)
                .First().Key,
            StringComparer.Ordinal);

        return new TripIndex(shapeIdByTripId, primaryShapeIdByRouteId, headsignByShapeId);
    }
}
```

The `ThenBy` on shape id breaks ties deterministically — without it, two equally-used shapes would make artifact builds non-reproducible for the same input.

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter TripIndexBuilderTests
```

Expected: PASS, 4 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: build the trip index and artifact records

Maps trips to canonical shapes, picks each route's most-used
shape as its primary with a deterministic tie-break, and records
headsigns for terminus labels.
```

---

### Task 12: Artifact assembly and validation

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/ArtifactValidator.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkArtifactBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkArtifactBuilderTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–11.
- Produces: `NetworkArtifactBuilder.Build(byte[] zipBytes, CityConfig config, string sourceETag, DateTimeOffset builtAt) -> NetworkArtifact`, and `ArtifactValidator.Validate(NetworkArtifact) -> void` (throws `ArtifactValidationException`).

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class NetworkArtifactBuilderTests
{
    private static readonly DateTimeOffset BuildTime =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static CityConfig TestConfig(double coreRadiusKm = 22) => new(
        Id: "test",
        Name: "TESTVILLE",
        Agency: "TT",
        Timezone: "America/New_York",
        StaticFeed: new FeedSource("https://example.test/gtfs.zip"),
        Realtime: new RealtimeSources(
            new FeedSource("https://example.test/vp"),
            new FeedSource("https://example.test/alerts")),
        RouteTypes: [0, 1],
        RouteFilter: null,
        Extent: new ExtentConfig([42.34, -71.08], coreRadiusKm),
        Simplify: new SimplifyConfig(ToleranceM: 25),
        DwellMs: 300000);

    private static NetworkArtifact BuildFromFixture(CityConfig? config = null)
    {
        byte[] zipBytes = GtfsFixtureBuilder.ThreeLineRailSystem().Build();
        return NetworkArtifactBuilder.Build(zipBytes, config ?? TestConfig(), "etag-abc", BuildTime);
    }

    [Fact]
    public void ProducesOneLinePerRailRouteAndNoBuses()
    {
        NetworkArtifact artifact = BuildFromFixture();

        Assert.Equal(["Green", "Red"], artifact.Scene.Lines.Select(line => line.Id).Order());
    }

    [Fact]
    public void EmitsNormalizedPointsInRange()
    {
        NetworkArtifact artifact = BuildFromFixture();

        foreach (ShapeGeometry shape in artifact.Scene.Lines.SelectMany(line => line.Shapes))
        {
            Assert.NotEmpty(shape.Points);
            Assert.All(shape.Points, value => Assert.InRange(value, 0.0, 1.0));
        }
    }

    [Fact]
    public void PrefixesRouteColorWithAHash()
    {
        NetworkArtifact artifact = BuildFromFixture();

        LineScene red = artifact.Scene.Lines.Single(line => line.Id == "Red");
        Assert.Equal("#DA291C", red.Color);
    }

    [Fact]
    public void CarriesFeedEndDateAndSourceEtagInTheManifest()
    {
        NetworkArtifact artifact = BuildFromFixture();

        Assert.Equal("etag-abc", artifact.Manifest.SourceETag);
        Assert.Equal(new DateOnly(2026, 12, 1), artifact.Manifest.FeedEndDate);
        Assert.Equal("test", artifact.Manifest.CityId);
        Assert.StartsWith("test@2026-09-22.", artifact.Manifest.Version);
    }

    [Fact]
    public void ProducesTheSameVersionForTheSameInput()
    {
        NetworkArtifact first = BuildFromFixture();
        NetworkArtifact second = BuildFromFixture();

        Assert.Equal(first.Manifest.Version, second.Manifest.Version);
    }

    [Fact]
    public void EmitsEdgeLabelsForLinesCutByATightExtent()
    {
        // A 1 km radius cuts every shape in the fixture.
        NetworkArtifact artifact = BuildFromFixture(TestConfig(coreRadiusKm: 1));

        Assert.NotEmpty(artifact.Scene.EdgeLabels);
        Assert.All(artifact.Scene.EdgeLabels, label => Assert.StartsWith("TO ", label.Text));
    }

    [Fact]
    public void ValidationRejectsAnArtifactWithNoShapes()
    {
        byte[] zipBytes = GtfsFixtureBuilder.ThreeLineRailSystem()
            .WithFile("routes.txt", """
                route_id,route_short_name,route_long_name,route_type,route_color
                Bus7,7,Bus Route 7,3,FFC72C
                """)
            .Build();

        var exception = Assert.Throws<ArtifactValidationException>(
            () => NetworkArtifactBuilder.Build(zipBytes, TestConfig(), "etag", BuildTime));

        Assert.Contains("no rail shapes", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter NetworkArtifactBuilderTests
```

Expected: compile error — `NetworkArtifactBuilder` does not exist.

- [ ] **Step 3: Write the validator**

`src/MetroDisplay.Gtfs.Static/Pipeline/ArtifactValidator.cs`:

```csharp
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Pipeline;

public sealed class ArtifactValidationException(string message) : Exception(message);

/// <summary>
/// The guard that stops a broken agency publish from blanking the display. A refresh that
/// fails validation is rejected and the previous artifact keeps serving.
/// </summary>
public static class ArtifactValidator
{
    public static void Validate(NetworkArtifact artifact)
    {
        int shapeCount = artifact.Scene.Lines.Sum(line => line.Shapes.Count);
        if (shapeCount == 0)
        {
            throw new ArtifactValidationException(
                $"Artifact for '{artifact.Manifest.CityId}' contains no rail shapes. " +
                "Refusing to publish it; the previous artifact remains in use.");
        }

        foreach (ShapeGeometry shape in artifact.Scene.Lines.SelectMany(line => line.Shapes))
        {
            if (shape.Points.Count < 4 || shape.Points.Count % 2 != 0)
            {
                throw new ArtifactValidationException(
                    $"Shape '{shape.Id}' has {shape.Points.Count} coordinate values; " +
                    "expected an even count of at least 4.");
            }
        }
    }
}
```

- [ ] **Step 4: Write the builder**

`src/MetroDisplay.Gtfs.Static/Pipeline/NetworkArtifactBuilder.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Geometry;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// Orchestrates the pipeline. Pure over its inputs: the same zip bytes and config always
/// produce the same artifact, including its version id.
/// </summary>
public static class NetworkArtifactBuilder
{
    public static NetworkArtifact Build(
        byte[] zipBytes,
        CityConfig config,
        string sourceETag,
        DateTimeOffset builtAt)
    {
        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);
        RailSelection selection = RailRouteSelector.Select(archive, config.RouteTypes, config.RouteFilter);

        IReadOnlyList<RawShape> grouped = ShapeDeduplicator.Group(archive.ShapePoints, selection.ShapeIds);
        DeduplicationResult deduplicated = ShapeDeduplicator.Deduplicate(grouped);

        // Checked here rather than in ArtifactValidator because the extent cannot be
        // calculated without content, and an empty feed is exactly the case the validator
        // exists to reject. Failing here keeps the caller's single catch meaningful.
        if (deduplicated.Shapes.Count == 0)
        {
            throw new ArtifactValidationException(
                $"Feed for '{config.Id}' yielded no rail shapes for route types " +
                $"[{string.Join(", ", config.RouteTypes)}]. Refusing to publish it; " +
                "the previous artifact remains in use.");
        }

        TripIndex tripIndex = TripIndexBuilder.Build(selection.Trips, deduplicated.CanonicalShapeIdByOriginalId);

        var projectedByShapeId = deduplicated.Shapes.ToDictionary(
            shape => shape.ShapeId,
            shape => (IReadOnlyList<PlanePoint>)shape.Points.Select(MercatorProjector.Project).ToList(),
            StringComparer.Ordinal);

        ExtentRectangle extent = ExtentCalculator.Calculate(
            config.Extent,
            projectedByShapeId.Values.SelectMany(points => points));

        var shapeIdByRouteId = selection.Trips
            .Where(trip => tripIndex.ShapeIdByTripId.ContainsKey(trip.TripId))
            .GroupBy(trip => trip.RouteId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(trip => tripIndex.ShapeIdByTripId[trip.TripId])
                              .Distinct(StringComparer.Ordinal)
                              .Order(StringComparer.Ordinal)
                              .ToList(),
                StringComparer.Ordinal);

        var lines = new List<LineScene>();
        var edgeLabels = new List<EdgeLabel>();

        foreach (GtfsRoute route in selection.Routes.OrderBy(route => route.RouteId, StringComparer.Ordinal))
        {
            if (!shapeIdByRouteId.TryGetValue(route.RouteId, out List<string>? shapeIds))
            {
                continue;
            }

            var shapes = new List<ShapeGeometry>();

            foreach (string shapeId in shapeIds)
            {
                IReadOnlyList<PlanePoint> projected = projectedByShapeId[shapeId];

                int runIndex = 0;
                foreach (ClippedPolyline run in PolylineClipper.Clip(projected, extent))
                {
                    IReadOnlyList<PlanePoint> simplified =
                        PolylineSimplifier.Simplify(run.Points, config.Simplify.ToleranceM);

                    if (simplified.Count < 2)
                    {
                        continue;
                    }

                    string runShapeId = runIndex == 0 ? shapeId : $"{shapeId}#{runIndex}";
                    runIndex++;

                    shapes.Add(new ShapeGeometry(
                        runShapeId,
                        CoordinateNormalizer.Flatten(simplified, extent),
                        LengthInMetres(simplified, config.Extent.Core[0])));

                    if (run.ExitsAtEnd)
                    {
                        edgeLabels.Add(BuildEdgeLabel(
                            simplified[^2], simplified[^1], extent, route.RouteId, shapeId, tripIndex));
                    }

                    if (run.ExitsAtStart)
                    {
                        edgeLabels.Add(BuildEdgeLabel(
                            simplified[1], simplified[0], extent, route.RouteId, shapeId, tripIndex));
                    }
                }
            }

            if (shapes.Count == 0)
            {
                continue;
            }

            lines.Add(new LineScene(
                route.RouteId,
                DisplayName(route),
                FormatColor(route.RouteColor),
                shapes));
        }

        List<StationMarker> stations = archive.Stops
            .Where(stop => stop.LocationType == 1)
            .Select(stop => (stop, plane: MercatorProjector.Project(new GeoPoint(stop.Latitude, stop.Longitude))))
            .Where(entry => extent.Contains(entry.plane))
            .Select(entry =>
            {
                (double x, double y) = CoordinateNormalizer.Normalize(entry.plane, extent);
                return new StationMarker(x, y, entry.stop.StopName, Rank: 1);
            })
            .ToList();

        var scene = new NetworkScene(
            ArtifactVersion: "pending",
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: new ExtentInfo(
                Aspect: Math.Round(extent.Aspect, 4),
                CoreRadiusKm: config.Extent.CoreRadiusKm,
                SpanKm: Math.Round(ExtentCalculator.SpanKm(extent, config.Extent.Core[0]), 2)),
            Lines: lines,
            Stations: stations,
            EdgeLabels: edgeLabels);

        string version = BuildVersion(config.Id, builtAt, scene);
        NetworkScene versionedScene = scene with { ArtifactVersion = version };

        var artifact = new NetworkArtifact(
            versionedScene,
            tripIndex,
            new ArtifactManifest(version, config.Id, sourceETag, archive.FeedInfo?.FeedEndDate, builtAt));

        ArtifactValidator.Validate(artifact);
        return artifact;
    }

    private static EdgeLabel BuildEdgeLabel(
        PlanePoint inside,
        PlanePoint boundary,
        ExtentRectangle extent,
        string routeId,
        string shapeId,
        TripIndex tripIndex)
    {
        (double x, double y) = CoordinateNormalizer.Normalize(boundary, extent);
        string destination = tripIndex.HeadsignByShapeId.GetValueOrDefault(shapeId, routeId);

        return new EdgeLabel(
            x,
            y,
            $"TO {destination.ToUpperInvariant()}",
            Math.Round(PolylineClipper.ExitAngleDegrees(inside, boundary), 1),
            routeId);
    }

    private static double LengthInMetres(IReadOnlyList<PlanePoint> points, double coreLatitude)
    {
        double planeLength = 0;
        for (int index = 0; index < points.Count - 1; index++)
        {
            planeLength += PolylineSimplifier.Distance(points[index], points[index + 1]);
        }
        return Math.Round(MercatorProjector.PlaneMetresToGround(planeLength, coreLatitude), 1);
    }

    private static string DisplayName(GtfsRoute route)
    {
        string name = !string.IsNullOrWhiteSpace(route.RouteShortName)
            ? route.RouteShortName
            : route.RouteLongName;
        return (string.IsNullOrWhiteSpace(name) ? route.RouteId : name).ToUpperInvariant();
    }

    private static string FormatColor(string routeColor)
        => string.IsNullOrWhiteSpace(routeColor) ? "#8E9BAD" : $"#{routeColor.TrimStart('#').ToUpperInvariant()}";

    /// <summary>
    /// Version is date plus a content hash, so identical input yields an identical version
    /// and a changed feed always yields a new one.
    /// </summary>
    private static string BuildVersion(string cityId, DateTimeOffset builtAt, NetworkScene scene)
    {
        string sceneJson = JsonSerializer.Serialize(scene, JsonDefaults.Options);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(sceneJson));
        string shortHash = Convert.ToHexStringLower(digest)[..8];
        return $"{cityId}@{builtAt:yyyy-MM-dd}.{shortHash}";
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter NetworkArtifactBuilderTests
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet test
```

Expected: PASS, 49 tests across all files.

- [ ] **Step 7: Hand off for commit**

```
feat: assemble and validate the network artifact

Runs the full pipeline over zip bytes and a city config: filter,
deduplicate, project, clip, simplify, normalize. Version is the
build date plus a content hash, so identical input reproduces an
identical artifact. Validation rejects an artifact with no rail
shapes rather than publishing an empty map.
```

---

### Task 13: Artifact storage

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Storage/INetworkArtifactStore.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Storage/FileNetworkArtifactStore.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/FileNetworkArtifactStoreTests.cs`

**Interfaces:**
- Consumes: `NetworkArtifact` (Task 11), `JsonDefaults.Options` (Task 1).
- Produces: `INetworkArtifactStore` with `GetLatestAsync`, `GetAsync`, `PutAsync`, `ListVersionsAsync`; and `FileNetworkArtifactStore(string rootDirectory)`.

- [ ] **Step 1: Write the failing test**

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Storage;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class FileNetworkArtifactStoreTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), $"metrodisplay-test-{Guid.NewGuid():N}");

    private static NetworkArtifact SampleArtifact(string version, string cityId = "test")
    {
        var scene = new NetworkScene(
            version,
            new CityMetadata(cityId, "TESTVILLE", "TT", "America/New_York"),
            new ExtentInfo(1.0, 22, 44),
            [new LineScene("Red", "RED", "#DA291C", [new ShapeGeometry("shape-a", [0, 0, 1, 1], 1000)])],
            [],
            []);

        return new NetworkArtifact(
            scene,
            new TripIndex(new Dictionary<string, string>(), new Dictionary<string, string>(),
                new Dictionary<string, string>()),
            new ArtifactManifest(version, cityId, "etag", null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task RoundTripsAnArtifact()
    {
        var store = new FileNetworkArtifactStore(rootDirectory);
        NetworkArtifact original = SampleArtifact("test@2026-09-22.aaaaaaaa");

        await store.PutAsync(original, CancellationToken.None);
        NetworkArtifact? loaded = await store.GetAsync("test", original.Manifest.Version, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(original.Manifest.Version, loaded.Manifest.Version);
        Assert.Equal("RED", loaded.Scene.Lines[0].Name);
    }

    [Fact]
    public async Task ReturnsNullForAVersionThatWasNeverWritten()
    {
        var store = new FileNetworkArtifactStore(rootDirectory);

        Assert.Null(await store.GetAsync("test", "test@1999-01-01.deadbeef", CancellationToken.None));
    }

    [Fact]
    public async Task LatestIsTheMostRecentlyBuiltArtifact()
    {
        var store = new FileNetworkArtifactStore(rootDirectory);
        await store.PutAsync(SampleArtifact("test@2026-09-01.aaaaaaaa"), CancellationToken.None);
        await store.PutAsync(SampleArtifact("test@2026-09-22.bbbbbbbb"), CancellationToken.None);

        NetworkArtifact? latest = await store.GetLatestAsync("test", CancellationToken.None);

        Assert.Equal("test@2026-09-22.bbbbbbbb", latest!.Manifest.Version);
    }

    [Fact]
    public async Task KeepsOlderVersionsAfterWritingANewOne()
    {
        var store = new FileNetworkArtifactStore(rootDirectory);
        await store.PutAsync(SampleArtifact("test@2026-09-01.aaaaaaaa"), CancellationToken.None);
        await store.PutAsync(SampleArtifact("test@2026-09-22.bbbbbbbb"), CancellationToken.None);

        IReadOnlyList<string> versions = await store.ListVersionsAsync("test", CancellationToken.None);

        Assert.Equal(2, versions.Count);
    }

    [Fact]
    public async Task IsolatesCitiesFromEachOther()
    {
        var store = new FileNetworkArtifactStore(rootDirectory);
        await store.PutAsync(SampleArtifact("mbta@2026-09-22.aaaaaaaa", "mbta"), CancellationToken.None);

        Assert.Null(await store.GetLatestAsync("bart", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}
```

Keeping older versions is what makes the validator's reject path meaningful — rejecting a bad refresh only helps if a good artifact is still there.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter FileNetworkArtifactStoreTests
```

Expected: compile error — `INetworkArtifactStore` does not exist.

- [ ] **Step 3: Write the interface**

`src/MetroDisplay.Gtfs.Static/Storage/INetworkArtifactStore.cs`:

```csharp
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Storage;

/// <summary>
/// Where built artifacts live. Disk today, blob storage later, with no caller changes —
/// and the seam that lets artifact building move to a scheduled job if it ever needs to.
/// </summary>
public interface INetworkArtifactStore
{
    Task<NetworkArtifact?> GetLatestAsync(string cityId, CancellationToken cancellationToken);

    Task<NetworkArtifact?> GetAsync(string cityId, string version, CancellationToken cancellationToken);

    Task<string> PutAsync(NetworkArtifact artifact, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListVersionsAsync(string cityId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write the file store**

`src/MetroDisplay.Gtfs.Static/Storage/FileNetworkArtifactStore.cs`:

```csharp
using System.Text.Json;
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Storage;

/// <summary>
/// Stores each artifact as one JSON file under {root}/{cityId}/{version}.json. Artifacts
/// are always read whole and never queried by field, so a document per version is the
/// right shape — any query capability would be paid for and never used.
/// </summary>
public sealed class FileNetworkArtifactStore(string rootDirectory) : INetworkArtifactStore
{
    public async Task<string> PutAsync(NetworkArtifact artifact, CancellationToken cancellationToken)
    {
        string cityDirectory = Path.Combine(rootDirectory, artifact.Manifest.CityId);
        Directory.CreateDirectory(cityDirectory);

        string path = Path.Combine(cityDirectory, $"{FileStem(artifact.Manifest.Version)}.json");
        string temporaryPath = path + ".tmp";

        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, artifact, JsonDefaults.Options, cancellationToken);
        }

        // Write-then-move so a crash mid-write cannot leave a half-parsed artifact behind.
        File.Move(temporaryPath, path, overwrite: true);
        return artifact.Manifest.Version;
    }

    public async Task<NetworkArtifact?> GetAsync(
        string cityId, string version, CancellationToken cancellationToken)
    {
        string path = Path.Combine(rootDirectory, cityId, $"{FileStem(version)}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<NetworkArtifact>(
            stream, JsonDefaults.Options, cancellationToken);
    }

    public async Task<NetworkArtifact?> GetLatestAsync(string cityId, CancellationToken cancellationToken)
    {
        string cityDirectory = Path.Combine(rootDirectory, cityId);
        if (!Directory.Exists(cityDirectory))
        {
            return null;
        }

        // Ordered by name, not write time: version ids are date-prefixed so they sort
        // correctly, and two artifacts written in the same filesystem tick would otherwise
        // be indistinguishable.
        string? newestPath = Directory.GetFiles(cityDirectory, "*.json")
            .OrderByDescending(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .FirstOrDefault();

        if (newestPath is null)
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(newestPath);
        return await JsonSerializer.DeserializeAsync<NetworkArtifact>(
            stream, JsonDefaults.Options, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListVersionsAsync(string cityId, CancellationToken cancellationToken)
    {
        string cityDirectory = Path.Combine(rootDirectory, cityId);
        if (!Directory.Exists(cityDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> versions = Directory.GetFiles(cityDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(stem => stem is not null)
            .Select(stem => VersionFromStem(stem!))
            .Order(StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(versions);
    }

    /// <summary>Version ids contain '@', which is legal on disk but awkward; store it as '_'.</summary>
    private static string FileStem(string version) => version.Replace('@', '_');

    /// <summary>
    /// Inverse of <see cref="FileStem"/>. Only the first underscore is restored, so a city
    /// id containing an underscore survives the round trip.
    /// </summary>
    private static string VersionFromStem(string stem)
    {
        int separator = stem.IndexOf('_');
        return separator < 0 ? stem : string.Concat(stem[..separator], "@", stem[(separator + 1)..]);
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter FileNetworkArtifactStoreTests
```

Expected: PASS, 5 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: store artifacts as versioned JSON documents

One file per version under a per-city directory, written through
a temporary file and moved into place so a crash cannot leave a
half-written artifact. Older versions are retained so a rejected
refresh has something to fall back to.
```

---

### Task 14: Feed client and CLI

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Feeds/StaticFeedResult.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Feeds/IStaticFeedClient.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Feeds/HttpStaticFeedClient.cs`
- Create: `src/MetroDisplay.Tools/MetroDisplay.Tools.csproj`
- Create: `src/MetroDisplay.Tools/Program.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/HttpStaticFeedClientTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `IStaticFeedClient.FetchAsync(FeedSource source, string? knownETag, CancellationToken) -> StaticFeedResult`; CLI command `build-artifact <cityId>`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Feeds;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class HttpStaticFeedClientTests
{
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ReturnsContentAndEtagOnASuccessfulFetch()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"abc123\"");
        var handler = new StubHandler(response);
        var client = new HttpStaticFeedClient(new HttpClient(handler));

        StaticFeedResult result = await client.FetchAsync(
            new FeedSource("https://example.test/gtfs.zip"), knownETag: null, CancellationToken.None);

        Assert.False(result.NotModified);
        Assert.Equal([1, 2, 3], result.Content);
        Assert.Equal("\"abc123\"", result.ETag);
    }

    [Fact]
    public async Task ReportsNotModifiedWithoutContent()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NotModified));
        var client = new HttpStaticFeedClient(new HttpClient(handler));

        StaticFeedResult result = await client.FetchAsync(
            new FeedSource("https://example.test/gtfs.zip"), knownETag: "\"abc123\"", CancellationToken.None);

        Assert.True(result.NotModified);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task SendsTheKnownEtagAsIfNoneMatch()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NotModified));
        var client = new HttpStaticFeedClient(new HttpClient(handler));

        await client.FetchAsync(
            new FeedSource("https://example.test/gtfs.zip"), knownETag: "\"abc123\"", CancellationToken.None);

        Assert.Contains(handler.LastRequest!.Headers.IfNoneMatch,
            tag => tag.Tag == "\"abc123\"");
    }

    [Fact]
    public async Task SendsConfiguredHeaders()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        });
        var client = new HttpStaticFeedClient(new HttpClient(handler));
        var source = new FeedSource(
            "https://example.test/gtfs.zip",
            new Dictionary<string, string> { ["x-api-key"] = "secret-value" });

        await client.FetchAsync(source, knownETag: null, CancellationToken.None);

        Assert.Equal("secret-value", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task ThrowsOnAServerError()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpStaticFeedClient(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.FetchAsync(
            new FeedSource("https://example.test/gtfs.zip"), knownETag: null, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter HttpStaticFeedClientTests
```

Expected: compile error — `HttpStaticFeedClient` does not exist.

- [ ] **Step 3: Write the feed client**

`src/MetroDisplay.Gtfs.Static/Feeds/StaticFeedResult.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Feeds;

/// <summary>
/// A conditional fetch result. NotModified means the agency returned 304 and there is
/// nothing to rebuild — the common case on a daily check.
/// </summary>
public sealed record StaticFeedResult(bool NotModified, byte[]? Content, string? ETag);
```

`src/MetroDisplay.Gtfs.Static/Feeds/IStaticFeedClient.cs`:

```csharp
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Feeds;

public interface IStaticFeedClient
{
    Task<StaticFeedResult> FetchAsync(
        FeedSource source, string? knownETag, CancellationToken cancellationToken);
}
```

`src/MetroDisplay.Gtfs.Static/Feeds/HttpStaticFeedClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using MetroDisplay.Contracts;

namespace MetroDisplay.Gtfs.Static.Feeds;

/// <summary>
/// Fetches a static GTFS archive with a conditional GET. This is the only part of the
/// static pipeline that touches the network, which is what keeps every stage behind it
/// testable against fixture bytes.
/// </summary>
public sealed class HttpStaticFeedClient(HttpClient httpClient) : IStaticFeedClient
{
    public async Task<StaticFeedResult> FetchAsync(
        FeedSource source, string? knownETag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);

        if (!string.IsNullOrEmpty(knownETag))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(knownETag));
        }

        if (source.Headers is not null)
        {
            foreach ((string name, string value) in source.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new StaticFeedResult(NotModified: true, Content: null, ETag: knownETag);
        }

        response.EnsureSuccessStatusCode();

        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new StaticFeedResult(
            NotModified: false,
            Content: content,
            ETag: response.Headers.ETag?.ToString());
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter HttpStaticFeedClientTests
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Create the CLI project**

```bash
dotnet new console -n MetroDisplay.Tools -o src/MetroDisplay.Tools -f net10.0
dotnet sln add src/MetroDisplay.Tools
dotnet add src/MetroDisplay.Tools reference src/MetroDisplay.Gtfs.Static
```

- [ ] **Step 6: Write the CLI**

`src/MetroDisplay.Tools/Program.cs`:

```csharp
using System.Diagnostics;
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Feeds;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using MetroDisplay.Gtfs.Static.Storage;

if (args.Length < 2 || args[0] != "build-artifact")
{
    Console.Error.WriteLine("Usage: MetroDisplay.Tools build-artifact <cityId>");
    return 1;
}

string cityId = args[1];
string repositoryRoot = Directory.GetCurrentDirectory();
string configPath = Path.Combine(repositoryRoot, "cities", $"{cityId}.json");

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"No city config at {configPath}");
    return 1;
}

CityConfig config = CityConfigLoader.LoadFromProcessEnvironment(await File.ReadAllTextAsync(configPath));
var store = new FileNetworkArtifactStore(Path.Combine(repositoryRoot, ".artifacts"));

NetworkArtifact? existing = await store.GetLatestAsync(cityId, CancellationToken.None);
string? knownETag = existing?.Manifest.SourceETag;

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var feedClient = new HttpStaticFeedClient(httpClient);

Console.WriteLine($"Fetching {config.StaticFeed.Url}");
var stopwatch = Stopwatch.StartNew();

StaticFeedResult feed = await feedClient.FetchAsync(config.StaticFeed, knownETag, CancellationToken.None);

if (feed.NotModified)
{
    Console.WriteLine($"Feed unchanged (304). Keeping {existing!.Manifest.Version}.");
    return 0;
}

Console.WriteLine($"Downloaded {feed.Content!.Length / 1024 / 1024} MB in {stopwatch.Elapsed.TotalSeconds:F1}s");

NetworkArtifact artifact;
try
{
    artifact = NetworkArtifactBuilder.Build(
        feed.Content, config, feed.ETag ?? "", DateTimeOffset.UtcNow);
}
catch (ArtifactValidationException validationFailure)
{
    Console.Error.WriteLine($"Rejected: {validationFailure.Message}");
    return 1;
}

await store.PutAsync(artifact, CancellationToken.None);

int shapeCount = artifact.Scene.Lines.Sum(line => line.Shapes.Count);
int pointCount = artifact.Scene.Lines.Sum(line => line.Shapes.Sum(shape => shape.Points.Count / 2));

Console.WriteLine($"Built {artifact.Manifest.Version}");
Console.WriteLine($"  lines      {artifact.Scene.Lines.Count}");
Console.WriteLine($"  shapes     {shapeCount}");
Console.WriteLine($"  points     {pointCount}");
Console.WriteLine($"  stations   {artifact.Scene.Stations.Count}");
Console.WriteLine($"  edgeLabels {artifact.Scene.EdgeLabels.Count}");
Console.WriteLine($"  extent     {artifact.Scene.Extent.SpanKm} km across, aspect {artifact.Scene.Extent.Aspect}");
Console.WriteLine($"  feed ends  {artifact.Manifest.FeedEndDate?.ToString() ?? "not stated"}");

return 0;
```

- [ ] **Step 7: Run it against the real MBTA feed**

```bash
dotnet run --project src/MetroDisplay.Tools -- build-artifact mbta
```

Expected: a version line plus counts. Sanity checks before moving on — lines should be 4 or so (Red, Orange, Blue, Green), points in the low thousands after simplification, `spanKm` at or under 44, and stations a few dozen. If points come back in the tens of thousands, `toleranceM` is too low; if lines is 0, the `routeTypes` filter is wrong for this agency.

- [ ] **Step 8: Add the artifacts directory to gitignore**

`.artifacts/` is already listed in `.gitignore`. Confirm with `git status` that no artifact JSON is staged — these are derived and must never be committed.

- [ ] **Step 9: Run the whole suite**

```bash
dotnet test
```

Expected: PASS, 59 tests.

- [ ] **Step 10: Hand off for commit**

```
feat: fetch feeds conditionally and add the build CLI

build-artifact fetches with If-None-Match, skips the rebuild on
304, and reports line, shape, point, and extent counts so the
output can be sanity-checked before the renderer exists.
```

---

## Known simplifications carried into Plan 2

- **`StationMarker.Rank` is always 1.** Spec §8 shows a rank of 2 without defining the scale.
  It is presumably a prominence hint — draw interchanges larger than ordinary stops — but
  deriving it needs a rule (transfer count? route count through the stop?) that only matters
  once stations are visible. Plan 2 decides it against a real map rather than inventing it here.
- **Only `location_type == 1` stops are emitted**, which is the GTFS parent-station marker.
  Agencies that do not model parent stations will produce zero stations; if MBTA comes back
  empty at Task 14, fall back to `location_type == 0` and deduplicate by name.
- **`routeTypes` is `[0, 1]`** for MBTA — commuter rail is excluded because it sprawls well
  past a 22 km extent. Revisit once the map is on screen.

## Done when

- `dotnet test` passes with 64 tests.
- `dotnet run --project src/MetroDisplay.Tools -- build-artifact mbta` writes `.artifacts/mbta/mbta_<date>.<hash>.json`.
- Running it twice in a row reports `Feed unchanged (304)` the second time.
- The artifact's `scene` object is byte-identical in shape to spec §8's `network` message, minus `rotation`, which the server adds at send time.

Plan 2 picks this artifact up and draws it.
