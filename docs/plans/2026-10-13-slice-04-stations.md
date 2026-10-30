# Slice 4: Stations Implementation Plan

**Goal:** Draw a marker at every rail station, and a name beside each interchange and terminus
that has room for one.

**Architecture:** `Gtfs.Static` reads `stops.txt` whole and streams `stop_times.txt`, keeping
only the rows of rail trips. A pure `StationBuilder` merges platforms into stations and ranks
each one. `NetworkSceneBuilder` projects the stations, drops those beyond the extent and
normalizes them into `StationMarker`s, which the contract already carries. The renderer draws
the markers and places the names with a small greedy label placer.

**Tech Stack:** .NET 10, CsvHelper 33.1.0, xUnit 2.9.3; TypeScript 6, Vite 8, Vitest 5, canvas2d.

**Spec:** `docs/specs/2026-09-22-metrodisplay-design.md`, §14 "Stations (slice 4)", and §6
stage 2.

**Measured before writing** (feed cached 2026-10-08, prototype in a scratch app):

- Boston's `stop_times.txt` is 147 MB unpacked and 3,154,923 rows. Reading it in .NET and
  keeping the 362,561 rows of rail trips takes 0.8 s. `stops.txt` (10,298 rows) takes 23 ms and
  building the stations under 0.1 s.
- Subway and light rail give 125 stations: 6 interchanges, 19 termini, 100 others. No two
  share a name.
- Riverside's own position lies 26 m west of the last point of its track, and so 26 m outside
  the frame the rail sets. A plain "inside the extent" test would drop a named terminus. Task 3
  moves a station that close onto the edge instead.
- The C# of Tasks 1 to 3 was applied to a copy of the repository and run: 183 tests pass, and
  the real feed gives 125 stations with Riverside at `x = 0`, in about 2 s for the whole scene.
  With commuter rail it gives 188.
- The renderer code in Tasks 4 and 5 was run against those 125 stations at 1920 by 1080.
  Haymarket, Park Street and Downtown Crossing get their names; State, Government Center and
  North Station have no room and show a ring only. Kenmore's name is also dropped, crowded out
  by the dots either side of it.

## Global Constraints

- Target framework `net10.0`, SDK 10.0.401.
- **The developer commits.** Every task ends with a hand-off step that supplies a commit
  message. No step runs `git add`, `git commit`, `git mv`, or any other git write.
- Commit messages: Conventional Commits prefix, imperative subject of 50 characters or
  fewer, body only when the why is not obvious, no trailers of any kind.
- **Naming:** readable words only. No single letters and no two- or three-character stubs,
  including locals, lambda parameters and generics. `x` and `y` only as members of a
  coordinate type.
- **Unit suffixes** on every unit-bearing name: `widthPx`, `radiusKm`.
- **Change files with an editor, never with an in-place stream editor** such as `sed -i`. It
  rewrites line endings, and in slice 3 that changed a raw string and cost a 13 MB download.
- Wire names match the C# records in `MetroDisplay.Contracts`. This slice adds no field.
- Public C# types carry `<summary>`/`<param>` doc comments in the style of `CityConfig.cs`.
- Comments the developer wrote stay wherever their line stays.
- TDD: write the failing test, watch it fail, write the minimal code, watch it pass.
- Builds finish with zero warnings. `npx tsc` in `web/` reports nothing.

## Review Focus

The five inputs most likely to bite that the spec implies but does not spell out. Each has a
pinned test in the task that owns the code.

1. **`stop_times.txt` rows in any order.** GTFS does not promise a trip's rows are sorted. A
   terminus found from "first row seen" would name the wrong station.
   → `FindsTheEndsOfATripWhateverOrderItsRowsAreIn` (Task 2)
2. **A stop row with no position.** Entrances and generic nodes in `stops.txt` leave
   `stop_lat` and `stop_lon` empty. Reading them as numbers must not fail the whole feed, and a
   station without a position cannot be drawn.
   → `ReadsAStopWithNoPosition` (Task 1), `LeavesOutAStationWithNoPosition` (Task 2)
3. **Stop times that point nowhere.** A row naming a stop missing from `stops.txt`, or a
   platform whose `parent_station` is missing, must not stop startup.
   → `SkipsAStopTimeWhoseStopIsNotInStopsTxt`, `TreatsAStopWithAnUnknownParentAsItsOwnStation`
   (Task 2)
4. **A bus that calls at a rail platform.** Bus rows are most of the file. One that stops at a
   rail platform must not turn that station into a terminus or an interchange.
   → `ReadsOnlyTheStopTimesOfWantedTrips` (Task 1), `IgnoresStopTimesOfTripsOutsideTheSelection`
   (Task 2)
5. **A terminus a few metres past the frame.** The frame's edge is the last point of track,
   and a station's own position is the middle of the station, a little beyond it.
   → `MovesAStationJustPastTheEdgeOntoIt` (Task 3)

Also pinned, because the whole look depends on it: **more names than room.** Interchanges sit
closer together than their names are wide. The display must drop names, never overlap them,
and never lose a marker.
→ `keeps two names from overlapping`, `gives up on a name with nowhere to go` (Task 4),
`keeps the marker when the name has nowhere to go` (Task 5)

## File Structure

```
src/MetroDisplay.Gtfs.Static/
  Reading/GtfsRecords.cs            + GtfsStop, GtfsStopTime; GtfsArchive gains Stops
  Reading/GtfsArchiveReader.cs      + stops.txt in Read; + ReadStopTimes
  Pipeline/StationBuilder.cs        new: stops + stop times + rail selection -> ranked stations
  Pipeline/NetworkSceneBuilder.cs   builds StationMarkers; FormatColor becomes internal

src/MetroDisplay.Spatial/
  ExtentRectangle.cs                + Clamp
  MercatorProjector.cs              + GroundMetresToPlane

src/MetroDisplay.Contracts/
  NetworkScene.cs                   doc comments only: Stations, StationMarker.Rank

src/MetroDisplay.Server/
  Program.cs                        station count in the startup log line

web/src/
  labels.ts                         new: where names go
  stations.ts                       new: draws markers and names
  contract.ts                       + stations, StationMarker
  main.ts                           calls drawStations; station count in the status line

tests/MetroDisplay.Gtfs.Static.Tests/
  GtfsFixtureBuilder.cs             + stops.txt, stop_times.txt in the standard feed
  GtfsArchiveReaderTests.cs         + 8 tests
  StationBuilderTests.cs            new, 12 tests
  NetworkSceneBuilderTests.cs       + 4 tests
tests/MetroDisplay.Spatial.Tests/
  ExtentRectangleTests.cs           + 1 test
  MercatorProjectorTests.cs         + 1 test
tests/MetroDisplay.Server.Tests/
  NetworkEndpointTests.cs           + 1 test
web/src/
  labels.test.ts                    new, 14 tests
  stations.test.ts                  new, 6 tests
  draw.test.ts                      one helper line
```

Test counts. .NET starts at 156 (Spatial 46, Osm 40, Gtfs.Static 49, Server 21); the
renderer starts at 8.

| After task | Spatial | Osm | Gtfs.Static | Server | .NET total | Renderer |
|---|---|---|---|---|---|---|
| 1 | 46 | 40 | 57 | 21 | 164 | 8 |
| 2 | 46 | 40 | 69 | 21 | 176 | 8 |
| 3 | 48 | 40 | 73 | 22 | 183 | 8 |
| 4 | 48 | 40 | 73 | 22 | 183 | 22 |
| 5 | 48 | 40 | 73 | 22 | 183 | 28 |

---

### Task 1: Read stops and rail stop times

**Files:**
- Modify: `src/MetroDisplay.Gtfs.Static/Reading/GtfsRecords.cs`
- Modify: `src/MetroDisplay.Gtfs.Static/Reading/GtfsArchiveReader.cs`
- Modify: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsFixtureBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/GtfsArchiveReaderTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `GtfsStop` with `string StopId`, `string StopName`, `double? Latitude`,
    `double? Longitude`, `string ParentStation`.
  - `sealed record GtfsStopTime(string TripId, string StopId, int Sequence)`.
  - `GtfsArchive(Routes, Trips, ShapePoints, Stops)`: `Stops` is the new fourth member,
    `IReadOnlyList<GtfsStop>`.
  - `static IReadOnlyList<GtfsStopTime> GtfsArchiveReader.ReadStopTimes(byte[] zipBytes, IReadOnlySet<string> wantedTripIds)`.
    Rows of other trips are never kept. Throws `InvalidDataException` naming `stop_times.txt`
    when the file, or one of `trip_id`, `stop_id`, `stop_sequence`, is missing.
  - The standard fixture `GtfsFixtureBuilder.TwoRailLinesAndABus()` now carries `stops.txt`
    (14 rows) and `stop_times.txt` (17 rows).

`stop_times.txt` cannot be read with the other files. Which trips are rail is only known
after routes and trips are read and selected, so it gets its own pass, with the wanted trip
ids handed in. It is read field by field instead of into one object per row: nine rows in ten
are thrown away, and building an object for each would triple the time.

The fixture's stations, used by every later task:

| Station | Platforms | Position | Served by |
|---|---|---|---|
| `place-south` South End | `south-platform` | 42.3200, -71.0900 | Red, at one end |
| `place-mid` Midway | `mid-north`, `mid-south` | 42.3400, -71.0800 | Red, passing through |
| `place-cross` Crossing | `cross-red`, `cross-green` | 42.3530, -71.0735 | Red and Green |
| `place-north` North End | `north-platform` | 42.3600, -71.0700 | Red, at the other end |
| `west-stop` West Side | itself, no parent | 42.3500, -71.1200 | Green, at one end |
| `east-stop` East Side | itself, no parent | 42.3540, -71.0600 | Green, at the other end |

`bus-stop` is served only by the bus, and the bus also calls at `mid-north`. `entrance-node`
is a station entrance with no position.

- [x] **Step 1: Give the standard fixture its stops and stop times**

In `GtfsFixtureBuilder.TwoRailLinesAndABus()`, the `shapes.txt` file is the last in the
chain and ends `""");`. Change that `;` so the chain carries on, and add two files after it:

```csharp
            .WithFile("stops.txt", """
                stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station
                place-south,South End,42.3200,-71.0900,1,
                south-platform,South End,42.3201,-71.0901,0,place-south
                place-mid,Midway,42.3400,-71.0800,1,
                mid-north,Midway,42.3401,-71.0801,0,place-mid
                mid-south,Midway,42.3399,-71.0799,0,place-mid
                place-cross,Crossing,42.3530,-71.0735,1,
                cross-red,Crossing,42.3531,-71.0735,0,place-cross
                cross-green,Crossing,42.3529,-71.0735,0,place-cross
                place-north,North End,42.3600,-71.0700,1,
                north-platform,North End,42.3601,-71.0701,0,place-north
                west-stop,West Side,42.3500,-71.1200,0,
                east-stop,East Side,42.3540,-71.0600,0,
                bus-stop,Bus Shelter,42.3000,-71.2000,0,
                entrance-node,,,,3,place-mid
                """)
            .WithFile("stop_times.txt", """
                trip_id,arrival_time,departure_time,stop_id,stop_sequence
                red-north-1,08:00:00,08:00:00,south-platform,1
                red-north-1,08:05:00,08:05:00,mid-north,2
                red-north-1,08:10:00,08:10:00,cross-red,3
                red-north-1,08:15:00,08:15:00,north-platform,4
                red-north-2,09:00:00,09:00:00,south-platform,1
                red-north-2,09:05:00,09:05:00,mid-north,2
                red-north-2,09:10:00,09:10:00,cross-red,3
                red-north-2,09:15:00,09:15:00,north-platform,4
                red-south-1,08:30:00,08:30:00,north-platform,1
                red-south-1,08:35:00,08:35:00,cross-red,2
                red-south-1,08:40:00,08:40:00,mid-south,3
                red-south-1,08:45:00,08:45:00,south-platform,4
                green-west-1,08:00:00,08:00:00,east-stop,1
                green-west-1,08:06:00,08:06:00,cross-green,2
                green-west-1,08:12:00,08:12:00,west-stop,3
                bus-1,08:00:00,08:00:00,bus-stop,1
                bus-1,08:20:00,08:20:00,mid-north,2
                """);
```

Update the method's summary to say so:

```csharp
    /// <summary>
    /// A subway line running both directions, a light rail line, and a bus route the
    /// rail filter must drop. Green's long name contains a comma inside quotes. The two rail
    /// lines share one station, Crossing, and the bus calls at one of Red's platforms.
    /// </summary>
```

- [x] **Step 2: Write the failing tests**

Add to `GtfsArchiveReaderTests`:

```csharp
    [Fact]
    public void ReadsStops()
    {
        GtfsArchive archive = GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        Assert.Equal(14, archive.Stops.Count);
        GtfsStop platform = archive.Stops.Single(stop => stop.StopId == "mid-north");
        Assert.Equal("Midway", platform.StopName);
        Assert.Equal(42.3401, platform.Latitude);
        Assert.Equal(-71.0801, platform.Longitude);
        Assert.Equal("place-mid", platform.ParentStation);
    }

    [Fact]
    public void ReadsAStopWithNoPosition()
    {
        // Entrances and generic nodes carry no stop_lat or stop_lon. They must not fail the read.
        GtfsArchive archive = GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());

        GtfsStop entrance = archive.Stops.Single(stop => stop.StopId == "entrance-node");
        Assert.Null(entrance.Latitude);
        Assert.Null(entrance.Longitude);
        Assert.Equal("", entrance.StopName);
    }

    [Fact]
    public void NamesTheFileWhenStopsAreMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("stops.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(() => GtfsArchiveReader.Read(zipBytes));

        Assert.Contains("stops.txt", exception.Message);
    }

    [Fact]
    public void ReadsOnlyTheStopTimesOfWantedTrips()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();
        HashSet<string> wantedTripIds = ["red-north-1", "green-west-1"];

        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, wantedTripIds);

        Assert.Equal(7, stopTimes.Count);
        Assert.All(stopTimes, stopTime => Assert.Contains(stopTime.TripId, wantedTripIds));
        Assert.Contains(new GtfsStopTime("green-west-1", "cross-green", 2), stopTimes);
    }

    [Fact]
    public void MapsStopTimeColumnsByHeaderNameNotPosition()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                stop_sequence,pickup_type,stop_id,trip_id
                7,0,mid-north,red-north-1
                """)
            .Build();

        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" });

        Assert.Equal(new GtfsStopTime("red-north-1", "mid-north", 7), Assert.Single(stopTimes));
    }

    [Fact]
    public void NamesTheFileWhenStopTimesAreMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("stop_times.txt").Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
    }

    [Fact]
    public void NamesTheColumnWhenAStopTimesColumnIsMissing()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                trip_id,stop_id
                red-north-1,mid-north
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
        Assert.Contains("stop_sequence", exception.Message);
    }

    [Fact]
    public void NamesTheValueWhenAStopSequenceIsNotANumber()
    {
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                red-north-1,mid-north,second
                """)
            .Build();

        var exception = Assert.Throws<InvalidDataException>(
            () => GtfsArchiveReader.ReadStopTimes(zipBytes, new HashSet<string> { "red-north-1" }));

        Assert.Contains("stop_times.txt", exception.Message);
        Assert.Contains("'second'", exception.Message);
    }
```

- [x] **Step 3: Run to see them fail**

```bash
dotnet build tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: errors CS1061 (`GtfsArchive` has no `Stops`), CS0246 (`GtfsStop`, `GtfsStopTime`)
and CS0117 (`GtfsArchiveReader` has no `ReadStopTimes`).

- [x] **Step 4: Add the records**

In `GtfsRecords.cs`, replace the `GtfsArchive` record and its doc comment:

```csharp
/// <summary>
/// The parts of a GTFS feed this system reads whole. <c>stop_times.txt</c> is too large for
/// that and is read separately, see <see cref="GtfsArchiveReader.ReadStopTimes"/>.
/// </summary>
/// <param name="Routes">Every row of <c>routes.txt</c>, buses included; filtering happens later.</param>
/// <param name="Trips">Every row of <c>trips.txt</c>.</param>
/// <param name="ShapePoints">Every row of <c>shapes.txt</c>, in file order.</param>
/// <param name="Stops">Every row of <c>stops.txt</c>: stations, platforms, entrances and bus stops alike.</param>
public sealed record GtfsArchive(IReadOnlyList<GtfsRoute> Routes, IReadOnlyList<GtfsTrip> Trips, IReadOnlyList<GtfsShapePoint> ShapePoints, IReadOnlyList<GtfsStop> Stops);
```

At the end of the file add:

```csharp

/// <summary>
/// A single row of <c>stops.txt</c>. A row is a station, a platform inside one, a stop standing
/// alone, or an entrance or other node that has no position of its own.
/// </summary>
public sealed class GtfsStop
{
    [Name("stop_id")]
    public string StopId { get; set; } = "";

    [Name("stop_name")]
    [Optional]
    public string StopName { get; set; } = "";

    /// <summary>
    /// Null where the feed leaves it empty, as it may for entrances and generic nodes.
    /// </summary>
    [Name("stop_lat")]
    [Optional]
    public double? Latitude { get; set; }

    [Name("stop_lon")]
    [Optional]
    public double? Longitude { get; set; }

    /// <summary>
    /// The <c>stop_id</c> of the station this platform belongs to. Empty for a station and for
    /// a stop that stands alone.
    /// </summary>
    [Name("parent_station")]
    [Optional]
    public string ParentStation { get; set; } = "";
}

/// <summary>
/// One call a trip makes at a stop: the three columns of <c>stop_times.txt</c> this system uses.
/// </summary>
/// <param name="TripId">The trip making the call.</param>
/// <param name="StopId">The stop or platform called at.</param>
/// <param name="Sequence">Order of the call within its trip. Increases along the trip; the values need not be consecutive.</param>
public sealed record GtfsStopTime(string TripId, string StopId, int Sequence);
```

- [x] **Step 5: Read stops, and stream stop times**

In `GtfsArchiveReader.cs`:

Replace the class summary:

```csharp
/// <summary>
/// Reads the GTFS files this system needs out of a zip. <c>stop_times.txt</c>, the bulk of a
/// typical archive and nearly all of it buses, is streamed and filtered instead of loaded.
/// </summary>
```

Replace the summary on `Read`:

```csharp
    /// <summary>
    /// Reads routes, trips, shapes and stops. <c>shapes.txt</c> is optional in GTFS but
    /// required here: without it there is no track geometry to draw.
    /// </summary>
```

In `Read`, after the `shapePoints` line, read the stops and pass them on:

```csharp
                List<GtfsStop> stops = ReadRequired<GtfsStop>(archive, "stops.txt");
                return new GtfsArchive(routes, trips, shapePoints, stops);
```

Add this method after `Read`:

```csharp
    /// <summary>
    /// Reads the calls made by the wanted trips. Every other row is passed over without being
    /// kept, so memory holds the rail rows only.
    /// </summary>
    /// <param name="zipBytes">The GTFS archive as downloaded.</param>
    /// <param name="wantedTripIds">The trips whose calls to keep.</param>
    /// <returns>The kept rows, in file order. GTFS does not promise that order means anything.</returns>
    /// <exception cref="InvalidDataException">The file or a required column is missing, or a <c>stop_sequence</c> is not a whole number. The message names it.</exception>
    public static IReadOnlyList<GtfsStopTime> ReadStopTimes(byte[] zipBytes, IReadOnlySet<string> wantedTripIds)
    {
        const string entryName = "stop_times.txt";

        using (MemoryStream memStream = new MemoryStream(zipBytes))
        {
            using (ZipArchive archive = new ZipArchive(memStream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"GTFS archive has no {entryName}.");

                using (StreamReader reader = new StreamReader(entry.Open()))
                {
                    using (CsvReader csvReader = new CsvReader(reader, CsvSettings))
                    {
                        string[] header = csvReader.Read() && csvReader.ReadHeader() ? csvReader.HeaderRecord ?? [] : [];
                        string[] requiredColumns = ["trip_id", "stop_id", "stop_sequence"];
                        string[] missingColumns = requiredColumns.Where(column => !header.Contains(column, StringComparer.Ordinal)).ToArray();
                        if (missingColumns.Length > 0)
                        {
                            throw new InvalidDataException($"{entryName} is missing required column(s): {string.Join(", ", missingColumns)}.");
                        }

                        // Fields are fetched by position. Mapping each of millions of rows onto
                        // an object, to throw nine in ten away, costs several times as long.
                        int tripIdIndex = Array.IndexOf(header, "trip_id");
                        int stopIdIndex = Array.IndexOf(header, "stop_id");
                        int sequenceIndex = Array.IndexOf(header, "stop_sequence");

                        List<GtfsStopTime> stopTimes = new();
                        while (csvReader.Read())
                        {
                            string tripId = csvReader.GetField(tripIdIndex) ?? "";
                            if (!wantedTripIds.Contains(tripId))
                            {
                                continue;
                            }

                            string sequenceText = csvReader.GetField(sequenceIndex) ?? "";
                            if (!int.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence))
                            {
                                throw new InvalidDataException($"{entryName} has a stop_sequence that is not a whole number: '{sequenceText}' on trip '{tripId}'.");
                            }

                            stopTimes.Add(new GtfsStopTime(tripId, csvReader.GetField(stopIdIndex) ?? "", sequence));
                        }
                        return stopTimes;
                    }
                }
            }
        }
    }
```

- [x] **Step 6: Run to see them pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: 57 passed. The 49 that predate this task pass unchanged: none of them reads
stations yet, and a fixture that replaces `routes.txt`, `trips.txt` or `shapes.txt` keeps the
standard stops and stop times.

- [x] **Step 7: Run everything**

```bash
dotnet test MetroDisplay.slnx
```

Expected: 164 passed (Spatial 46, Osm 40, Gtfs.Static 57, Server 21), 0 warnings.

- [x] **Step 8: Hand off**

```
feat: read stops and rail stop times

stop_times.txt is streamed against the rail trip ids, so the bus
rows that make up most of it are never held in memory.
```

---

### Task 2: Build ranked stations

**Files:**
- Create: `src/MetroDisplay.Gtfs.Static/Pipeline/StationBuilder.cs`
- Modify: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs` (one access modifier)
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/StationBuilderTests.cs`

**Interfaces:**
- Consumes: `GtfsStop`, `GtfsStopTime`, `GtfsArchiveReader.ReadStopTimes` (Task 1);
  `RailSelection` with `Routes` and `Trips`; `GeoPoint(double Latitude, double Longitude)`.
- Produces:
  - `sealed record Station(string Id, string Name, GeoPoint Position, int Rank)`.
  - `StationBuilder.OtherRank` = 1, `StationBuilder.TerminusRank` = 2,
    `StationBuilder.InterchangeRank` = 3.
  - `static IReadOnlyList<Station> StationBuilder.Build(RailSelection selection, IReadOnlyList<GtfsStop> stops, IReadOnlyList<GtfsStopTime> stopTimes)`,
    ordered by station id.
  - `NetworkSceneBuilder.FormatColor` becomes `internal`.

The rules, from spec §14:

- A station is a stop's `parent_station`, or the stop itself where it has none.
- **Interchange (3):** served by routes of two or more different colours. The colour is the one
  the line is drawn in, so routes drawn alike count as one line.
- **Terminus (2):** some trip starts or ends there. A trip's start and end are its lowest and
  highest `stop_sequence`, not its first and last row.
- **Other (1):** everything else. A station that is both interchange and terminus is 3.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Gtfs.Static.Tests/StationBuilderTests.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Pipeline;
using MetroDisplay.Gtfs.Static.Reading;
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Gtfs.Static.Tests;

public class StationBuilderTests
{
    /// <summary>
    /// Runs a feed the way the pipeline does: read, select rail, read those trips' calls, build.
    /// </summary>
    private static IReadOnlyList<Station> Build(GtfsFixtureBuilder fixture)
    {
        byte[] zipBytes = fixture.Build();
        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);
        RailSelection selection = RailRouteSelector.Select(archive, [0, 1], filter: null);
        HashSet<string> tripIds = selection.Trips.Select(trip => trip.TripId).ToHashSet(StringComparer.Ordinal);
        return StationBuilder.Build(selection, archive.Stops, GtfsArchiveReader.ReadStopTimes(zipBytes, tripIds));
    }

    private static int RankOf(IReadOnlyList<Station> stations, string stationId) =>
        stations.Single(station => station.Id == stationId).Rank;

    [Fact]
    public void MergesPlatformsIntoTheirStation()
    {
        IReadOnlyList<Station> stations = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        // Ten rail platforms and stops become six stations, in id order. The bus stop is not one.
        Assert.Equal(
            new[] { "east-stop", "place-cross", "place-mid", "place-north", "place-south", "west-stop" },
            stations.Select(station => station.Id));
    }

    [Fact]
    public void TakesNameAndPositionFromTheStationNotItsPlatforms()
    {
        IReadOnlyList<Station> stations = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Station midway = stations.Single(station => station.Id == "place-mid");
        Assert.Equal("Midway", midway.Name);
        // The platforms sit at 42.3401 and 42.3399. The station's own row says 42.3400.
        Assert.Equal(new GeoPoint(42.34, -71.08), midway.Position);
    }

    [Fact]
    public void RanksAStationServedByTwoColoursAsAnInterchange()
    {
        IReadOnlyList<Station> stations = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(StationBuilder.InterchangeRank, RankOf(stations, "place-cross"));
    }

    [Fact]
    public void RanksAStationWhereATripStartsOrEndsAsATerminus()
    {
        IReadOnlyList<Station> stations = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "place-south"));
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "place-north"));
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "west-stop"));
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "east-stop"));
        // Every Red trip runs through Midway.
        Assert.Equal(StationBuilder.OtherRank, RankOf(stations, "place-mid"));
    }

    [Fact]
    public void DoesNotCountTwoRoutesOfOneColourAsAnInterchange()
    {
        // A second green route, its colour written in lower case, running through Midway.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("routes.txt", """
                route_id,route_short_name,route_long_name,route_type,route_color
                Green,B,Green Line B,0,00843D
                GreenC,C,Green Line C,0,00843d
                """)
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Green,weekday,green-west-1,West Side,0,shape-green
                GreenC,weekday,greenc-west-1,West Side,0,shape-green
                """)
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                green-west-1,east-stop,1
                green-west-1,mid-north,2
                green-west-1,west-stop,3
                greenc-west-1,east-stop,1
                greenc-west-1,mid-south,2
                greenc-west-1,west-stop,3
                """);

        Assert.Equal(StationBuilder.OtherRank, RankOf(Build(fixture), "place-mid"));
    }

    [Fact]
    public void CountsAShortTurnAsATerminus()
    {
        // One Red trip a day turns back at Midway. That is enough to name it.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Red,weekday,red-north-1,Alewife,0,shape-red
                Red,weekday,red-short-1,Midway,0,shape-red
                """)
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                red-north-1,south-platform,1
                red-north-1,mid-north,2
                red-north-1,north-platform,3
                red-short-1,south-platform,1
                red-short-1,mid-north,2
                """);

        Assert.Equal(StationBuilder.TerminusRank, RankOf(Build(fixture), "place-mid"));
    }

    [Fact]
    public void FindsTheEndsOfATripWhateverOrderItsRowsAreIn()
    {
        // Midway's row comes first and Crossing's last. Neither is where the trip starts or ends.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Red,weekday,red-north-1,Alewife,0,shape-red
                """)
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                red-north-1,mid-north,20
                red-north-1,north-platform,40
                red-north-1,south-platform,10
                red-north-1,cross-red,30
                """);

        IReadOnlyList<Station> stations = Build(fixture);

        Assert.Equal(StationBuilder.OtherRank, RankOf(stations, "place-mid"));
        Assert.Equal(StationBuilder.OtherRank, RankOf(stations, "place-cross"));
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "place-south"));
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "place-north"));
    }

    [Fact]
    public void IgnoresStopTimesOfTripsOutsideTheSelection()
    {
        // Handed every row in the file, bus rows included. The bus ends its trip at one of
        // Midway's platforms and has a colour of its own.
        byte[] zipBytes = GtfsFixtureBuilder.TwoRailLinesAndABus().Build();
        GtfsArchive archive = GtfsArchiveReader.Read(zipBytes);
        RailSelection selection = RailRouteSelector.Select(archive, [0, 1], filter: null);
        HashSet<string> everyTripId = archive.Trips.Select(trip => trip.TripId).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<Station> stations = StationBuilder.Build(selection, archive.Stops, GtfsArchiveReader.ReadStopTimes(zipBytes, everyTripId));

        Assert.DoesNotContain(stations, station => station.Id == "bus-stop");
        Assert.Equal(StationBuilder.OtherRank, RankOf(stations, "place-mid"));
    }

    [Fact]
    public void SkipsAStopTimeWhoseStopIsNotInStopsTxt()
    {
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Red,weekday,red-north-1,Alewife,0,shape-red
                """)
            .WithFile("stop_times.txt", """
                trip_id,stop_id,stop_sequence
                red-north-1,south-platform,1
                red-north-1,mid-north,2
                red-north-1,north-platform,3
                red-north-1,platform-nobody-listed,4
                """);

        IReadOnlyList<Station> stations = Build(fixture);

        Assert.Equal(new[] { "place-mid", "place-north", "place-south" }, stations.Select(station => station.Id));
        // The unknown stop is passed over, so the trip still ends at North End.
        Assert.Equal(StationBuilder.TerminusRank, RankOf(stations, "place-north"));
    }

    [Fact]
    public void LeavesOutAStationWithNoPosition()
    {
        // Midway's own row has lost its coordinates. There is nowhere to draw it.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stops.txt", """
                stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station
                place-south,South End,42.3200,-71.0900,1,
                south-platform,South End,42.3201,-71.0901,0,place-south
                place-mid,Midway,,,1,
                mid-north,Midway,42.3401,-71.0801,0,place-mid
                mid-south,Midway,42.3399,-71.0799,0,place-mid
                place-cross,Crossing,42.3530,-71.0735,1,
                cross-red,Crossing,42.3531,-71.0735,0,place-cross
                cross-green,Crossing,42.3529,-71.0735,0,place-cross
                place-north,North End,42.3600,-71.0700,1,
                north-platform,North End,42.3601,-71.0701,0,place-north
                west-stop,West Side,42.3500,-71.1200,0,
                east-stop,East Side,42.3540,-71.0600,0,
                """);

        IReadOnlyList<Station> stations = Build(fixture);

        Assert.DoesNotContain(stations, station => station.Id == "place-mid");
        Assert.Equal(5, stations.Count);
    }

    [Fact]
    public void TreatsAStopWithAnUnknownParentAsItsOwnStation()
    {
        // West Side names a parent that stops.txt does not list.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus()
            .WithFile("stops.txt", """
                stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station
                west-stop,West Side,42.3500,-71.1200,0,place-nobody-listed
                cross-green,Crossing,42.3529,-71.0735,0,
                east-stop,East Side,42.3540,-71.0600,0,
                """)
            .WithFile("trips.txt", """
                route_id,service_id,trip_id,trip_headsign,direction_id,shape_id
                Green,weekday,green-west-1,Boston College,0,shape-green
                """);

        IReadOnlyList<Station> stations = Build(fixture);

        Assert.Equal(new[] { "cross-green", "east-stop", "west-stop" }, stations.Select(station => station.Id));
    }

    [Fact]
    public void BuildsNothingFromNoStopTimes()
    {
        GtfsArchive archive = GtfsArchiveReader.Read(GtfsFixtureBuilder.TwoRailLinesAndABus().Build());
        RailSelection selection = RailRouteSelector.Select(archive, [0, 1], filter: null);

        Assert.Empty(StationBuilder.Build(selection, archive.Stops, []));
    }
}
```

- [x] **Step 2: Run to see them fail**

```bash
dotnet build tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: errors CS0246 (`Station`) and CS0103 (`StationBuilder`).

- [x] **Step 3: Let the station builder see how a line's colour is drawn**

In `NetworkSceneBuilder.cs`, change `FormatColor` from `private` to `internal`:

```csharp
    internal static string FormatColor(string routeColor)
```

Nothing else in that file changes in this task.

- [x] **Step 4: Write the station builder**

`src/MetroDisplay.Gtfs.Static/Pipeline/StationBuilder.cs`:

```csharp
using MetroDisplay.Gtfs.Static.Reading;
using MetroDisplay.Spatial;

namespace MetroDisplay.Gtfs.Static.Pipeline;

/// <summary>
/// A rail station: every platform that shares a parent, as one place.
/// </summary>
/// <param name="Id">The <c>stop_id</c> of the station's own row in <c>stops.txt</c>.</param>
/// <param name="Name">The station's <c>stop_name</c>.</param>
/// <param name="Position">Where the feed puts the station itself, not any one platform.</param>
/// <param name="Rank">How much the station matters to someone reading the map. See <see cref="StationBuilder"/>.</param>
public sealed record Station(string Id, string Name, GeoPoint Position, int Rank);

/// <summary>
/// Works out which stops are rail stations and how prominent each is. Pure over its inputs.
/// </summary>
public static class StationBuilder
{
    /// <summary>
    /// A station trains run through, on one line.
    /// </summary>
    public const int OtherRank = 1;

    /// <summary>
    /// A station where some trip starts or ends, a short turn included.
    /// </summary>
    public const int TerminusRank = 2;

    /// <summary>
    /// A station served by lines of two or more colours. Outranks a terminus.
    /// </summary>
    public const int InterchangeRank = 3;

    /// <param name="selection">The rail routes and trips being drawn.</param>
    /// <param name="stops">Every row of <c>stops.txt</c>.</param>
    /// <param name="stopTimes">Calls made by trips. Calls of trips outside <paramref name="selection"/> are ignored.</param>
    /// <returns>One station per parent, ordered by id. A station the feed gives no position is left out.</returns>
    public static IReadOnlyList<Station> Build(RailSelection selection, IReadOnlyList<GtfsStop> stops, IReadOnlyList<GtfsStopTime> stopTimes)
    {
        // A feed should not repeat a stop_id. If one does, the first row wins.
        Dictionary<string, GtfsStop> stopsById = new(StringComparer.Ordinal);
        foreach (GtfsStop stop in stops)
        {
            stopsById.TryAdd(stop.StopId, stop);
        }

        Dictionary<string, string> colorByTripId = ColorByTripId(selection);

        Dictionary<string, HashSet<string>> colorsByStationId = new(StringComparer.Ordinal);
        Dictionary<string, (int Sequence, string StationId)> firstCallByTripId = new(StringComparer.Ordinal);
        Dictionary<string, (int Sequence, string StationId)> lastCallByTripId = new(StringComparer.Ordinal);

        foreach (GtfsStopTime stopTime in stopTimes)
        {
            // A call by a trip that is not being drawn, or at a stop the feed does not list, says nothing about a station.
            if (!colorByTripId.TryGetValue(stopTime.TripId, out string? color) || !stopsById.TryGetValue(stopTime.StopId, out GtfsStop? stop))
            {
                continue;
            }

            // A platform belongs to its parent. A stop with no parent, or one the feed does not list, stands for itself.
            string stationId = stopsById.ContainsKey(stop.ParentStation) ? stop.ParentStation : stop.StopId;

            if (!colorsByStationId.TryGetValue(stationId, out HashSet<string>? colors))
            {
                colors = new HashSet<string>(StringComparer.Ordinal);
                colorsByStationId[stationId] = colors;
            }
            colors.Add(color);

            // Rows arrive in no promised order, so a trip's ends are its lowest and highest sequence.
            if (!firstCallByTripId.TryGetValue(stopTime.TripId, out (int Sequence, string StationId) firstCall) || stopTime.Sequence < firstCall.Sequence)
            {
                firstCallByTripId[stopTime.TripId] = (stopTime.Sequence, stationId);
            }
            if (!lastCallByTripId.TryGetValue(stopTime.TripId, out (int Sequence, string StationId) lastCall) || stopTime.Sequence > lastCall.Sequence)
            {
                lastCallByTripId[stopTime.TripId] = (stopTime.Sequence, stationId);
            }
        }

        HashSet<string> terminusIds = firstCallByTripId.Values
            .Concat(lastCallByTripId.Values)
            .Select(call => call.StationId)
            .ToHashSet(StringComparer.Ordinal);

        List<Station> stations = new();
        foreach ((string stationId, HashSet<string> colors) in colorsByStationId.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            GtfsStop station = stopsById[stationId];
            if (station.Latitude is not double latitude || station.Longitude is not double longitude)
            {
                continue;
            }

            int rank = colors.Count >= 2 ? InterchangeRank
                : terminusIds.Contains(stationId) ? TerminusRank
                : OtherRank;
            stations.Add(new Station(stationId, station.StopName, new GeoPoint(latitude, longitude), rank));
        }
        return stations;
    }

    /// <summary>
    /// The colour each trip's line is drawn in. Routes drawn in one colour read as one line, so
    /// the colour, not the route, is what makes two lines different.
    /// </summary>
    private static Dictionary<string, string> ColorByTripId(RailSelection selection)
    {
        Dictionary<string, string> colorByRouteId = new(StringComparer.Ordinal);
        foreach (GtfsRoute route in selection.Routes)
        {
            colorByRouteId.TryAdd(route.RouteId, NetworkSceneBuilder.FormatColor(route.RouteColor));
        }

        Dictionary<string, string> colorByTripId = new(StringComparer.Ordinal);
        foreach (GtfsTrip trip in selection.Trips)
        {
            if (colorByRouteId.TryGetValue(trip.RouteId, out string? color))
            {
                colorByTripId.TryAdd(trip.TripId, color);
            }
        }
        return colorByTripId;
    }
}
```

- [x] **Step 5: Run to see them pass**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: 69 passed.

- [x] **Step 6: Hand off**

```
feat: build ranked stations from stop times

Platforms merge into their parent station. A station served by
two colours is an interchange; one where any trip starts or ends
is a terminus.
```

---

### Task 3: Put stations in the scene

**Files:**
- Modify: `src/MetroDisplay.Spatial/ExtentRectangle.cs`
- Modify: `src/MetroDisplay.Spatial/MercatorProjector.cs`
- Modify: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`
- Modify: `src/MetroDisplay.Contracts/NetworkScene.cs` (doc comments)
- Modify: `src/MetroDisplay.Server/Program.cs` (log line)
- Test: `tests/MetroDisplay.Spatial.Tests/ExtentRectangleTests.cs`
- Test: `tests/MetroDisplay.Spatial.Tests/MercatorProjectorTests.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneBuilderTests.cs`
- Test: `tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`

**Interfaces:**
- Consumes: `GtfsArchive.Stops`, `GtfsArchiveReader.ReadStopTimes` (Task 1);
  `StationBuilder.Build`, `Station` (Task 2); `MercatorProjector.Project`,
  `CoordinateNormalizer.Normalize(PlanePoint, ExtentRectangle)` returning `(double X, double Y)`.
- Produces:
  - `PlanePoint ExtentRectangle.Clamp(PlanePoint point)`: the nearest point of the rectangle.
  - `static double MercatorProjector.GroundMetresToPlane(double groundMetres, double latitudeDegrees)`:
    the inverse of `PlaneMetresToGround`.
  - `NetworkScene.Stations` is filled: one `StationMarker(X, Y, Name, Rank)` per station in
    or within 100 m of the extent, in station id order.
  - `NetworkSceneBuilder.Build` and `BuildLayer` throw `InvalidDataException` for a feed
    without `stops.txt` or `stop_times.txt`.

Why a station is not simply tested for being inside. The extent's edge is set by the track:
where a line ends inside the core square, the edge passes through its last vertex. The
station's own position is the middle of the station, which can lie past that vertex. Boston's
Riverside is 26 m out. So a station up to `StationReachM` (100 m of ground) beyond the edge is
moved onto the edge and kept. Farther out, the line was cut before reaching it, and it is
left out.

- [x] **Step 1: Write the failing geometry tests**

Add to `ExtentRectangleTests`:

```csharp
    [Fact]
    public void ClampsAPointToTheNearestPointOfTheRectangle()
    {
        var rectangle = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 50);

        // Inside or on the edge: unchanged.
        Assert.Equal(new PlanePoint(40, 20), rectangle.Clamp(new PlanePoint(40, 20)));
        Assert.Equal(new PlanePoint(100, 50), rectangle.Clamp(new PlanePoint(100, 50)));
        // Past one edge: moved straight back onto it.
        Assert.Equal(new PlanePoint(100, 20), rectangle.Clamp(new PlanePoint(120, 20)));
        Assert.Equal(new PlanePoint(40, 0), rectangle.Clamp(new PlanePoint(40, -5)));
        // Past a corner: moved to the corner.
        Assert.Equal(new PlanePoint(0, 50), rectangle.Clamp(new PlanePoint(-5, 60)));
    }
```

Add to `MercatorProjectorTests`:

```csharp
    [Fact]
    public void StretchesGroundDistanceIntoPlaneDistanceAwayFromTheEquator()
    {
        // The inverse of PlaneMetresToGround: 1000 m of ground at latitude 42 is about 1346 plane units.
        double planeMetres = MercatorProjector.GroundMetresToPlane(1000, latitudeDegrees: 42);

        Assert.Equal(1000 / Math.Cos(42 * Math.PI / 180), planeMetres, precision: 3);
        Assert.Equal(1000, MercatorProjector.PlaneMetresToGround(planeMetres, latitudeDegrees: 42), precision: 6);
    }
```

- [x] **Step 2: Write the failing builder tests**

Add to `NetworkSceneBuilderTests`:

```csharp
    [Fact]
    public void PlacesStationsInTheSameFrameAsTheLines()
    {
        NetworkScene scene = Build(GtfsFixtureBuilder.TwoRailLinesAndABus());

        // The frame runs from 71.12 W to 71.06 W and from 42.36 N down to 42.32 N. North End is
        // Red's northern end, so it sits on the top edge: y is 0.
        Assert.Equal(
            new[]
            {
                new StationMarker(1, 0.1353, "East Side", 2),
                new StationMarker(0.775, 0.1579, "Crossing", 3),
                new StationMarker(0.6667, 0.451, "Midway", 1),
                new StationMarker(0.8333, 0, "North End", 2),
                new StationMarker(0.5, 0.9019, "South End", 2),
                new StationMarker(0, 0.2255, "West Side", 2),
            },
            scene.Stations);
    }

    [Fact]
    public void LeavesOutStationsBeyondTheExtent()
    {
        // 1 km around the core reaches from 42.331 N to 42.349 N. Only Midway, at 42.34 N, is
        // inside. Crossing is the nearest of the rest, some 450 m past the north edge.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([42.34, -71.08], CoreRadiusKm: 1) };

        NetworkScene scene = NetworkSceneBuilder.Build(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), config);

        Assert.Equal(new[] { "Midway" }, scene.Stations.Select(station => station.Name));
    }

    [Fact]
    public void MovesAStationJustPastTheEdgeOntoIt()
    {
        // The frame's north edge is the last point of Red's track, at 42.36 N. North End's own
        // position is 0.0005 degrees, about 55 m, beyond it: the middle of a station past the
        // end of the line. South End's is 0.002 degrees, about 220 m, beyond the south edge.
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("stops.txt", """
            stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station
            place-south,South End,42.3180,-71.0900,1,
            south-platform,South End,42.3201,-71.0901,0,place-south
            place-north,North End,42.3605,-71.0700,1,
            north-platform,North End,42.3601,-71.0701,0,place-north
            """);

        NetworkScene scene = Build(fixture);

        // North End is drawn on the edge, where its line ends. South End is too far out to be
        // a station on the map.
        Assert.Equal(new[] { new StationMarker(0.8333, 0, "North End", 2) }, scene.Stations);
    }

    [Fact]
    public void RefusesAFeedWithoutStopTimes()
    {
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus().WithoutFile("stop_times.txt");

        var exception = Assert.Throws<InvalidDataException>(() => Build(fixture));

        Assert.Contains("stop_times.txt", exception.Message);
    }
```

- [x] **Step 3: Run to see them fail**

```bash
dotnet build tests/MetroDisplay.Spatial.Tests
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~NetworkSceneBuilderTests"
```

Expected: the first fails to build with CS1061 (`ExtentRectangle` has no `Clamp`) and CS0117
(`MercatorProjector` has no `GroundMetresToPlane`). In the second the four new tests fail:
three report an empty `Stations`, and `RefusesAFeedWithoutStopTimes` reports that no exception
was thrown.

- [x] **Step 4: Write `Clamp` and `GroundMetresToPlane`**

In `ExtentRectangle.cs`, after `Intersect`:

```csharp
    /// <summary>
    /// The point of the rectangle nearest to <paramref name="point"/>: the point itself when it
    /// is inside or on an edge.
    /// </summary>
    public PlanePoint Clamp(PlanePoint point) => new(Math.Clamp(point.X, MinX, MaxX), Math.Clamp(point.Y, MinY, MaxY));
```

In `MercatorProjector.cs`, after `PlaneMetresToGround`:

```csharp
    /// <summary>
    /// Converts a ground distance to the plane distance that represents it at a given latitude.
    /// The inverse of <see cref="PlaneMetresToGround"/>.
    /// </summary>
    /// <param name="groundMetres">Distance measured on the ground.</param>
    /// <param name="latitudeDegrees">Latitude the distance sits at.</param>
    public static double GroundMetresToPlane(double groundMetres, double latitudeDegrees) => groundMetres / Math.Cos(latitudeDegrees * DegreesToRadians);
```

- [x] **Step 5: Build the markers**

In `NetworkSceneBuilder.cs`:

Replace the `<remarks>` on the class:

```csharp
/// <remarks>
/// Lines and stations, framed on the extent of spec §5 and cut at its edge. Edge labels and
/// simplification each arrive in a later slice (spec §14).
/// </remarks>
```

On both `Build` and `BuildLayer`, replace the `InvalidDataException` line:

```csharp
    /// <exception cref="InvalidDataException">The feed has no rail shapes, or lacks a required file or column. <c>stops.txt</c> and <c>stop_times.txt</c> are required.</exception>
```

Add a constant below `FallbackColor`:

```csharp
    /// <summary>
    /// How far past the extent's edge a station's own position may lie and still be drawn, on
    /// the edge. That position is the middle of the station, which at the end of a line lies a
    /// little beyond the last point of track, and the track is what set the edge.
    /// </summary>
    private const double StationReachM = 100;
```

In `BuildLayer`, directly after the line that computes `centerLatitude`, add:

```csharp
        // Stations come from the calls rail trips make, so only those trips' rows are read.
        HashSet<string> tripIds = selection.Trips.Select(trip => trip.TripId).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<GtfsStopTime> stopTimes = GtfsArchiveReader.ReadStopTimes(zipBytes, tripIds);
        List<StationMarker> stations = BuildStationMarkers(
            StationBuilder.Build(selection, archive.Stops, stopTimes),
            extent,
            MercatorProjector.GroundMetresToPlane(StationReachM, centerLatitude));
```

In the `NetworkScene` constructor call, replace `Stations: [],` with:

```csharp
            Stations: stations,
```

Add this method after `ClipToRuns`:

```csharp
    /// <summary>
    /// Stations in the same normalized frame as the lines. One just past the edge is drawn on
    /// it. One farther out is left off: its line was cut before reaching it.
    /// </summary>
    /// <param name="stations">Every rail station in the feed.</param>
    /// <param name="extent">The frame the lines are normalized in.</param>
    /// <param name="reachPlaneUnits">How far past the edge still counts. Plane units, not metres of ground.</param>
    private static List<StationMarker> BuildStationMarkers(IReadOnlyList<Station> stations, ExtentRectangle extent, double reachPlaneUnits)
    {
        List<StationMarker> markers = new();
        foreach (Station station in stations)
        {
            PlanePoint position = MercatorProjector.Project(station.Position);
            PlanePoint nearest = extent.Clamp(position);
            double distancePastEdge = Math.Sqrt(Math.Pow(position.X - nearest.X, 2) + Math.Pow(position.Y - nearest.Y, 2));

            // Written this way round so a position that is not a number is left off too.
            if (!(distancePastEdge <= reachPlaneUnits))
            {
                continue;
            }

            (double normalizedX, double normalizedY) = CoordinateNormalizer.Normalize(nearest, extent);
            markers.Add(new StationMarker(normalizedX, normalizedY, station.Name, station.Rank));
        }
        return markers;
    }
```

- [x] **Step 6: Say what the contract now carries**

In `src/MetroDisplay.Contracts/NetworkScene.cs`, replace two doc lines. On `NetworkScene`:

```csharp
/// <param name="Stations">Rail stations in the extent, each with its platforms merged into one marker.</param>
```

On `StationMarker`:

```csharp
/// <param name="Rank">Prominence: 1 for an ordinary station, 2 for a terminus, 3 for an interchange. A renderer names the stations of rank 2 and above that it has room for.</param>
```

- [x] **Step 7: Run the builder and rectangle tests**

```bash
dotnet test tests/MetroDisplay.Spatial.Tests
dotnet test tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: 48 passed, then 73 passed.

- [x] **Step 8: Write the failing Server test**

Add to `NetworkEndpointTests`:

```csharp
    [Fact]
    public async Task ServesStationsWithTheScene()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("test");

        JsonElement network = await GetNetworkAsync(factory);

        JsonElement[] stations = network.GetProperty("stations").EnumerateArray().ToArray();
        Assert.Equal(6, stations.Length);
        JsonElement crossing = stations.Single(station => station.GetProperty("name").GetString() == "Crossing");
        Assert.Equal(3, crossing.GetProperty("rank").GetInt32());
        Assert.Equal(0.775, crossing.GetProperty("x").GetDouble());
        Assert.Equal(0.1579, crossing.GetProperty("y").GetDouble());
    }
```

- [x] **Step 9: Run it**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~ServesStationsWithTheScene"
```

Expected: PASS. The Server serves whatever the builder returns, so no Server code is needed
for it. The test is there so the wire names `stations`, `name`, `rank`, `x`, `y` are pinned
where the renderer reads them.

- [x] **Step 10: Count stations in the startup log**

In `Program.cs`, replace the `LogInformation` call:

```csharp
app.Logger.LogInformation(
    "Built {ArtifactVersion}: {LineCount} lines, {ShapeCount} shapes, {StationCount} stations, {WaterCount} water areas",
    scene.ArtifactVersion,
    scene.Lines.Count,
    scene.Lines.Sum(line => line.Shapes.Count),
    scene.Stations.Count,
    scene.Water.Count);
```

- [x] **Step 11: Run everything**

```bash
dotnet test MetroDisplay.slnx
```

Expected: 183 passed (Spatial 48, Osm 40, Gtfs.Static 73, Server 22), 0 warnings.

- [x] **Step 12: Hand off**

```
feat: serve stations with the scene

Stations are normalized in the rail's frame. One within 100 m of
the edge is drawn on it, since a station's own position lies a
little past the end of its track. One farther out is left off.
```

---

### Task 4: Place names without overlap

**Files:**
- Create: `web/src/labels.ts`
- Test: `web/src/labels.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces, all exported from `labels.ts`:
  - `interface Box { left: number; top: number; right: number; bottom: number }`, in CSS pixels.
  - `interface LabelRequest { anchorX: number; anchorY: number; widthPx: number; heightPx: number; priority: number }`.
  - `interface LabelSurroundings { bounds: Box; obstacles: Box[]; polylines: number[][] }`.
  - `const LABEL_GAP_PX = 6`.
  - `function placeLabels(requests: LabelRequest[], surroundings: LabelSurroundings): (Box | null)[]`:
    one entry per request, in the same order.
  - `function segmentCrossesBox(startX: number, startY: number, endX: number, endY: number, box: Box): boolean`.

This module knows nothing about stations, a canvas, or the scene. It takes rectangles and
returns rectangles, which is what makes it testable without a browser and reusable for place
names in slice 8.

How it decides. Names are taken highest priority first. Each tries eight positions around its
marker, in this order: right, left, right and above, right and below, left and above, left and
below, centred above, centred below. A position is ruled out if it leaves the bounds, overlaps
a name already placed, or covers a marker. Of what is left, the first that crosses no line
wins. If every one crosses a line, the first is used anyway. If none is left, the name is not
placed.

- [x] **Step 1: Write the failing tests**

`web/src/labels.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { LABEL_GAP_PX, placeLabels, segmentCrossesBox, type Box, type LabelRequest, type LabelSurroundings } from './labels';

const wideOpen: Box = { left: 0, top: 0, right: 1000, bottom: 1000 };

function surroundings(overrides: Partial<LabelSurroundings> = {}): LabelSurroundings {
  return { bounds: wideOpen, obstacles: [], polylines: [], ...overrides };
}

/** A name 40 px wide and 10 px tall beside a marker at the given point. */
function request(anchorX: number, anchorY: number, priority = 1): LabelRequest {
  return { anchorX, anchorY, widthPx: 40, heightPx: 10, priority };
}

describe('placeLabels', () => {
  it('puts a name to the right of its marker when nothing is in the way', () => {
    const [box] = placeLabels([request(500, 500)], surroundings());

    expect(box).toEqual({ left: 500 + LABEL_GAP_PX, top: 495, right: 546, bottom: 505 });
  });

  it('moves a name to the left when the right would cross a line', () => {
    // A line running east from the marker, as a terminus has on one side.
    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: [[500, 500, 900, 500]] }));

    expect(box).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('goes above or below a line that runs through the marker both ways', () => {
    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: [[100, 500, 900, 500]] }));

    // Right and above: the third position tried.
    expect(box).toEqual({ left: 506, top: 484, right: 546, bottom: 494 });
  });

  it('accepts crossing a line when every position crosses one', () => {
    // A grid of lines 8 px apart leaves no clear gap for a 10 px tall name.
    const grid: number[][] = [];
    for (let lineY = 400; lineY <= 600; lineY += 8) {
      grid.push([0, lineY, 1000, lineY]);
    }

    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: grid }));

    // Falls back to the first position, on the right.
    expect(box).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
  });

  it('never covers another marker', () => {
    const neighbour: Box = { left: 516, top: 496, right: 524, bottom: 504 };

    const [box] = placeLabels([request(500, 500)], surroundings({ obstacles: [neighbour] }));

    expect(box).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('keeps two names from overlapping', () => {
    // Markers 4 px apart: both names want the same place on the right.
    const [first, second] = placeLabels([request(500, 500), request(500, 504)], surroundings());

    expect(first).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
    expect(second).toEqual({ left: 454, top: 499, right: 494, bottom: 509 });
  });

  it('places the higher priority name first, whatever the order they arrive in', () => {
    const [lowPriority, highPriority] = placeLabels([request(500, 500, 2), request(500, 504, 3)], surroundings());

    // The later, more important name takes the right-hand place.
    expect(highPriority).toEqual({ left: 506, top: 499, right: 546, bottom: 509 });
    expect(lowPriority).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('gives up on a name with nowhere to go, without disturbing the others', () => {
    // Room for one name only: the bounds are barely larger than a name beside its marker.
    const tight: Box = { left: 494, top: 490, right: 550, bottom: 510 };

    const placements = placeLabels([request(500, 500), request(500, 500)], surroundings({ bounds: tight }));

    expect(placements[0]).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
    expect(placements[1]).toBeNull();
  });

  it('keeps a name wholly on screen', () => {
    // A marker near the right edge: the name would run off, so it goes left.
    const [box] = placeLabels([request(980, 500)], surroundings());

    expect(box).toEqual({ left: 934, top: 495, right: 974, bottom: 505 });
  });

  it('returns nothing for no requests', () => {
    expect(placeLabels([], surroundings())).toEqual([]);
  });
});

describe('segmentCrossesBox', () => {
  const box: Box = { left: 0, top: 0, right: 100, bottom: 100 };

  it('finds a segment that passes through with both ends outside', () => {
    expect(segmentCrossesBox(-50, 50, 150, 50, box)).toBe(true);
    // Across a corner.
    expect(segmentCrossesBox(60, -20, 120, 40, box)).toBe(true);
  });

  it('finds a segment that lies wholly inside', () => {
    expect(segmentCrossesBox(10, 10, 20, 20, box)).toBe(true);
  });

  it('rejects a segment that stays to one side', () => {
    expect(segmentCrossesBox(-50, -10, 150, -10, box)).toBe(false);
    expect(segmentCrossesBox(110, 0, 110, 100, box)).toBe(false);
    // Passes the corner without reaching it.
    expect(segmentCrossesBox(90, -30, 130, 10, box)).toBe(false);
  });

  it('treats a point as a segment of no length', () => {
    expect(segmentCrossesBox(50, 50, 50, 50, box)).toBe(true);
    expect(segmentCrossesBox(150, 50, 150, 50, box)).toBe(false);
  });
});
```

- [x] **Step 2: Run to see them fail**

```bash
cd web
npx vitest run src/labels.test.ts
```

Expected: FAIL, cannot find module `./labels`.

- [x] **Step 3: Write the placer**

`web/src/labels.ts`:

```ts
/** A rectangle in CSS pixels. */
export interface Box {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

/** A name that wants a place beside its marker. */
export interface LabelRequest {
  /** Centre of the marker the name belongs to. */
  anchorX: number;
  anchorY: number;
  widthPx: number;
  heightPx: number;
  /** Names with a higher priority are placed first, so they get the better positions. */
  priority: number;
}

/** What a name has to fit among. */
export interface LabelSurroundings {
  /** A name must lie wholly inside this. */
  bounds: Box;
  /** Markers. A name never covers one. */
  obstacles: Box[];
  /** Lines, each a flat [x0, y0, x1, y1, ...] array in pixels. A name avoids crossing one when it can. */
  polylines: number[][];
}

/** Clear space between a marker's centre and its name. Larger than any marker's radius. */
export const LABEL_GAP_PX = 6;

/**
 * Finds a place for each name, highest priority first. A name takes the first position around
 * its marker that overlaps no name already placed and no marker, preferring one that crosses
 * no line.
 * Returns one entry per request, in the same order: the name's box, or null when it has
 * nowhere to go.
 */
export function placeLabels(requests: LabelRequest[], surroundings: LabelSurroundings): (Box | null)[] {
  const placements: (Box | null)[] = requests.map(() => null);
  const placedBoxes: Box[] = [];

  // Highest priority first. Equal priorities keep the order they arrived in.
  const order = requests
    .map((_request, requestIndex) => requestIndex)
    .sort((firstIndex, secondIndex) => requests[secondIndex].priority - requests[firstIndex].priority || firstIndex - secondIndex);

  for (const requestIndex of order) {
    const freeBoxes = candidateBoxes(requests[requestIndex]).filter(
      (box) =>
        isInside(box, surroundings.bounds) &&
        !placedBoxes.some((placedBox) => overlaps(box, placedBox)) &&
        !surroundings.obstacles.some((obstacle) => overlaps(box, obstacle)),
    );

    const chosenBox = freeBoxes.find((box) => !crossesAnyLine(box, surroundings.polylines)) ?? freeBoxes[0];
    if (chosenBox) {
      placements[requestIndex] = chosenBox;
      placedBoxes.push(chosenBox);
    }
  }

  return placements;
}

/** The eight positions tried, in order of preference: beside the marker first, then diagonal, then above and below. */
function candidateBoxes(request: LabelRequest): Box[] {
  const { anchorX, anchorY, widthPx, heightPx } = request;
  const eastLeft = anchorX + LABEL_GAP_PX;
  const westLeft = anchorX - LABEL_GAP_PX - widthPx;
  const centredLeft = anchorX - widthPx / 2;
  const middleTop = anchorY - heightPx / 2;
  const aboveTop = anchorY - LABEL_GAP_PX - heightPx;
  const belowTop = anchorY + LABEL_GAP_PX;

  const corners: [number, number][] = [
    [eastLeft, middleTop],
    [westLeft, middleTop],
    [eastLeft, aboveTop],
    [eastLeft, belowTop],
    [westLeft, aboveTop],
    [westLeft, belowTop],
    [centredLeft, aboveTop],
    [centredLeft, belowTop],
  ];
  return corners.map(([left, top]) => ({ left, top, right: left + widthPx, bottom: top + heightPx }));
}

function isInside(box: Box, bounds: Box): boolean {
  return box.left >= bounds.left && box.top >= bounds.top && box.right <= bounds.right && box.bottom <= bounds.bottom;
}

/** Boxes that only share an edge do not overlap. */
function overlaps(first: Box, second: Box): boolean {
  return first.left < second.right && second.left < first.right && first.top < second.bottom && second.top < first.bottom;
}

function crossesAnyLine(box: Box, polylines: number[][]): boolean {
  for (const polyline of polylines) {
    for (let index = 2; index < polyline.length; index += 2) {
      if (segmentCrossesBox(polyline[index - 2], polyline[index - 1], polyline[index], polyline[index + 1], box)) {
        return true;
      }
    }
  }
  return false;
}

/**
 * Whether a segment passes through a box. Each side of the box narrows the part of the
 * segment, as a fraction from 0 at its start to 1 at its end, that lies inside (Liang-Barsky).
 */
export function segmentCrossesBox(startX: number, startY: number, endX: number, endY: number, box: Box): boolean {
  const changeX = endX - startX;
  const changeY = endY - startY;
  let entryFraction = 0;
  let exitFraction = 1;

  // Each pair is how fast the segment moves toward the outside of one side, and how far
  // inside that side it starts.
  const sides: [number, number][] = [
    [-changeX, startX - box.left],
    [changeX, box.right - startX],
    [-changeY, startY - box.top],
    [changeY, box.bottom - startY],
  ];

  for (const [direction, distance] of sides) {
    if (direction === 0) {
      // Parallel to this side: outside it for the whole length, or not at all.
      if (distance < 0) {
        return false;
      }
      continue;
    }

    const fraction = distance / direction;
    if (direction < 0) {
      entryFraction = Math.max(entryFraction, fraction);
    } else {
      exitFraction = Math.min(exitFraction, fraction);
    }
    if (entryFraction > exitFraction) {
      return false;
    }
  }

  return true;
}
```

- [x] **Step 4: Run to see them pass**

```bash
npx vitest run src/labels.test.ts
npx tsc
```

Expected: 14 passed, and `tsc` prints nothing.

- [x] **Step 5: Hand off**

```
feat: place map names clear of each other

A greedy placer: highest priority first, eight positions around
each marker, never over another name or marker, off the lines
where it can be.
```

---

### Task 5: Draw stations

**Files:**
- Create: `web/src/stations.ts`
- Modify: `web/src/contract.ts`
- Modify: `web/src/main.ts`
- Modify: `web/src/draw.test.ts` (one line)
- Test: `web/src/stations.test.ts`

**Interfaces:**
- Consumes: `placeLabels`, `Box`, `LabelRequest` from `./labels` (Task 4); `toPixel`, `Fit`
  from `./fit`; the wire fields `stations[].x`, `.y`, `.name`, `.rank` (Task 3).
- Produces:
  - `interface StationMarker { x: number; y: number; name: string; rank: number }` and
    `NetworkScene.stations: StationMarker[]` in `contract.ts`.
  - `function drawStations(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit, bounds: Box): void`
    exported from `stations.ts`.

What is drawn: a dot of radius 1.5 px for rank 1; a ring of radius 3.5 px with a centre in the
page's background colour for ranks 2 and 3; an 11 px name beside each ring that has room. All
125 markers are obstacles to every name, so a name never sits on a neighbouring station.

- [x] **Step 1: Add stations to the hand-written contract types**

In `web/src/contract.ts`, add one member to `NetworkScene`, between `lines` and `water`:

```ts
  stations: StationMarker[];
```

and add this interface above `WaterArea`:

```ts
/** A station. `rank` is 1 for an ordinary station, 2 for a terminus, 3 for an interchange. */
export interface StationMarker {
  x: number;
  y: number;
  name: string;
  rank: number;
}
```

- [x] **Step 2: Keep the water tests compiling**

`NetworkScene` now requires `stations`. In `web/src/draw.test.ts`, in `sceneWith`, change
`lines: [], water };` to:

```ts
lines: [], stations: [], water };
```

- [x] **Step 3: Write the failing tests**

`web/src/stations.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { NetworkScene, StationMarker } from './contract';
import { drawStations } from './stations';

/** A stand-in canvas context that records what drawStations draws. Every character is 6 px wide. */
function recordingContext(): { context: CanvasRenderingContext2D; calls: string[] } {
  const calls: string[] = [];
  const context = {
    font: '',
    fillStyle: '',
    strokeStyle: '',
    lineWidth: 0,
    textAlign: '',
    textBaseline: '',
    beginPath: () => undefined,
    arc: (pixelX: number, pixelY: number, radiusPx: number) => calls.push(`arc ${pixelX},${pixelY} r${radiusPx}`),
    fill: () => undefined,
    stroke: () => calls.push('stroke'),
    measureText: (text: string) => ({ width: text.length * 6 }),
    fillText: (text: string, pixelX: number, pixelY: number) => calls.push(`text ${text} ${pixelX},${pixelY}`),
  };
  return { context: context as unknown as CanvasRenderingContext2D, calls };
}

function sceneWith(stations: StationMarker[], lines: NetworkScene['lines'] = []): NetworkScene {
  return { artifactVersion: 'test@00000000', extent: { aspect: 1, coreRadiusKm: 22, spanKm: 10 }, lines, stations, water: [] };
}

/** One pixel per thousandth of a unit, no offset: 0.5 lands on pixel 500. */
const fit = { scale: 1000, originX: 0, originY: 0 };
const canvas = { left: 0, top: 0, right: 1000, bottom: 1000 };

describe('drawStations', () => {
  it('draws a dot for an ordinary station and no name', () => {
    const { context, calls } = recordingContext();

    drawStations(context, sceneWith([{ x: 0.5, y: 0.25, name: 'Midway', rank: 1 }]), fit, canvas);

    expect(calls).toEqual(['arc 500,250 r1.5']);
  });

  it('draws a ring and a name for a terminus and for an interchange', () => {
    const { context, calls } = recordingContext();

    drawStations(
      context,
      sceneWith([
        { x: 0.25, y: 0.25, name: 'North End', rank: 2 },
        { x: 0.5, y: 0.5, name: 'Crossing', rank: 3 },
      ]),
      fit,
      canvas,
    );

    expect(calls).toEqual([
      'arc 250,250 r3.5', 'stroke',
      'arc 500,500 r3.5', 'stroke',
      // Each name starts 6 px right of its marker, centred on it.
      'text North End 256,250',
      'text Crossing 506,500',
    ]);
  });

  it('moves a name off the line its station is on', () => {
    const { context, calls } = recordingContext();
    // A terminus with its line running away to the east.
    const line = { id: 'Red', name: 'Red Line', color: '#DA291C', shapes: [{ id: 'shape-red', points: [0.5, 0.5, 0.9, 0.5], lengthM: 1000 }] };

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: 'Alewife', rank: 2 }], [line]), fit, canvas);

    // 'Alewife' is 42 px wide, so on the left it starts at 500 - 6 - 42.
    expect(calls).toContain('text Alewife 452,500');
  });

  it('gives the place to the interchange when two names want it', () => {
    const { context, calls } = recordingContext();

    drawStations(
      context,
      sceneWith([
        { x: 0.5, y: 0.5, name: 'Terminus', rank: 2 },
        { x: 0.5, y: 0.501, name: 'Crossing', rank: 3 },
      ]),
      fit,
      canvas,
    );

    expect(calls).toContain('text Crossing 506,501');
    expect(calls).toContain('text Terminus 446,500');
  });

  it('keeps the marker when the name has nowhere to go', () => {
    const { context, calls } = recordingContext();
    // No name fits inside bounds this small.
    const cramped = { left: 495, top: 495, right: 505, bottom: 505 };

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: 'Crossing', rank: 3 }]), fit, cramped);

    expect(calls).toEqual(['arc 500,500 r3.5', 'stroke']);
  });

  it('draws a ring without a name for a ranked station the feed left unnamed', () => {
    const { context, calls } = recordingContext();

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: '', rank: 2 }]), fit, canvas);

    expect(calls).toEqual(['arc 500,500 r3.5', 'stroke']);
  });
});
```

- [x] **Step 4: Run to see them fail**

```bash
npx vitest run src/stations.test.ts
```

Expected: FAIL, cannot find module `./stations`.

- [x] **Step 5: Write the drawing**

`web/src/stations.ts`:

```ts
import type { NetworkScene, StationMarker } from './contract';
import { toPixel, type Fit } from './fit';
import { placeLabels, type Box, type LabelRequest } from './labels';

const MARKER_COLOR = '#c5ced8';
// The page background, so a ring reads as hollow over a line.
const RING_CENTRE_COLOR = '#07090c';
const NAME_COLOR = '#9aa7b4';
const NAME_FONT = '11px ui-monospace, monospace';
const NAME_HEIGHT_PX = 13;

const DOT_RADIUS_PX = 1.5;
const RING_RADIUS_PX = 3.5;
const RING_WIDTH_PX = 1.5;

// Rank 1 is an ordinary station. A terminus (2) and an interchange (3) get a ring and a name.
const NAMED_FROM_RANK = 2;

interface PlacedMarker {
  station: StationMarker;
  pixelX: number;
  pixelY: number;
}

/**
 * Draws a marker at every station, and a name beside each terminus and interchange that has
 * room for one. Call after the lines, so markers sit on top of them.
 * bounds: the area names must stay inside, normally the whole canvas.
 */
export function drawStations(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit, bounds: Box): void {
  const markers: PlacedMarker[] = scene.stations.map((station) => {
    const [pixelX, pixelY] = toPixel(fit, station.x, station.y);
    return { station, pixelX, pixelY };
  });

  // The font has to be set before names are measured.
  context.font = NAME_FONT;
  const namedMarkers = markers.filter((marker) => marker.station.rank >= NAMED_FROM_RANK && marker.station.name !== '');
  const requests: LabelRequest[] = namedMarkers.map((marker) => ({
    anchorX: marker.pixelX,
    anchorY: marker.pixelY,
    widthPx: context.measureText(marker.station.name).width,
    heightPx: NAME_HEIGHT_PX,
    priority: marker.station.rank,
  }));
  const nameBoxes = placeLabels(requests, {
    bounds,
    obstacles: markers.map(markerBox),
    polylines: linesInPixels(scene, fit),
  });

  for (const marker of markers) {
    drawMarker(context, marker);
  }

  context.fillStyle = NAME_COLOR;
  context.textAlign = 'left';
  context.textBaseline = 'middle';
  nameBoxes.forEach((nameBox, nameIndex) => {
    if (nameBox) {
      context.fillText(namedMarkers[nameIndex].station.name, nameBox.left, (nameBox.top + nameBox.bottom) / 2);
    }
  });
}

function drawMarker(context: CanvasRenderingContext2D, marker: PlacedMarker): void {
  context.beginPath();
  if (marker.station.rank < NAMED_FROM_RANK) {
    context.arc(marker.pixelX, marker.pixelY, DOT_RADIUS_PX, 0, 2 * Math.PI);
    context.fillStyle = MARKER_COLOR;
    context.fill();
    return;
  }

  context.arc(marker.pixelX, marker.pixelY, RING_RADIUS_PX, 0, 2 * Math.PI);
  context.fillStyle = RING_CENTRE_COLOR;
  context.fill();
  context.lineWidth = RING_WIDTH_PX;
  context.strokeStyle = MARKER_COLOR;
  context.stroke();
}

/** The square a marker covers, sized for the larger marker so no name crowds either kind. */
function markerBox(marker: PlacedMarker): Box {
  const reachPx = RING_RADIUS_PX + RING_WIDTH_PX / 2;
  return {
    left: marker.pixelX - reachPx,
    top: marker.pixelY - reachPx,
    right: marker.pixelX + reachPx,
    bottom: marker.pixelY + reachPx,
  };
}

function linesInPixels(scene: NetworkScene, fit: Fit): number[][] {
  const polylines: number[][] = [];
  for (const line of scene.lines) {
    for (const shape of line.shapes) {
      const polyline: number[] = [];
      for (let index = 0; index + 1 < shape.points.length; index += 2) {
        const [pixelX, pixelY] = toPixel(fit, shape.points[index], shape.points[index + 1]);
        polyline.push(pixelX, pixelY);
      }
      polylines.push(polyline);
    }
  }
  return polylines;
}
```

- [x] **Step 6: Call it from the page**

In `web/src/main.ts`:

Add the import after the `./fit` import:

```ts
import { drawStations } from './stations';
```

In `render`, after `drawNetwork(drawing, scene, fit);`:

```ts
  drawStations(drawing, scene, fit, { left: 0, top: 0, right: widthPx, bottom: heightPx });
```

In the status line, add the station count:

```ts
    statusLine.textContent = `${scene.lines.length} lines · ${scene.stations.length} stations · ${scene.artifactVersion}${attribution}`;
```

- [x] **Step 7: Run everything in the renderer**

```bash
npx vitest run
npx tsc
```

Expected: 28 passed in 4 files, and `tsc` prints nothing.

- [x] **Step 8: Hand off**

```
feat: draw station markers and names

Every station gets a marker. Termini and interchanges get a ring
and, where there is room, a name.
```

---

### Task 6: Check against the real feed

**Files:**
- Modify: `docs/plans/2026-10-13-slice-04-stations.md` (tick the boxes)
- Modify, only if the look needs it: the constants at the top of `web/src/stations.ts`

No new code. Tests cannot see two things this task looks at: what Boston's real stations come
out as, and whether the map reads well.

- [x] **Step 1: Boston's numbers**

```bash
dotnet run --project src/MetroDisplay.Server
```

Expected in the log: `8 lines, 67 shapes, 125 stations, 254 water areas`, and no request to
`overpass-api.de`. The Server should reach `Now listening` about a second later than it did
before this slice. If the stop times add more than three seconds, stop and report the figure
before going on.

In a second shell. The last line printed is the check on the edge rule:

```bash
curl -s http://localhost:5180/api/network -o network.json
python -c "import json,collections; stations=json.load(open('network.json'))['stations']; print(len(stations), sorted(collections.Counter(station['rank'] for station in stations).items())); print(sorted(station['name'] for station in stations if station['rank']==3)); print([station for station in stations if station['name']=='Riverside'])"
rm network.json
```

Expected:

```
125 [(1, 100), (2, 19), (3, 6)]
['Downtown Crossing', 'Government Center', 'Haymarket', 'North Station', 'Park Street', 'State']
[{'x': 0, 'y': 0.4341, 'name': 'Riverside', 'rank': 2}]
```

- [x] **Step 2: By eye**

Run `npm run dev` in `web/` and open `http://localhost:5173`.

Expected:

- The status line reads `8 lines · 125 stations · …`.
- A small pale dot at every station, on its line.
- Riverside, at the far west, has its ring exactly on the frame's left edge with its name
  beside it. Its own position is 26 m outside the frame.
- A ring and a name at each line end: Alewife, Braintree, Ashmont, Mattapan, Oak Grove,
  Forest Hills, Wonderland, Bowdoin, Riverside, Boston College, Cleveland Circle, Heath Street,
  Medford/Tufts, Union Square. And at the short turns: Orient Heights, Babcock Street,
  Reservoir, East Somerville.
- No name lies along its own line: Alewife's sits left of its marker, away from the track.
- Downtown, six rings in a knot and about three names. No two names overlap, and no name
  covers a ring or a dot.
- Kenmore has a ring and probably no name. It is boxed in by its neighbours' dots.
- Resize the window. Names move and drop as the map shrinks; markers never do.

The colours and sizes are the constants at the top of `web/src/stations.ts`
(`MARKER_COLOR`, `NAME_COLOR`, `NAME_FONT`, `DOT_RADIUS_PX`, `RING_RADIUS_PX`). Change them
here if the developer asks for a different look, re-run `npx vitest run`, and say what
changed in the hand-off. `RING_RADIUS_PX` and `DOT_RADIUS_PX` appear in the expected values of
`stations.test.ts`.

- [x] **Step 3: Commuter rail, as a second look**

In `cities/mbta.json` change `"routeTypes": [0, 1]` to `"routeTypes": [0, 1, 2]`, restart
the Server and reload.

Expected in the log: `22 lines, 148 shapes, 188 stations`. The feed has 262 rail stations with
commuter rail; the 74 more than 100 m beyond the 22 km square are left out. On screen: commuter stations dotted along the purple lines up
to the frame's edge, none outside it.

Restore `"routeTypes": [0, 1]`, stop the Server, then:

```bash
git status --short cities/
```

Expected: no output.

- [x] **Step 4: Tick the plan and hand off**

Tick every box in this plan. The commit carries the plan, and `stations.ts` only if Step 2
changed it:

```
docs: record slice 4 as built
```

---

## Done when

- `dotnet test MetroDisplay.slnx` passes with 183 tests and no warnings.
- `npx vitest run` in `web/` passes with 28 tests, and `npx tsc` prints nothing.
- Boston shows 125 stations: 100 dots and 25 rings, with names that never overlap.
- `cities/mbta.json` is unchanged.

## Carried into later slices

- **Which names survive downtown is decided by id order.** Among equal ranks the placer takes
  names in the order the scene lists them. A better tie-break, such as how many lines meet at
  the station, waits until the present choice looks wrong on screen.
- **Dots crowd out names.** Every marker is an obstacle, which is why Kenmore loses its name.
  Letting a name overlap a rank 1 dot is the knob.
- **Rare trips make termini.** Reservoir and East Somerville are named for a handful of trips
  a day. A minimum share of a route's trips is the knob (spec §14).
- **A long shared trunk makes every station an interchange.** BART, in slice 13 (spec §14).
- **Names are placed on every repaint.** Each resize tests about 25 names against 17,000 line
  segments, which is quick on a desktop. Slice 9 cuts the segment count; if the Pi still
  stutters, the placement can be kept between repaints of one size.
- **Place names (slice 8)** go through `placeLabels` with their own priorities, in one call
  with the station names so the two kinds cannot overlap.
- **Edge labels (slice 7)** need each clipped line's destination. `StationBuilder` already
  finds where every trip ends.
- **A bad number in `stops.txt`**, such as text in `stop_lat`, still fails with CsvHelper's own
  message, which does not name the file. That is the slice 1 item already on the slice 16 list.
