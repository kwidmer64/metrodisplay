# Slice 1: Lines on Screen Implementation Plan

**Goal:** Boston's rail lines drawn in their route colours in a browser, built from the MBTA
static GTFS feed by a .NET server.

**Architecture:** `MetroDisplay.Gtfs.Static` turns zip bytes and a city config into a
`NetworkScene` through small pure stages: read, select rail, project, fit to bounds,
normalize. A new `MetroDisplay.Server` downloads the zip once, builds the scene before it
starts listening, and serves it at `GET /api/network`. A new `web/` renderer fetches the
scene, contain-fits it to a canvas, and strokes each shape as a polyline.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit 2.9.3, CsvHelper 33.1.0, ASP.NET Core
minimal API, Microsoft.AspNetCore.Mvc.Testing 10.0.12. Node 24, Vite 8, TypeScript 6.0,
Vitest 5, canvas2d.

**Spec:** `docs/specs/2026-09-22-metrodisplay-design.md`. This is slice 1 of §14. It
implements §6 stages 2, 4 and 7 (normalizing to the network's own bounds; the §5 extent is
slice 3), the `lines` part of §8's `network` payload, §10's `ResizeObserver` sizing, and
§11's rule that the Server resolves placeholders from `IConfiguration`.

**Reference:** the superseded `docs/plans/2026-09-22-static-geometry-pipeline.md`. Tasks 1–5
here lift its Tasks 3, 4, 5, 10 and 12, cut down to this slice. Each task says what changed.

## Global Constraints

- Target framework `net10.0`, SDK 10.0.401. Node 24, npm 11.
- **The developer commits.** Every task ends with a hand-off step that supplies a commit
  message. No step runs `git add`, `git commit`, or any other git write.
- Commit messages: Conventional Commits prefix, imperative subject of 50 characters or
  fewer, body only when the why is not obvious, no trailers of any kind.
- **Naming:** readable words only. No single letters and no two- or three-character stubs,
  including locals, lambda parameters and generics (`TRecord`, `TElement`). `X` and `Y` are
  allowed only as members of a coordinate type (`PlanePoint.X`, a named tuple element).
- **Unit suffixes** on every unit-bearing name: `lengthM`, `spanKm`, `coreRadiusKm`,
  `marginPx`, `EarthRadiusM`.
- **The wire contract does not change in this slice.** JSON comes from the records in
  `MetroDisplay.Contracts` through `JsonDefaults.Options`.
- Public types carry `<summary>`/`<param>` doc comments in the style of `CityConfig.cs`.
- TDD throughout: write the failing test, watch it fail, write the minimal code, watch it pass.
- Builds finish with zero warnings.

## Review Focus

The five inputs most likely to bite that the spec implies but does not spell out. Each one
has a pinned test in the task that owns the code.

1. **`shapes.txt` rows out of sequence order.** GTFS promises order only through
   `shape_pt_sequence`, never file order. Lines must not zigzag.
   → `OrdersShapePointsBySequenceNotFileOrder` (Task 5)
2. **Trips pointing at a shape that `shapes.txt` does not contain, and rail routes with no
   trips at all.** The build skips them; it neither crashes nor sends an empty line.
   → `SkipsTripsWhoseShapeIsNotInShapesTxt`, `LeavesOutRailRoutesThatHaveNoTrips` (Task 5)
3. **`route_color` missing, lowercase, `#`-prefixed, or not hex.** The output is always
   `#RRGGBB`, and grey when the value is unusable.
   → `FormatsRouteColorAsHashAndSixHexDigits` (Task 5)
4. **The feed download fails.** Nothing is cached, so the next start tries again instead of
   trusting a bad file. → `CachesNothingWhenTheDownloadFails` (Task 6)
5. **The map container measures 0 × 0**, as in a background tab or before layout (§10).
   There is no negative or NaN scale. Nothing is drawn, and the map draws once the container
   has a size. → `returns a zero scale for a container that has not been laid out` (Task 7)

## File Structure

```
src/MetroDisplay.Gtfs.Static/
  Reading/GtfsRecords.cs            GtfsArchive, GtfsRoute, GtfsTrip, GtfsShapePoint
  Reading/GtfsArchiveReader.cs      zip bytes -> GtfsArchive (routes, trips, shapes)
  Pipeline/RailRouteSelector.cs     route_type filter, then include/exclude
  Pipeline/NetworkSceneBuilder.cs   zip bytes + CityConfig -> NetworkScene
  Geometry/GeoPoint.cs              latitude/longitude pair
  Geometry/PlanePoint.cs            Web Mercator X/Y pair
  Geometry/MercatorProjector.cs     lat/lon -> plane; plane distance -> ground distance
  Geometry/GroundDistance.cs        haversine; polyline length in metres
  Geometry/ExtentRectangle.cs       bounds of plane points; aspect, longest span
  Geometry/CoordinateNormalizer.cs  plane -> [0,1], Y flipped, 4 decimals

src/MetroDisplay.Server/
  MetroDisplay.Server.csproj
  Program.cs                        builds the scene at startup; GET /api/network
  ServerSettings.cs                 the MetroDisplay configuration section
  ConfigurationEnvironment.cs       IConfiguration -> placeholder values
  Feeds/StaticFeedCache.cs          download once, reuse from disk
  appsettings.json
  appsettings.Development.json      template default, unchanged
  Properties/launchSettings.json    one http profile on port 5180

tests/MetroDisplay.Gtfs.Static.Tests/
  GtfsFixtureBuilder.cs             builds GTFS zips in memory
  GtfsArchiveReaderTests.cs  RailRouteSelectorTests.cs  MercatorProjectorTests.cs
  GroundDistanceTests.cs  ExtentRectangleTests.cs  CoordinateNormalizerTests.cs
  NetworkSceneBuilderTests.cs

tests/MetroDisplay.Server.Tests/
  StubHttpHandler.cs  TemporaryDirectory.cs
  StaticFeedCacheTests.cs  ConfigurationEnvironmentTests.cs  NetworkEndpointTests.cs

web/
  package.json  package-lock.json  tsconfig.json  vite.config.ts  index.html
  src/contract.ts                   hand-written scene types (slice 2 generates them)
  src/fit.ts  src/fit.test.ts       contain-fit math
  src/draw.ts                       strokes the lines
  src/main.ts                       fetch, size, redraw

.gitignore                          gains .cache/
```

---

### Task 1: GTFS archive reader

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Reading/GtfsRecords.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Reading/GtfsArchiveReader.cs`
- Create: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsFixtureBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsArchiveReaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `GtfsArchive(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlyList<GtfsShapePoint> ShapePoints)`;
  `GtfsArchiveReader.Read(byte[] zipBytes) -> GtfsArchive`, which throws `InvalidDataException`
  naming the file (and the column, when one is missing); the test helper
  `GtfsFixtureBuilder` with `TwoRailLinesAndABus()`, `WithFile(name, contents)`,
  `WithoutFile(name)`, and `Build(bool withByteOrderMark = false) -> byte[]`.

**Changed from Plan 1 Task 3:**
- Reads only `routes.txt`, `trips.txt` and `shapes.txt`. Stops arrive in slice 5 and
  `feed_info.txt` in slice 14.
- A missing required file or column now throws with its name. The old reader returned an
  empty list and read a missing column as 0.
- The fixture is renamed for what it holds. It writes UTF-8 without a byte order mark
  unless asked, so both cases can be tested.

- [x] **Step 1: Add the CSV dependency**

```bash
dotnet add src/MetroDisplay.Gtfs.Static package CsvHelper --version 33.1.0
```

GTFS files are RFC 4180 CSV. Quoted fields containing commas are common, such as
`"Green Line, B Branch"`, and splitting on `,` by hand breaks on real feeds.

- [x] **Step 2: Write the fixture builder**

`tests/MetroDisplay.Gtfs.Static.Tests/GtfsFixtureBuilder.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace MetroDisplay.Gtfs.Static.Tests;

/// <summary>
/// Builds a GTFS zip in memory. A test reads as a description of a feed instead of depending
/// on a committed binary, and each test can vary one file.
/// </summary>
public sealed class GtfsFixtureBuilder
{
    private readonly Dictionary<string, string> filesByName = [];

    /// <summary>
    /// A subway line running both directions, a light rail line, and a bus route the
    /// rail filter must drop. Green's long name contains a comma inside quotes.
    /// </summary>
    public static GtfsFixtureBuilder TwoRailLinesAndABus()
    {
        return new GtfsFixtureBuilder()
            .WithFile("routes.txt", """
                route_id,route_short_name,route_long_name,route_type,route_color
                Red,,Red Line,1,DA291C
                Green,B,"Green Line, B Branch",0,00843D
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

    /// <param name="withByteOrderMark">Start every file with a UTF-8 byte order mark, as some agencies do.</param>
    public byte[] Build(bool withByteOrderMark = false)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: withByteOrderMark);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string contents) in filesByName)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), encoding);
                writer.Write(contents);
            }
        }
        return buffer.ToArray();
    }
}
```

- [x] **Step 3: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/GtfsArchiveReaderTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class GtfsArchiveReaderTests
{
    [Fact]
    public void ReadsRoutesTripsAndShapePoints()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Equal(3, archive.Routes.Count);
        Assert.Equal(5, archive.Trips.Count);
        Assert.Equal(11, archive.ShapePoints.Count);
    }

    [Fact]
    public void MapsColumnsByHeaderNameNotPosition()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("routes.txt", """
                route_type,route_color,route_id,route_long_name
                1,DA291C,Red,Red Line
                """)
            .Build();

        GtfsRoute route = GtfsArchiveReader.Read(zipBytes).Routes.Single();

        Assert.Equal("Red", route.RouteId);
        Assert.Equal(1, route.RouteType);
        Assert.Equal("DA291C", route.RouteColor);
        Assert.Equal("", route.RouteShortName);
    }

    [Fact]
    public void KeepsCommasInsideQuotedFields()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Contains(archive.Routes, route => route.RouteLongName == "Green Line, B Branch");
    }

    [Fact]
    public void ReadsFilesThatStartWithAByteOrderMark()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build(withByteOrderMark: true);

        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);

        Assert.Equal("Red", archive.Routes[0].RouteId);
    }

    [Fact]
    public void NamesTheFileWhenARequiredFileIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("shapes.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("shapes.txt", exception.Message);
    }

    [Fact]
    public void NamesTheColumnWhenARequiredColumnIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("shapes.txt", """
                shape_id,shape_pt_lon,shape_pt_sequence
                shape-red,-71.0900,1
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("shapes.txt", exception.Message);
        Assert.Contains("shape_pt_lat", exception.Message);
    }
}
```

The byte order mark test pins real-feed behaviour: some agencies publish UTF-8 with a BOM,
and a reader that keeps it would see the first header as `route_id` with an invisible U+FEFF in front of it.

- [x] **Step 4: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~GtfsArchiveReaderTests"
```

Expected: build error CS0246, `The type or namespace name 'GtfsArchive' could not be found`.

- [x] **Step 5: Write the records**

`src/MetroDisplay.Gtfs.Static/Reading/GtfsRecords.cs`:

```csharp
using CsvHelper.Configuration.Attributes;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// The parts of a GTFS feed this system reads.
/// </summary>
/// <param name="Routes">Every row of <c>routes.txt</c>, buses included; filtering happens later.</param>
/// <param name="Trips">Every row of <c>trips.txt</c>.</param>
/// <param name="ShapePoints">Every row of <c>shapes.txt</c>, in file order.</param>
public sealed record GtfsArchive(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlyList<GtfsShapePoint> ShapePoints);

/// <summary>
/// One row of <c>routes.txt</c>.
/// </summary>
public sealed class GtfsRoute
{
    [Name("route_id")] public string RouteId { get; set; } = "";
    [Name("route_short_name")] [Optional] public string RouteShortName { get; set; } = "";
    [Name("route_long_name")] [Optional] public string RouteLongName { get; set; } = "";
    [Name("route_type")] public int RouteType { get; set; }
    [Name("route_color")] [Optional] public string RouteColor { get; set; } = "";
}

/// <summary>
/// One row of <c>trips.txt</c>.
/// </summary>
public sealed class GtfsTrip
{
    [Name("route_id")] public string RouteId { get; set; } = "";
    [Name("trip_id")] public string TripId { get; set; } = "";
    [Name("shape_id")] [Optional] public string ShapeId { get; set; } = "";
}

/// <summary>
/// One row of <c>shapes.txt</c>: a single vertex of a shape's polyline.
/// </summary>
public sealed class GtfsShapePoint
{
    [Name("shape_id")] public string ShapeId { get; set; } = "";
    [Name("shape_pt_lat")] public double Latitude { get; set; }
    [Name("shape_pt_lon")] public double Longitude { get; set; }
    [Name("shape_pt_sequence")] public int Sequence { get; set; }
}
```

- [x] **Step 6: Write the reader**

`src/MetroDisplay.Gtfs.Static/Reading/GtfsArchiveReader.cs`:

```csharp
using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace MetroDisplay.Gtfs.Static.Reading;

/// <summary>
/// Reads the GTFS files this system needs out of a zip. <c>stop_times.txt</c>, the bulk of a
/// typical archive, is never opened.
/// </summary>
public static class GtfsArchiveReader
{
    /// <summary>
    /// Header validation stays on, so a missing required column fails instead of reading as 0.
    /// Columns marked <c>[Optional]</c> are exempt.
    /// </summary>
    private static readonly CsvConfiguration CsvSettings = new(CultureInfo.InvariantCulture)
    {
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    /// <summary>
    /// Reads routes, trips and shapes. <c>shapes.txt</c> is optional in GTFS but required
    /// here: without it there is no track geometry to draw.
    /// </summary>
    /// <param name="zipBytes">The GTFS archive as downloaded.</param>
    /// <exception cref="InvalidDataException">A required file or column is missing. The message names it.</exception>
    public static GtfsArchive Read(byte[] zipBytes)
    {
        using var buffer = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        return new GtfsArchive(
            Routes: ReadRequired<GtfsRoute>(archive, "routes.txt"),
            Trips: ReadRequired<GtfsTrip>(archive, "trips.txt"),
            ShapePoints: ReadRequired<GtfsShapePoint>(archive, "shapes.txt"));
    }

    private static List<TRecord> ReadRequired<TRecord>(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"GTFS archive has no {entryName}.");

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvSettings);
        try
        {
            return csv.GetRecords<TRecord>().ToList();
        }
        catch (HeaderValidationException exception)
        {
            string missingColumns = string.Join(", ", exception.InvalidHeaders.SelectMany(header => header.Names));
            throw new InvalidDataException($"{entryName} is missing required column(s): {missingColumns}.", exception);
        }
    }
}
```

`StreamReader` detects and strips a UTF-8 byte order mark by default, which is what the BOM
test relies on.

- [x] **Step 7: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~GtfsArchiveReaderTests"
```

Expected: PASS, 6 tests.

- [x] **Step 8: Run the whole suite**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 14 tests.

- [x] **Step 9: Hand off for commit**

```
feat: read GTFS routes, trips and shapes

Only the three files the map needs are opened. A missing file or
column fails with its name instead of reading as empty or zero.
```

---

### Task 2: Rail route selection

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/RailRouteSelector.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/RailRouteSelectorTests.cs`

**Interfaces:**
- Consumes: `GtfsArchive`, `GtfsRoute`, `GtfsTrip` (Task 1); `RouteFilter` (`MetroDisplay.Contracts`).
- Produces: `RailSelection(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlySet<string> ShapeIds)`;
  `RailRouteSelector.Select(GtfsArchive archive, IReadOnlyList<int> routeTypes, RouteFilter? filter) -> RailSelection`.

**Changed from Plan 1 Task 4:** only the fixture name and the doc comments.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/RailRouteSelectorTests.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class RailRouteSelectorTests
{
    private static GtfsArchive SampleArchive() =>
        GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

    [Fact]
    public void KeepsRailRouteTypesAndDropsBuses()
    {
        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter: null);

        Assert.Equal(new[] { "Green", "Red" }, selection.Routes.Select(route => route.RouteId).Order());
        Assert.DoesNotContain(selection.Trips, trip => trip.RouteId == "Bus7");
    }

    [Fact]
    public void CollectsOnlyShapeIdsReachableFromKeptTrips()
    {
        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter: null);

        Assert.Equal(new[] { "shape-green", "shape-red", "shape-red-rev" }, selection.ShapeIds.Order());
    }

    [Fact]
    public void AppliesTheExcludeListAfterTheRouteTypeFilter()
    {
        var filter = new RouteFilter(Exclude: ["Green"]);

        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter);

        Assert.Equal(new[] { "Red" }, selection.Routes.Select(route => route.RouteId));
        Assert.DoesNotContain("shape-green", selection.ShapeIds);
    }

    [Fact]
    public void AnIncludeListWinsOverEverythingElse()
    {
        var filter = new RouteFilter(Include: ["Green"], Exclude: ["Green"]);

        RailSelection selection = RailRouteSelector.Select(SampleArchive(), [0, 1], filter);

        Assert.Equal(new[] { "Green" }, selection.Routes.Select(route => route.RouteId));
    }
}
```

- [x] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~RailRouteSelectorTests"
```

Expected: build error CS0234, `The type or namespace name 'Pipeline' does not exist in the namespace 'MetroDisplay.Gtfs.Static'`. This is the first file in that namespace.

- [x] **Step 3: Write the selector**

`src/MetroDisplay.Gtfs.Static/Pipeline/RailRouteSelector.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// The rail subset of a feed.
/// </summary>
/// <param name="Routes">Routes that passed the route type filter and the include or exclude list.</param>
/// <param name="Trips">Trips on those routes.</param>
/// <param name="ShapeIds">Every non-empty <c>shape_id</c> those trips reference.</param>
public sealed record RailSelection(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlySet<string> ShapeIds);

/// <summary>
/// Narrows a whole-agency feed to the rail routes this city draws, and to the shapes those
/// routes actually use. Everything downstream works on this subset.
/// </summary>
public static class RailRouteSelector
{
    /// <param name="archive">The whole feed.</param>
    /// <param name="routeTypes">GTFS <c>route_type</c> values to keep.</param>
    /// <param name="filter">Optional include or exclude list by <c>route_id</c>. When both are set, include wins.</param>
    public static RailSelection Select(GtfsArchive archive, IReadOnlyList<int> routeTypes, RouteFilter? filter)
    {
        var wantedTypes = routeTypes.ToHashSet();

        IEnumerable<GtfsRoute> candidates = archive.Routes.Where(route => wantedTypes.Contains(route.RouteType));

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

        List<GtfsTrip> trips = archive.Trips.Where(trip => routeIds.Contains(trip.RouteId)).ToList();

        HashSet<string> shapeIds = trips
            .Select(trip => trip.ShapeId)
            .Where(shapeId => !string.IsNullOrEmpty(shapeId))
            .ToHashSet(StringComparer.Ordinal);

        return new RailSelection(routes, trips, shapeIds);
    }
}
```

An `include` list wins outright instead of intersecting with `exclude`. Setting both is a
config mistake, and picking one rule predictably is easier to reason about than a silent
intersection.

- [x] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~RailRouteSelectorTests"
```

Expected: PASS, 4 tests.

- [x] **Step 5: Hand off for commit**

```
feat: select rail routes and their shapes

Filters by route_type, then applies the include or exclude list,
then collects only the shape ids that kept trips reference.
Include wins when both lists are set.
```

---

### Task 3: Mercator projection and ground distance

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/GeoPoint.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/PlanePoint.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/MercatorProjector.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/GroundDistance.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/MercatorProjectorTests.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/GroundDistanceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GeoPoint(double Latitude, double Longitude)`; `PlanePoint(double X, double Y)`;
  `MercatorProjector.Project(GeoPoint) -> PlanePoint`;
  `MercatorProjector.PlaneMetresToGround(double planeMetres, double latitudeDegrees) -> double`;
  `GroundDistance.BetweenM(GeoPoint start, GeoPoint end) -> double`;
  `GroundDistance.PolylineLengthM(IReadOnlyList<GeoPoint> points) -> double`.

**Changed from Plan 1 Task 5:**
- `GroundMetresToPlane` waits for slice 3, which is the first slice to convert a configured
  ground distance.
- `PlaneMetresToGround` is here now, because `spanKm` needs it.
- `GroundDistance` is new. `lengthM` is measured on the ground from latitude and longitude
  instead of from plane distance, so it no longer depends on a reference latitude.
- The projector's locals are renamed from `x`/`y` to `eastingM`/`northingM` to meet the
  naming rules.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/MercatorProjectorTests.cs`:

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
    public void ProjectsLongitudeLinearlyAlongTheEquator()
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

        Assert.True(north.Y > south.Y, "Mercator Y grows northward; the screen flip happens later, in normalization.");
    }

    [Fact]
    public void ShrinksPlaneDistanceBackToGroundDistanceAwayFromTheEquator()
    {
        // Mercator stretches distance by 1/cos(latitude): about 1.346 at latitude 42.
        double groundMetres = MercatorProjector.PlaneMetresToGround(1000, latitudeDegrees: 42);

        Assert.Equal(1000 * Math.Cos(42 * Math.PI / 180), groundMetres, precision: 3);
    }

    [Fact]
    public void PlaneAndGroundDistanceAgreeAtTheEquator()
    {
        Assert.Equal(1000, MercatorProjector.PlaneMetresToGround(1000, latitudeDegrees: 0), precision: 6);
    }
}
```

`tests/MetroDisplay.Gtfs.Static.Tests/GroundDistanceTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class GroundDistanceTests
{
    /// <summary>One degree of arc on the mean-radius sphere: pi * 6371008.8 / 180.</summary>
    private const double OneDegreeM = 111195.08;

    [Fact]
    public void MeasuresOneDegreeOfLatitude()
    {
        double distanceM = GroundDistance.BetweenM(new GeoPoint(0, 0), new GeoPoint(1, 0));

        Assert.Equal(OneDegreeM, distanceM, tolerance: 0.01);
    }

    [Fact]
    public void SumsTheSegmentsOfAPolyline()
    {
        GeoPoint[] alongTheEquator = [new(0, 0), new(0, 1), new(0, 2)];

        Assert.Equal(2 * OneDegreeM, GroundDistance.PolylineLengthM(alongTheEquator), tolerance: 0.02);
    }

    [Fact]
    public void GivesASinglePointNoLength()
    {
        Assert.Equal(0, GroundDistance.PolylineLengthM([new GeoPoint(42.35, -71.06)]));
    }
}
```

The two `PlaneMetresToGround` tests guard this pipeline's subtlest bug. Web Mercator units
are not ground metres: they are stretched by `1/cos(latitude)`. Any plane distance reported
as a real one has to be shrunk back, or Boston reads a third larger than it is.

- [x] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~MercatorProjectorTests|FullyQualifiedName~GroundDistanceTests"
```

Expected: build error CS0234, `The type or namespace name 'Geometry' does not exist in the namespace 'MetroDisplay.Gtfs.Static'`. This is the first file in that namespace.

- [x] **Step 3: Write the point types**

`src/MetroDisplay.Gtfs.Static/Geometry/GeoPoint.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// A position on the globe.
/// </summary>
/// <param name="Latitude">Degrees north of the equator.</param>
/// <param name="Longitude">Degrees east of Greenwich.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);
```

`src/MetroDisplay.Gtfs.Static/Geometry/PlanePoint.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// A projected position in Web Mercator plane units. Y grows northward.
/// </summary>
/// <param name="X">Easting.</param>
/// <param name="Y">Northing.</param>
public readonly record struct PlanePoint(double X, double Y);
```

- [x] **Step 4: Write the projector and ground distance**

`src/MetroDisplay.Gtfs.Static/Geometry/MercatorProjector.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Web Mercator (EPSG:3857). Conformal, so a network keeps its real shape at city scale.
/// Plane distances are stretched by 1/cos(latitude) relative to the ground.
/// </summary>
public static class MercatorProjector
{
    /// <summary>The WGS 84 equatorial radius that EPSG:3857 is defined on.</summary>
    private const double EarthRadiusM = 6378137.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    public static PlanePoint Project(GeoPoint point)
    {
        double eastingM = EarthRadiusM * point.Longitude * DegreesToRadians;
        double latitudeRadians = point.Latitude * DegreesToRadians;
        double northingM = EarthRadiusM * Math.Log(Math.Tan(Math.PI / 4.0 + latitudeRadians / 2.0));
        return new PlanePoint(eastingM, northingM);
    }

    /// <summary>
    /// Converts a plane distance to the ground distance it represents at a given latitude.
    /// </summary>
    /// <param name="planeMetres">Distance measured between projected points.</param>
    /// <param name="latitudeDegrees">Latitude the distance sits at.</param>
    public static double PlaneMetresToGround(double planeMetres, double latitudeDegrees)
        => planeMetres * Math.Cos(latitudeDegrees * DegreesToRadians);
}
```

`src/MetroDisplay.Gtfs.Static/Geometry/GroundDistance.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Distances over the Earth's surface, for reporting real lengths. Never used for drawing.
/// </summary>
public static class GroundDistance
{
    /// <summary>
    /// Mean Earth radius (IUGG). Closer than the equatorial radius for surface distance at
    /// any latitude a city sits at.
    /// </summary>
    private const double MeanEarthRadiusM = 6371008.8;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>
    /// Great-circle distance by the haversine formula.
    /// </summary>
    public static double BetweenM(GeoPoint start, GeoPoint end)
    {
        double startLatitude = start.Latitude * DegreesToRadians;
        double endLatitude = end.Latitude * DegreesToRadians;
        double latitudeChange = endLatitude - startLatitude;
        double longitudeChange = (end.Longitude - start.Longitude) * DegreesToRadians;

        double haversine = Math.Pow(Math.Sin(latitudeChange / 2), 2)
            + Math.Cos(startLatitude) * Math.Cos(endLatitude) * Math.Pow(Math.Sin(longitudeChange / 2), 2);

        return 2 * MeanEarthRadiusM * Math.Asin(Math.Sqrt(haversine));
    }

    /// <summary>
    /// Total ground length of a polyline. Zero for fewer than two points.
    /// </summary>
    public static double PolylineLengthM(IReadOnlyList<GeoPoint> points)
    {
        double lengthM = 0;
        for (int index = 1; index < points.Count; index++)
        {
            lengthM += BetweenM(points[index - 1], points[index]);
        }
        return lengthM;
    }
}
```

- [x] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~MercatorProjectorTests|FullyQualifiedName~GroundDistanceTests"
```

Expected: PASS, 10 tests.

**As built:** a gap analysis after GREEN found that the eight planned tests exercised the
latitude terms only at the equator. Mercator `Y` was pinned only at 0 and for direction,
and the haversine `cos(latitude)` factor only ran where cos = 1. Two tests close that:
`StretchesNorthingWithLatitude` pins `Y` at 42° to 5,160,979.44 (a plain `R * latitude` gives
4,675,418.61), and `ShortensLongitudeAwayFromTheEquator` pins 0.01° of longitude at 42.35° N
to 821.78 m (1,111.95 m without the factor). Each was shown to fail against its mutant and
only that mutant. Later suite totals include both.

- [x] **Step 6: Hand off for commit**

```
feat: add Mercator projection and ground distance

Plane distance converts back to ground distance by cos(latitude),
since Mercator units are stretched away from the equator. Shape
length is measured on the ground from latitude and longitude.
```

---

### Task 4: Fit-to-bounds normalization

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/ExtentRectangle.cs`
- Create: `src/MetroDisplay.Gtfs.Static/Geometry/CoordinateNormalizer.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/ExtentRectangleTests.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/CoordinateNormalizerTests.cs`

**Interfaces:**
- Consumes: `PlanePoint` (Task 3).
- Produces: `ExtentRectangle(double MinX, double MinY, double MaxX, double MaxY)` with
  `Width`, `Height`, `LongestSpan`, `Aspect`, and `static ExtentRectangle Enclosing(IEnumerable<PlanePoint> points)`;
  `CoordinateNormalizer.Normalize(PlanePoint point, ExtentRectangle extent) -> (double X, double Y)`;
  `CoordinateNormalizer.Flatten(IEnumerable<PlanePoint> points, ExtentRectangle extent) -> IReadOnlyList<double>`.

**Changed from Plan 1:**
- `ExtentRectangle` comes from Plan 1 Task 6, minus `Contains` (clipping needs it in
  slice 3) and plus `Enclosing`. In this slice the rectangle is the network's own bounds.
  Slice 3 intersects it with the configured core square.
- The normalizer is Plan 1 Task 10 with its `x`/`y` locals renamed.

- [ ] **Step 1: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/ExtentRectangleTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class ExtentRectangleTests
{
    [Fact]
    public void EnclosesEveryPoint()
    {
        ExtentRectangle bounds = ExtentRectangle.Enclosing(
            [new PlanePoint(10, 20), new PlanePoint(-5, 40), new PlanePoint(30, 0)]);

        Assert.Equal(new ExtentRectangle(MinX: -5, MinY: 0, MaxX: 30, MaxY: 40), bounds);
    }

    [Fact]
    public void ReportsWidthOverHeightAsAspect()
    {
        var bounds = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

        Assert.Equal(2.0, bounds.Aspect);
        Assert.Equal(200, bounds.LongestSpan);
    }

    [Fact]
    public void RefusesToBoundAnEmptySetOfPoints()
    {
        Assert.Throws<ArgumentException>(() => ExtentRectangle.Enclosing([]));
    }
}
```

`tests/MetroDisplay.Gtfs.Static.Tests/CoordinateNormalizerTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Geometry;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class CoordinateNormalizerTests
{
    private static readonly ExtentRectangle WideExtent = new(MinX: 0, MinY: 0, MaxX: 200, MaxY: 100);

    [Fact]
    public void DividesBothAxesByTheLongestSpanSoNothingIsDistorted()
    {
        (double normalizedX, double normalizedY) = CoordinateNormalizer.Normalize(new PlanePoint(200, 0), WideExtent);

        Assert.Equal(1.0, normalizedX, precision: 6);
        Assert.Equal(0.5, normalizedY, precision: 6);
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

        (double normalizedX, double _) = CoordinateNormalizer.Normalize(new PlanePoint(1, 0), extent);

        Assert.Equal(0.3333, normalizedX);
    }

    [Fact]
    public void FlattensPointsIntoInterleavedPairs()
    {
        IReadOnlyList<double> flattened = CoordinateNormalizer.Flatten(
            [new PlanePoint(0, 0), new PlanePoint(200, 100)], WideExtent);

        Assert.Equal(new[] { 0.0, 0.5, 1.0, 0.0 }, flattened);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~ExtentRectangleTests|FullyQualifiedName~CoordinateNormalizerTests"
```

Expected: build error CS0246, `The type or namespace name 'ExtentRectangle' could not be found`.

- [ ] **Step 3: Write the rectangle**

`src/MetroDisplay.Gtfs.Static/Geometry/ExtentRectangle.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// An axis-aligned rectangle in Web Mercator plane units: the part of the plane the map shows.
/// </summary>
/// <param name="MinX">Western edge.</param>
/// <param name="MinY">Southern edge.</param>
/// <param name="MaxX">Eastern edge.</param>
/// <param name="MaxY">Northern edge.</param>
public readonly record struct ExtentRectangle(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double LongestSpan => Math.Max(Width, Height);

    /// <summary>
    /// Width over height. 1 for a rectangle with no height, so no consumer divides by zero.
    /// </summary>
    public double Aspect => Height == 0 ? 1.0 : Width / Height;

    /// <summary>
    /// The smallest rectangle containing every point.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static ExtentRectangle Enclosing(IEnumerable<PlanePoint> points)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool sawPoint = false;

        foreach (PlanePoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            sawPoint = true;
        }

        if (!sawPoint)
        {
            throw new ArgumentException("Cannot bound an empty set of points.", nameof(points));
        }

        return new ExtentRectangle(minX, minY, maxX, maxY);
    }
}
```

- [ ] **Step 4: Write the normalizer**

`src/MetroDisplay.Gtfs.Static/Geometry/CoordinateNormalizer.cs`:

```csharp
namespace MetroDisplay.Gtfs.Static.Geometry;

/// <summary>
/// Maps plane coordinates into the normalized space the renderer draws. Both axes are divided
/// by the same span, so a renderer that ignores aspect still draws correct proportions.
/// Y is flipped here, once, so no renderer can get it backwards.
/// </summary>
public static class CoordinateNormalizer
{
    /// <summary>
    /// Four places is about a fifth of a pixel at a 60 km extent on a 1920 px display:
    /// visually lossless, and roughly half the JSON of full precision.
    /// </summary>
    private const int Decimals = 4;

    /// <returns>The point in extent-relative units: the long axis spans [0, 1].</returns>
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

    /// <returns>Normalized points as the wire carries them: <c>[x0, y0, x1, y1, ...]</c>.</returns>
    public static IReadOnlyList<double> Flatten(IEnumerable<PlanePoint> points, ExtentRectangle extent)
    {
        var flattened = new List<double>();
        foreach (PlanePoint point in points)
        {
            (double normalizedX, double normalizedY) = Normalize(point, extent);
            flattened.Add(normalizedX);
            flattened.Add(normalizedY);
        }
        return flattened;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~ExtentRectangleTests|FullyQualifiedName~CoordinateNormalizerTests"
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: normalize the network to its own bounds

Both axes divide by the longest span so distortion is impossible,
Y flips to screen convention once, and values round to four
decimals. The configured extent replaces these bounds in slice 3.
```

---

### Task 5: Scene assembly

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneBuilderTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4; `CityConfig`, `NetworkScene`, `CityMetadata`,
  `ExtentInfo`, `LineScene`, `ShapeGeometry` (`MetroDisplay.Contracts`).
- Produces: `NetworkSceneBuilder.Build(byte[] zipBytes, CityConfig config) -> NetworkScene`,
  which throws `InvalidDataException` when there are no rail shapes;
  `NetworkSceneBuilder.FallbackColor` (`"#8E9BAD"`).

**Changed from Plan 1 Task 12:**
- It builds a `NetworkScene`, not a `NetworkArtifact`, and has no manifest, trip index,
  dedupe, clipping, simplification, stations or edge labels.
- `artifactVersion` is `<cityId>@` plus the first 8 hex digits of the zip's SHA-256, per spec §14.
- Shape points are now explicitly ordered by `shape_pt_sequence`.
- A line's name prefers `route_long_name`. Display abbreviations are slice 11's decision.

- [ ] **Step 1: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneBuilderTests.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class NetworkSceneBuilderTests
{
    private static CityConfig TestConfig() => new(
        Id: "test",
        Name: "TESTVILLE",
        Agency: "TT",
        Timezone: "America/New_York",
        StaticFeed: new FeedSource("https://example.test/gtfs.zip"),
        Realtime: new RealtimeSources(
            new FeedSource("https://example.test/vp"),
            new FeedSource("https://example.test/alerts")),
        RouteTypes: [0, 1],
        Extent: new ExtentConfig([42.34, -71.08], CoreRadiusKm: 22),
        Simplify: new SimplifyConfig(ToleranceM: 25),
        DwellMs: 300000);

    private static NetworkScene Build(GtfsFixtureBuilder fixture) =>
        NetworkSceneBuilder.Build(fixture.Build(), TestConfig());

    private static IReadOnlyList<double> ShapePoints(NetworkScene scene, string shapeId) =>
        scene.Lines.SelectMany(line => line.Shapes).First(shape => shape.Id == shapeId).Points;

    [Fact]
    public void ProducesOneLinePerRailRouteAndNoBuses()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(new[] { "Green", "Red" }, scene.Lines.Select(line => line.Id));
    }

    [Fact]
    public void FitsTheNetworkToItsOwnBounds()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        List<double> values = scene.Lines.SelectMany(line => line.Shapes).SelectMany(shape => shape.Points).ToList();
        List<double> xValues = values.Where((_, index) => index % 2 == 0).ToList();
        List<double> yValues = values.Where((_, index) => index % 2 == 1).ToList();

        // The fixture network is wider than it is tall, so X spans all of [0, 1].
        Assert.Equal(0.0, xValues.Min());
        Assert.Equal(1.0, xValues.Max());
        Assert.Equal(0.0, yValues.Min());
        Assert.InRange(yValues.Max(), 0.0, 1.0);
    }

    [Fact]
    public void OrdersShapePointsBySequenceNotFileOrder()
    {
        NetworkScene inOrder = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());
        NetworkScene shuffled = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3600,-71.0700,3
            shape-green,42.3540,-71.0600,3
            shape-red,42.3200,-71.0900,1
            shape-red-rev,42.3200,-71.0900,3
            shape-green,42.3500,-71.1200,1
            shape-red,42.3400,-71.0800,2
            shape-red-rev,42.3600,-71.0700,1
            shape-green,42.3520,-71.0900,2
            shape-red-rev,42.3400,-71.0800,2
            """));

        Assert.Equal(ShapePoints(inOrder, "shape-red"), ShapePoints(shuffled, "shape-red"));
    }

    [Theory]
    [InlineData("DA291C", "#DA291C")]
    [InlineData("00843d", "#00843D")]
    [InlineData("#ED8B00", "#ED8B00")]
    [InlineData("", NetworkSceneBuilder.FallbackColor)]
    [InlineData("red", NetworkSceneBuilder.FallbackColor)]
    public void FormatsRouteColorAsHashAndSixHexDigits(string routeColor, string expectedColor)
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", $"""
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,{routeColor}
            """));

        Assert.Equal(expectedColor, scene.Lines.Single().Color);
    }

    [Fact]
    public void SkipsTripsWhoseShapeIsNotInShapesTxt()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("trips.txt", """
            route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
            Red,weekday,red-north-1,Alewife,0,shape-red
            Red,weekday,red-ghost-1,Nowhere,0,shape-not-published
            Green,weekday,green-west-1,Boston College,0,shape-green
            """));

        LineScene red = scene.Lines.Single(line => line.Id == "Red");
        Assert.Equal(new[] { "shape-red" }, red.Shapes.Select(shape => shape.Id));
    }

    [Fact]
    public void LeavesOutRailRoutesThatHaveNoTrips()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,DA291C
            Green,B,"Green Line, B Branch",0,00843D
            Orange,,Orange Line,1,ED8B00
            """));

        Assert.Equal(new[] { "Green", "Red" }, scene.Lines.Select(line => line.Id));
    }

    [Fact]
    public void NamesLinesByLongNameThenShortName()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,RL,Red Line,1,DA291C
            Green,GL,,0,00843D
            """));

        Assert.Equal("Red Line", scene.Lines.Single(line => line.Id == "Red").Name);
        Assert.Equal("GL", scene.Lines.Single(line => line.Id == "Green").Name);
    }

    [Fact]
    public void MeasuresShapeLengthOnTheGround()
    {
        // 0.01 degrees along a meridian: pi * 6371008.8 / 180 / 100 = 1111.95 m.
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3000,-71.1000,1
            shape-red,42.3100,-71.1000,2
            """));

        ShapeGeometry shape = scene.Lines.Single(line => line.Id == "Red").Shapes.Single();
        Assert.Equal(1111.95, shape.LengthM, tolerance: 0.1);
    }

    [Fact]
    public void ReportsAspectAndSpanOfTheNetworkBounds()
    {
        // Bounds run 42.32..42.36 N and 71.12..71.06 W: 6679 x 6024 plane metres, and
        // the 6679 m long side is 4.94 km on the ground at the centre latitude 42.34.
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(1.1087, scene.Extent.Aspect);
        Assert.Equal(4.94, scene.Extent.SpanKm);
        Assert.Equal(22, scene.Extent.CoreRadiusKm);
    }

    [Fact]
    public void DerivesTheVersionFromTheZipContents()
    {
        byte[] original = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();
        byte[] recoloured = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Red,,Red Line,1,DA291D
            """).Build();

        string originalVersion = NetworkSceneBuilder.Build(original, TestConfig()).ArtifactVersion;

        Assert.Matches("^test@[0-9a-f]{8}$", originalVersion);
        Assert.Equal(originalVersion, NetworkSceneBuilder.Build(original, TestConfig()).ArtifactVersion);
        Assert.NotEqual(originalVersion, NetworkSceneBuilder.Build(recoloured, TestConfig()).ArtifactVersion);
    }

    [Fact]
    public void RefusesAFeedWithNoRailShapes()
    {
        GtfsFixtureBuilder busesOnly = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("routes.txt", """
            route_id,route_short_name,route_long_name,route_type,route_color
            Bus7,7,Bus Route 7,3,FFC72C
            """);

        var exception = Assert.Throws<InvalidDataException>(() => Build(busesOnly));

        Assert.Contains("no rail shapes", exception.Message);
    }
}
```

`DerivesTheVersionFromTheZipContents` reuses one `original` byte array on purpose. Zip
entries carry a timestamp, so two `Build()` calls a second apart produce different bytes.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~NetworkSceneBuilderTests"
```

Expected: build error CS0103, `The name 'NetworkSceneBuilder' does not exist in the current context`.

- [ ] **Step 3: Write the builder**

`src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`:

```csharp
using System.Security.Cryptography;
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Geometry;
using MetroDisplay.Gtfs.Static.Reading;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// Turns a GTFS zip and a city config into the scene the renderer draws. Pure over its
/// inputs: the same zip and config always produce the same scene, version included.
/// </summary>
/// <remarks>
/// Lines only, fitted to the network's own bounds. The configured extent, clipping, edge
/// labels, stations and simplification each arrive in a later slice (spec §14).
/// </remarks>
public static class NetworkSceneBuilder
{
    /// <summary>
    /// Drawn for a route whose agency publishes no usable <c>route_color</c>.
    /// </summary>
    public const string FallbackColor = "#8E9BAD";

    /// <param name="zipBytes">The static GTFS archive.</param>
    /// <param name="config">The city being built.</param>
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column.</exception>
    public static NetworkScene Build(byte[] zipBytes, CityConfig config)
    {
        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);
        RailSelection selection = RailRouteSelector.Select(archive, config.RouteTypes, config.RouteFilter);

        Dictionary<string, List<GeoPoint>> geoPointsByShapeId = GroupShapePoints(archive.ShapePoints, selection.ShapeIds);
        if (geoPointsByShapeId.Count == 0)
        {
            throw new InvalidDataException(
                $"Feed for '{config.Id}' has no rail shapes for route types [{string.Join(", ", config.RouteTypes)}].");
        }

        Dictionary<string, List<PlanePoint>> planePointsByShapeId = geoPointsByShapeId.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Select(MercatorProjector.Project).ToList(),
            StringComparer.Ordinal);

        ExtentRectangle bounds = ExtentRectangle.Enclosing(planePointsByShapeId.Values.SelectMany(points => points));

        List<double> latitudes = geoPointsByShapeId.Values.SelectMany(points => points).Select(point => point.Latitude).ToList();
        double centreLatitude = (latitudes.Min() + latitudes.Max()) / 2.0;

        var extent = new ExtentInfo(
            Aspect: Math.Round(bounds.Aspect, 4),
            CoreRadiusKm: config.Extent.CoreRadiusKm,
            SpanKm: Math.Round(MercatorProjector.PlaneMetresToGround(bounds.LongestSpan, centreLatitude) / 1000.0, 2));

        return new NetworkScene(
            ArtifactVersion: ContentVersion(config.Id, zipBytes),
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: extent,
            Lines: BuildLines(selection, geoPointsByShapeId, planePointsByShapeId, bounds),
            Stations: [],
            EdgeLabels: []);
    }

    /// <summary>
    /// Groups the wanted shapes' points, each shape in <c>shape_pt_sequence</c> order. Row
    /// order in <c>shapes.txt</c> is not guaranteed by GTFS.
    /// </summary>
    private static Dictionary<string, List<GeoPoint>> GroupShapePoints(IReadOnlyList<GtfsShapePoint> shapePoints, IReadOnlySet<string> wantedShapeIds)
        => shapePoints
            .Where(point => wantedShapeIds.Contains(point.ShapeId))
            .GroupBy(point => point.ShapeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(point => point.Sequence).Select(point => new GeoPoint(point.Latitude, point.Longitude)).ToList(),
                StringComparer.Ordinal);

    private static List<LineScene> BuildLines(
        RailSelection selection,
        Dictionary<string, List<GeoPoint>> geoPointsByShapeId,
        Dictionary<string, List<PlanePoint>> planePointsByShapeId,
        ExtentRectangle bounds)
    {
        // Trips whose shape is not in shapes.txt drop out here.
        ILookup<string, string> shapeIdsByRouteId = selection.Trips
            .Where(trip => geoPointsByShapeId.ContainsKey(trip.ShapeId))
            .ToLookup(trip => trip.RouteId, trip => trip.ShapeId, StringComparer.Ordinal);

        var lines = new List<LineScene>();
        foreach (GtfsRoute route in selection.Routes.OrderBy(route => route.RouteId, StringComparer.Ordinal))
        {
            List<ShapeGeometry> shapes = shapeIdsByRouteId[route.RouteId]
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(shapeId => new ShapeGeometry(
                    shapeId,
                    CoordinateNormalizer.Flatten(planePointsByShapeId[shapeId], bounds),
                    Math.Round(GroundDistance.PolylineLengthM(geoPointsByShapeId[shapeId]), 1)))
                .ToList();

            // A route listed in routes.txt with nothing to draw, such as a seasonal route, is
            // left off the map instead of being sent as an empty line.
            if (shapes.Count == 0)
            {
                continue;
            }

            lines.Add(new LineScene(route.RouteId, DisplayName(route), FormatColor(route.RouteColor), shapes));
        }
        return lines;
    }

    private static string DisplayName(GtfsRoute route)
        => string.IsNullOrWhiteSpace(route.RouteLongName) ? route.RouteShortName : route.RouteLongName;

    /// <summary>
    /// GTFS specifies six hex digits without a leading <c>#</c>. Agencies vary, so case and a
    /// stray <c>#</c> are tolerated; anything else falls back to grey.
    /// </summary>
    private static string FormatColor(string routeColor)
    {
        string hex = routeColor.Trim().TrimStart('#');
        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? $"#{hex.ToUpperInvariant()}" : FallbackColor;
    }

    /// <summary>
    /// Derived from the zip so the same feed always yields the same version. The artifact
    /// store replaces this with a dated version in slice 14.
    /// </summary>
    private static string ContentVersion(string cityId, byte[] zipBytes)
        => $"{cityId}@{Convert.ToHexStringLower(SHA256.HashData(zipBytes))[..8]}";
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~NetworkSceneBuilderTests"
```

Expected: PASS, 15 tests (10 facts, plus 5 cases of the colour theory).

- [ ] **Step 5: Run the whole suite**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 50 tests.

- [ ] **Step 6: Hand off for commit**

```
feat: build the network scene from a GTFS zip

Lines only, fitted to the network's own bounds. Shape points are
ordered by sequence, colours normalized to #RRGGBB, and the version
is a hash of the zip so the same feed always yields the same one.
```

---

### Task 6: Server

**Files:**
- Create (template): `src/MetroDisplay.Server/` via `dotnet new web`
- Modify: `src/MetroDisplay.Server/Program.cs`
- Modify: `src/MetroDisplay.Server/appsettings.json`
- Modify: `src/MetroDisplay.Server/Properties/launchSettings.json`
- Create: `src/MetroDisplay.Server/ServerSettings.cs`
- Create: `src/MetroDisplay.Server/ConfigurationEnvironment.cs`
- Create: `src/MetroDisplay.Server/Feeds/StaticFeedCache.cs`
- Create (template): `tests/MetroDisplay.Server.Tests/` via `dotnet new xunit`
- Create: `tests/MetroDisplay.Server.Tests/StubHttpHandler.cs`
- Create: `tests/MetroDisplay.Server.Tests/TemporaryDirectory.cs`
- Test: `tests/MetroDisplay.Server.Tests/StaticFeedCacheTests.cs`
- Test: `tests/MetroDisplay.Server.Tests/ConfigurationEnvironmentTests.cs`
- Test: `tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`
- Modify: `MetroDisplay.slnx`, `.gitignore`

**Interfaces:**
- Consumes: `CityConfigLoader.Load` (done), `NetworkSceneBuilder.Build` (Task 5),
  `JsonDefaults.Options`, `FeedSource`; `GtfsFixtureBuilder` (Task 1, linked into the test project).
- Produces: `GET /api/network` returning the `NetworkScene` as JSON on `http://localhost:5180`;
  `StaticFeedCache(HttpClient httpClient, string cacheDirectory).GetAsync(string cityId, FeedSource source, CancellationToken cancellationToken = default) -> Task<byte[]>`;
  `ConfigurationEnvironment.From(IConfiguration) -> IReadOnlyDictionary<string, string>`;
  `ServerSettings(string CityId, string CitiesDirectory, string FeedCacheDirectory)`.

Facts this task relies on, all checked against SDK 10.0.401 on 2026-09-24:
- `dotnet run --project src/MetroDisplay.Server` uses the project directory as the content
  root, so relative paths in `appsettings.json` resolve from `src/MetroDisplay.Server`.
- `WebApplicationFactory` settings are visible in `app.Configuration` after `Build()`, and
  code between `Build()` and `Run()` runs under the factory. That is why settings are read
  after `Build()`.
- A startup exception reaches the test unwrapped, with its original message.
- .NET 10 generates a public `Program` class, so `WebApplicationFactory<Program>` needs no
  `public partial class Program;` declaration.

- [ ] **Step 1: Scaffold the Server project**

```bash
dotnet new web -o src/MetroDisplay.Server
dotnet sln MetroDisplay.slnx add src/MetroDisplay.Server/MetroDisplay.Server.csproj
dotnet add src/MetroDisplay.Server reference src/MetroDisplay.Contracts/MetroDisplay.Contracts.csproj src/MetroDisplay.Gtfs.Static/MetroDisplay.Gtfs.Static.csproj
```

The template creates `Program.cs`, `appsettings.json`, `appsettings.Development.json` and
`Properties/launchSettings.json`. Steps 9–10 replace three of them.

- [ ] **Step 2: Scaffold the test project**

```bash
dotnet new xunit -o tests/MetroDisplay.Server.Tests
rm tests/MetroDisplay.Server.Tests/UnitTest1.cs
dotnet sln MetroDisplay.slnx add tests/MetroDisplay.Server.Tests/MetroDisplay.Server.Tests.csproj
dotnet add tests/MetroDisplay.Server.Tests reference src/MetroDisplay.Server/MetroDisplay.Server.csproj
dotnet add tests/MetroDisplay.Server.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.12
```

`UnitTest1.cs` is an empty test that always passes. Then link the fixture builder into
`tests/MetroDisplay.Server.Tests/MetroDisplay.Server.Tests.csproj` rather than copying it,
so both test projects build zips the same way:

```xml
  <ItemGroup>
    <Compile Include="..\MetroDisplay.Gtfs.Static.Tests\GtfsFixtureBuilder.cs" Link="GtfsFixtureBuilder.cs" />
  </ItemGroup>
```

- [ ] **Step 3: Write the test helpers**

`tests/MetroDisplay.Server.Tests/StubHttpHandler.cs`:

```csharp
namespace MetroDisplay.Server.Tests;

/// <summary>
/// Answers every request from a function and records what was sent.
/// </summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
```

`tests/MetroDisplay.Server.Tests/TemporaryDirectory.cs`:

```csharp
namespace MetroDisplay.Server.Tests;

/// <summary>
/// A fresh directory under the system temp folder, deleted on dispose.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string FullPath { get; } = Path.Combine(Path.GetTempPath(), "metrodisplay-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(FullPath);

    public void Dispose() => Directory.Delete(FullPath, recursive: true);
}
```

- [ ] **Step 4: Write the failing feed cache tests**

`tests/MetroDisplay.Server.Tests/StaticFeedCacheTests.cs`:

```csharp
using System.Net;
using MetroDisplay.Contracts;
using MetroDisplay.Server.Feeds;
using Xunit;

namespace MetroDisplay.Server.Tests;

public class StaticFeedCacheTests
{
    private static readonly FeedSource Source = new("https://example.test/gtfs.zip");

    /// <summary>The cache treats the feed as opaque bytes, so these need not be a real zip.</summary>
    private static readonly byte[] FeedBytes = [0x50, 0x4B, 0x03, 0x04, 0x2A];

    private static StubHttpHandler Serving(byte[] body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    [Fact]
    public async Task DownloadsAndKeepsTheFeedWhenNothingIsCached()
    {
        using var workspace = new TemporaryDirectory();
        string cacheDirectory = Path.Combine(workspace.FullPath, "gtfs");
        var cache = new StaticFeedCache(new HttpClient(Serving(FeedBytes)), cacheDirectory);

        byte[] result = await cache.GetAsync("test", Source);

        Assert.Equal(FeedBytes, result);
        Assert.Equal(FeedBytes, await File.ReadAllBytesAsync(Path.Combine(cacheDirectory, "test.zip")));
    }

    [Fact]
    public async Task ReusesTheCachedFeedWithoutDownloading()
    {
        using var workspace = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(workspace.FullPath, "test.zip"), FeedBytes);
        StubHttpHandler handler = Serving([]);
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);

        byte[] result = await cache.GetAsync("test", Source);

        Assert.Equal(FeedBytes, result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendsTheConfiguredHeaders()
    {
        using var workspace = new TemporaryDirectory();
        StubHttpHandler handler = Serving(FeedBytes);
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);
        var keyedSource = Source with { Headers = new Dictionary<string, string> { ["x-api-key"] = "secret-value" } };

        await cache.GetAsync("test", keyedSource);

        Assert.Equal("secret-value", handler.Requests.Single().Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task CachesNothingWhenTheDownloadFails()
    {
        using var workspace = new TemporaryDirectory();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cache = new StaticFeedCache(new HttpClient(handler), workspace.FullPath);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("test", Source));

        Assert.Contains(Source.Url, exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.FullPath));
    }
}
```

- [ ] **Step 5: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~StaticFeedCacheTests"
```

Expected: build error CS0234, `The type or namespace name 'Feeds' does not exist in the namespace 'MetroDisplay.Server'`.

- [ ] **Step 6: Write the feed cache**

`src/MetroDisplay.Server/Feeds/StaticFeedCache.cs`:

```csharp
using MetroDisplay.Contracts;

namespace MetroDisplay.Server.Feeds;

/// <summary>
/// Downloads a city's static GTFS zip once and keeps it on disk, so a restart does not fetch
/// 25 MB again. There is no refresh: delete the cached file to pick up a newer feed.
/// Conditional GET and a daily re-check arrive in slice 14.
/// </summary>
/// <param name="httpClient">Client used for the download.</param>
/// <param name="cacheDirectory">Where zips are kept, one per city as <c>&lt;cityId&gt;.zip</c>. Created on first download.</param>
public sealed class StaticFeedCache(HttpClient httpClient, string cacheDirectory)
{
    /// <exception cref="HttpRequestException">The download failed. The message names the city and URL, and nothing is cached.</exception>
    public async Task<byte[]> GetAsync(string cityId, FeedSource source, CancellationToken cancellationToken = default)
    {
        string cachePath = Path.Combine(cacheDirectory, $"{cityId}.zip");
        if (File.Exists(cachePath))
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        if (source.Headers is not null)
        {
            foreach ((string name, string value) in source.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Static feed for '{cityId}' returned HTTP {(int)response.StatusCode} from {source.Url}.",
                inner: null,
                response.StatusCode);
        }

        byte[] zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllBytesAsync(cachePath, zipBytes, cancellationToken);
        return zipBytes;
    }
}
```

- [ ] **Step 7: Run the tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~StaticFeedCacheTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 8: Write the failing configuration and endpoint tests**

`tests/MetroDisplay.Server.Tests/ConfigurationEnvironmentTests.cs`:

```csharp
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
```

`tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`:

```csharp
using System.Text.Json;
using MetroDisplay.Gtfs.Static.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MetroDisplay.Server.Tests;

public sealed class NetworkEndpointTests : IDisposable
{
    private const string TestCityJson = """
        {
          "id": "test",
          "name": "TESTVILLE",
          "agency": "TT",
          "timezone": "America/New_York",
          "staticFeed": { "url": "https://example.test/gtfs.zip" },
          "realtime": {
            "vehiclePositions": { "url": "https://example.test/vp" },
            "alerts": { "url": "https://example.test/alerts" }
          },
          "routeTypes": [0, 1],
          "extent": { "core": [42.34, -71.08], "coreRadiusKm": 22 },
          "simplify": { "toleranceM": 25 },
          "dwellMs": 300000
        }
        """;

    private readonly TemporaryDirectory workspace = new();

    /// <summary>
    /// A Server pointed at a temp cities directory and a cache that already holds the fixture
    /// zip, so startup never touches the network.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory(string cityId)
    {
        string citiesDirectory = Path.Combine(workspace.FullPath, "cities");
        string cacheDirectory = Path.Combine(workspace.FullPath, "cache");
        Directory.CreateDirectory(citiesDirectory);
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(citiesDirectory, "test.json"), TestCityJson);
        File.WriteAllBytes(Path.Combine(cacheDirectory, "test.zip"), GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        return new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("MetroDisplay:CityId", cityId)
            .UseSetting("MetroDisplay:CitiesDirectory", citiesDirectory)
            .UseSetting("MetroDisplay:FeedCacheDirectory", cacheDirectory));
    }

    [Fact]
    public async Task ServesTheSceneAsCamelCaseJson()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/network");

        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        Assert.StartsWith("test@", root.GetProperty("artifactVersion").GetString());
        Assert.Equal(new[] { "Green", "Red" }, root.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("id").GetString()));
    }

    [Fact]
    public void RefusesToStartWithoutItsCityConfig()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("missing");

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("missing.json", exception.ToString());
    }

    public void Dispose() => workspace.Dispose();
}
```

- [ ] **Step 9: Run the tests and confirm they fail**

```bash
dotnet test tests/MetroDisplay.Server.Tests
```

Expected: build error CS0103, `The name 'ConfigurationEnvironment' does not exist in the current context`.

- [ ] **Step 10: Write the settings, configuration adapter and startup**

`src/MetroDisplay.Server/ServerSettings.cs`:

```csharp
namespace MetroDisplay.Server;

/// <summary>
/// The Server's own settings, bound from the <c>MetroDisplay</c> configuration section.
/// </summary>
/// <param name="CityId">The city to show. Until rotation arrives (slice 13), the Server shows one.</param>
/// <param name="CitiesDirectory">Directory holding the city configs. A relative path resolves against the content root.</param>
/// <param name="FeedCacheDirectory">Where downloaded static feeds are kept between runs. A relative path resolves against the content root.</param>
public sealed record ServerSettings(string CityId, string CitiesDirectory, string FeedCacheDirectory);
```

`src/MetroDisplay.Server/ConfigurationEnvironment.cs`:

```csharp
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
```

Replace `src/MetroDisplay.Server/Program.cs`:

```csharp
using MetroDisplay.Contracts;
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using MetroDisplay.Server;
using MetroDisplay.Server.Feeds;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

// Read after Build so a test host's settings apply.
ServerSettings settings = app.Configuration.GetRequiredSection("MetroDisplay").Get<ServerSettings>()
    ?? throw new InvalidOperationException("The MetroDisplay configuration section is empty.");
string contentRoot = app.Environment.ContentRootPath;

// The scene is built once, before the Server listens. Any failure stops startup and names
// its cause; degrading gracefully is slice 14's job.
string configPath = Path.GetFullPath(Path.Combine(contentRoot, settings.CitiesDirectory, $"{settings.CityId}.json"));
CityConfig config = CityConfigLoader.Load(await File.ReadAllTextAsync(configPath), ConfigurationEnvironment.From(app.Configuration));

var feedCache = new StaticFeedCache(
    app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(),
    Path.GetFullPath(Path.Combine(contentRoot, settings.FeedCacheDirectory)));
byte[] zipBytes = await feedCache.GetAsync(config.Id, config.StaticFeed);
NetworkScene scene = NetworkSceneBuilder.Build(zipBytes, config);

app.Logger.LogInformation(
    "Built {ArtifactVersion}: {LineCount} lines, {ShapeCount} shapes",
    scene.ArtifactVersion,
    scene.Lines.Count,
    scene.Lines.Sum(line => line.Shapes.Count));

app.MapGet("/api/network", () => TypedResults.Json(scene, JsonDefaults.Options));

app.Run();
```

Replace `src/MetroDisplay.Server/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "MetroDisplay": {
    "CityId": "mbta",
    "CitiesDirectory": "../../cities",
    "FeedCacheDirectory": "../../.cache/gtfs"
  }
}
```

Replace `src/MetroDisplay.Server/Properties/launchSettings.json`. The fixed port is the one
`web/vite.config.ts` proxies to in Task 7:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5180",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Add one line to `.gitignore`, so the downloaded feed never lands in history:

```
.cache/
```

- [ ] **Step 11: Run the Server tests and confirm they pass**

```bash
dotnet test tests/MetroDisplay.Server.Tests
```

Expected: PASS, 7 tests.

- [ ] **Step 12: Run the whole suite**

```bash
dotnet test MetroDisplay.slnx
```

Expected: PASS, 57 tests (50 in `MetroDisplay.Gtfs.Static.Tests`, 7 in `MetroDisplay.Server.Tests`).

- [ ] **Step 13: Run it against the real feed**

```bash
dotnet run --project src/MetroDisplay.Server
```

Expected: the first run downloads about 25 MB into `.cache/gtfs/mbta.zip` and logs
`Built mbta@<8 hex>: 8 lines, 65 shapes`. The counts were measured from the feed on
2026-09-24; a newer feed may differ slightly. Then, from a second terminal:

```bash
curl -s http://localhost:5180/api/network | head -c 400
```

Expected: camelCase JSON that starts `{"artifactVersion":"mbta@`. Stop the Server and run it
again: it starts without downloading.

- [ ] **Step 14: Hand off for commit**

```
feat: serve the network scene from the server

The server downloads the static feed once, builds the scene before
it listens, and serves it at GET /api/network. Placeholders resolve
from IConfiguration, so user-secrets and app settings both work.
```

---

### Task 7: Renderer

**Files:**
- Create: `web/package.json`, `web/package-lock.json` (generated), `web/tsconfig.json`, `web/vite.config.ts`, `web/index.html`
- Create: `web/src/contract.ts`, `web/src/fit.ts`, `web/src/draw.ts`, `web/src/main.ts`
- Test: `web/src/fit.test.ts`
- Modify: `docs/specs/2026-09-22-metrodisplay-design.md` §4 "Renderer fit"

**Interfaces:**
- Consumes: `GET /api/network` on port 5180 (Task 6).
- Produces: `containFit(viewportWidth, viewportHeight, aspect, marginPx) -> Fit` and
  `toPixel(fit, normalizedX, normalizedY) -> [number, number]` in `web/src/fit.ts`. Slice 10's
  polyline walk builds on these.

Toolchain facts, checked on 2026-09-24: `create-vite` 9 pins TypeScript `~6.0.2` alongside
Vite `^8.3.0`, and Vitest 5.0.1 runs with both. The files below are written by hand rather
than scaffolded, so there is no template content to delete.

- [ ] **Step 1: Create the package and install**

`web/package.json`:

```json
{
  "name": "metrodisplay-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "test": "vitest run"
  },
  "devDependencies": {
    "typescript": "~6.0.2",
    "vite": "^8.3.0",
    "vitest": "^5.0.1"
  }
}
```

```bash
cd web && npm install
```

Expected: `node_modules/` (already ignored) and `package-lock.json`, which is committed.

- [ ] **Step 2: Add the TypeScript and Vite config**

`web/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "es2023",
    "module": "esnext",
    "lib": ["ES2023", "DOM"],
    "types": ["vite/client"],
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "verbatimModuleSyntax": true,
    "moduleDetection": "force",
    "noEmit": true,
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "erasableSyntaxOnly": true,
    "noFallthroughCasesInSwitch": true
  },
  "include": ["src"]
}
```

`web/vite.config.ts`:

```ts
import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    // The Server's port, from src/MetroDisplay.Server/Properties/launchSettings.json.
    proxy: { '/api': 'http://localhost:5180' },
  },
});
```

- [ ] **Step 3: Write the failing fit tests**

`web/src/fit.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { containFit, toPixel } from './fit';

describe('containFit', () => {
  it('fills the width with a wide map and centres it vertically', () => {
    // Aspect 2: the map is 1 unit wide and 0.5 tall, so width is the constraint.
    expect(containFit(1000, 1000, 2, 0)).toEqual({ scale: 1000, originX: 0, originY: 250 });
  });

  it('fills the height with a tall map and centres it horizontally', () => {
    // Aspect 0.5: the map is 0.5 units wide and 1 tall, so height is the constraint.
    expect(containFit(1000, 500, 0.5, 0)).toEqual({ scale: 500, originX: 375, originY: 0 });
  });

  it('keeps the margin clear on the constraining axis', () => {
    expect(containFit(1000, 1000, 1, 50)).toEqual({ scale: 900, originX: 50, originY: 50 });
  });

  it('returns a zero scale for a container that has not been laid out', () => {
    expect(containFit(0, 0, 1.2, 24)).toEqual({ scale: 0, originX: 0, originY: 0 });
  });
});

describe('toPixel', () => {
  it('maps normalized coordinates through the fit', () => {
    expect(toPixel({ scale: 900, originX: 50, originY: 100 }, 0.5, 0.25)).toEqual([500, 325]);
  });
});
```

The first test also catches a defect in spec §4. The spec's formula divides by
`Math.max(1, aspect)` and would give a scale of 500 here, drawing a wide map at half size.
Step 7 corrects the spec.

- [ ] **Step 4: Run the tests and confirm they fail**

```bash
cd web && npm test
```

Expected: FAIL, `Failed to resolve import "./fit" from "src/fit.test.ts"`.

- [ ] **Step 5: Write the fit math**

`web/src/fit.ts`:

```ts
/** Where the normalized scene lands on screen: pixel = origin + normalized * scale. */
export interface Fit {
  scale: number;
  originX: number;
  originY: number;
}

/**
 * Contain-fit of the normalized scene into a viewport, centred and inset by a margin.
 * The scene's long axis spans [0, 1] and its short axis [0, shorter / longer], so the map
 * measures 1 by 1/aspect units when wide and aspect by 1 when tall.
 */
export function containFit(viewportWidth: number, viewportHeight: number, aspect: number, marginPx: number): Fit {
  const mapWidthUnits = Math.min(1, aspect);
  const mapHeightUnits = Math.min(1, 1 / aspect);
  const availableWidth = Math.max(0, viewportWidth - 2 * marginPx);
  const availableHeight = Math.max(0, viewportHeight - 2 * marginPx);
  const scale = Math.min(availableWidth / mapWidthUnits, availableHeight / mapHeightUnits);
  return {
    scale,
    originX: (viewportWidth - mapWidthUnits * scale) / 2,
    originY: (viewportHeight - mapHeightUnits * scale) / 2,
  };
}

export function toPixel(fit: Fit, normalizedX: number, normalizedY: number): [number, number] {
  return [fit.originX + normalizedX * fit.scale, fit.originY + normalizedY * fit.scale];
}
```

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
cd web && npm test
```

Expected: PASS, 5 tests.

- [ ] **Step 7: Correct the fit formula in spec §4**

In `docs/specs/2026-09-22-metrodisplay-design.md`, under "Renderer fit", replace the block

```ts
const scale = Math.min(
  viewportWidth  / Math.max(1, aspect),
  viewportHeight / Math.max(1, 1 / aspect),
);
```

with

```ts
// The map measures min(1, aspect) by min(1, 1 / aspect) normalized units.
const scale = Math.min(
  viewportWidth  / Math.min(1, aspect),
  viewportHeight / Math.min(1, 1 / aspect),
);
```

Check: a wide map with aspect 2 in a 1000 × 1000 viewport. It is 1 unit wide, so the
scale should be 1000. The old formula gives `min(1000 / 2, 1000 / 1) = 500`. The new one
gives `min(1000 / 1, 1000 / 0.5) = 1000`.

- [ ] **Step 8: Write the page, the contract types, drawing and startup**

`web/index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>MetroDisplay</title>
    <style>
      html, body { margin: 0; height: 100%; background: #07090c; }
      #map { position: fixed; inset: 0; }
      #map canvas { display: block; width: 100%; height: 100%; }
      #status { position: fixed; left: 16px; bottom: 16px; color: #8e9bad; font: 13px ui-monospace, monospace; }
    </style>
  </head>
  <body>
    <div id="map"><canvas></canvas></div>
    <div id="status"></div>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
```

`#map` is sized by the viewport (`position: fixed; inset: 0`), not by its children, so
`ResizeObserver` measures a real size (spec §10).

`web/src/contract.ts`:

```ts
// Mirrors the parts of MetroDisplay.Contracts the renderer reads. Written by hand for now;
// slice 2 replaces this file with types generated from the C# records.

export interface NetworkScene {
  artifactVersion: string;
  extent: ExtentInfo;
  lines: LineScene[];
}

export interface ExtentInfo {
  aspect: number;
  coreRadiusKm: number;
  spanKm: number;
}

export interface LineScene {
  id: string;
  name: string;
  color: string;
  shapes: ShapeGeometry[];
}

export interface ShapeGeometry {
  id: string;
  points: number[];
  lengthM: number;
}
```

`web/src/draw.ts`:

```ts
import type { NetworkScene } from './contract';
import { toPixel, type Fit } from './fit';

const LINE_WIDTH_PX = 2;

/** Strokes every shape of every line as a polyline in its line's colour. */
export function drawNetwork(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit): void {
  context.lineWidth = LINE_WIDTH_PX;
  context.lineJoin = 'round';
  context.lineCap = 'round';

  for (const line of scene.lines) {
    context.strokeStyle = line.color;
    for (const shape of line.shapes) {
      const points = shape.points;
      if (points.length < 4) {
        continue;
      }
      context.beginPath();
      const [startX, startY] = toPixel(fit, points[0], points[1]);
      context.moveTo(startX, startY);
      for (let index = 2; index < points.length; index += 2) {
        const [pixelX, pixelY] = toPixel(fit, points[index], points[index + 1]);
        context.lineTo(pixelX, pixelY);
      }
      context.stroke();
    }
  }
}
```

`web/src/main.ts`:

```ts
import type { NetworkScene } from './contract';
import { drawNetwork } from './draw';
import { containFit } from './fit';

const MARGIN_PX = 32;
const BACKGROUND = '#07090c';

function requireElement<TElement extends Element>(selector: string): TElement {
  const element = document.querySelector<TElement>(selector);
  if (!element) {
    throw new Error(`index.html has no element matching ${selector}.`);
  }
  return element;
}

const container = requireElement<HTMLDivElement>('#map');
const canvas = requireElement<HTMLCanvasElement>('#map canvas');
const statusLine = requireElement<HTMLDivElement>('#status');
const context = canvas.getContext('2d');
if (!context) {
  throw new Error('Canvas 2D is not available.');
}
const drawing: CanvasRenderingContext2D = context;

function render(scene: NetworkScene, widthPx: number, heightPx: number): void {
  const pixelRatio = window.devicePixelRatio || 1;
  canvas.width = Math.round(widthPx * pixelRatio);
  canvas.height = Math.round(heightPx * pixelRatio);
  // Draw in CSS pixels; the transform maps them onto device pixels so lines stay crisp.
  drawing.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  drawing.fillStyle = BACKGROUND;
  drawing.fillRect(0, 0, widthPx, heightPx);

  // Not laid out yet. ResizeObserver calls again once the container has a size.
  if (widthPx === 0 || heightPx === 0) {
    return;
  }
  drawNetwork(drawing, scene, containFit(widthPx, heightPx, scene.extent.aspect, MARGIN_PX));
}

async function loadScene(): Promise<NetworkScene> {
  const response = await fetch('/api/network');
  if (!response.ok) {
    throw new Error(`GET /api/network returned HTTP ${response.status}.`);
  }
  return (await response.json()) as NetworkScene;
}

loadScene()
  .then((scene) => {
    statusLine.textContent = `${scene.lines.length} lines · ${scene.artifactVersion}`;
    new ResizeObserver((entries) => {
      const size = entries[0].contentRect;
      render(scene, size.width, size.height);
    }).observe(container);
  })
  .catch((error: unknown) => {
    statusLine.textContent = `Network unavailable: ${error instanceof Error ? error.message : String(error)}`;
  });
```

The status line is a stand-in for the bottom rail, which arrives in slice 11. For now it
confirms which scene is on screen and reports a failed load.

- [ ] **Step 9: Type-check and build**

```bash
cd web && npm run build
```

Expected: `tsc` reports nothing, and Vite writes `web/dist/` (already ignored).

- [ ] **Step 10: Look at it**

In one terminal:

```bash
dotnet run --project src/MetroDisplay.Server
```

In another:

```bash
cd web && npm run dev
```

Open `http://localhost:5173`. Expected:
- Eight routes on a near-black background: Red with its Ashmont and Braintree branches plus
  Mattapan in the same red; Orange running north–south; Blue running north-east to
  Wonderland; and Green's four branches fanning west.
- The status line reads `8 lines · mbta@<8 hex>`.
- Resize the window narrow and then wide. The network keeps its proportions, stays centred,
  and never touches the edges.
- Stop the Server and reload. The page stays dark, and the status line reads
  `Network unavailable: GET /api/network returned HTTP <status>.`

- [ ] **Step 11: Hand off for commit**

```
feat: draw the rail network in the browser

Vite and TypeScript renderer that fetches the scene, contain-fits
it to a canvas sized by ResizeObserver, and strokes each shape in
its line colour. Also fixes spec §4's fit formula, which divided
by max(1, aspect) and drew wide maps at a fraction of their size.
```

---

## Done when

- `dotnet test MetroDisplay.slnx` passes with 57 tests.
- `npm test` in `web/` passes with 5 tests, and `npm run build` succeeds.
- With the Server and `npm run dev` both running, `http://localhost:5173` shows Boston's
  eight rail routes in their colours, correctly proportioned, centred with a margin, and
  redrawn on resize.

## Carried into later slices

- **Line names** use `route_long_name` ("Red Line", "Green Line B"). The short labels in
  §10's rail (`RED`, `GRN`) are slice 11's decision.
- **`extent.coreRadiusKm`** repeats the config value even though no clip applies yet.
  Slice 3 makes it true.
- **Shapes are not deduplicated.** MBTA ships 65 shapes, of which 47 are geometrically
  unique. The overdraw is invisible, and slice 7 removes it.
- **The feed cache never refreshes.** Delete `.cache/gtfs/mbta.zip` to pick up a new feed.
  Slice 14 adds conditional GET.
- **The Server does not serve the built renderer.** Development goes through the Vite proxy.
  Serving `web/dist` belongs with deployment, in slice 14 at the latest.
- **User-secrets are not set up.** MBTA needs no key. Run
  `dotnet user-secrets init --project src/MetroDisplay.Server` when a keyed city arrives;
  `ConfigurationEnvironment` already picks the values up.
- **Douglas–Peucker tolerance units.** The ruling from the superseded plan stands for
  slice 7: convert `toleranceM` from ground metres to plane units before simplifying, and
  name the parameter `toleranceInPlaneUnits`.
