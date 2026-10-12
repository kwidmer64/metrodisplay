# Slice 3: True-Scale Extent and Clip Implementation Plan

**Goal:** The map is framed by real distance from the city's core point, and rail that runs
past that frame is cut at its edge.

**Architecture:** The extent is the rail's bounding box intersected with the core square, the
box reaching `coreRadiusKm` from `core` in each direction. `GeoBox` moves into
`MetroDisplay.Spatial` so the rail and the water build that square with the same code. Two
pieces of pure geometry join it there: a rectangle intersection and a polyline clipper.
`NetworkSceneBuilder` computes the extent, clips each shape into runs, then normalizes. The
wire contract, `MetroDisplay.Osm`'s logic, the Server's wiring and the renderer do not change.

**Tech Stack:** .NET 10, C#, xUnit 2.9.3. No new packages.

**Spec:** `docs/specs/2026-09-22-metrodisplay-design.md`: §14 "True-scale extent and clip
(slice 3)", §4 (the transform), §5 (extent and clipping).

**Verified before writing (2026-10-11):** the clipper, the intersection and the changed builder
below ran in a scratch app against the feed cached on 2026-10-08.

- Subway and light rail (`routeTypes` `[0, 1]`): 67 shapes, none cut, and the scene's JSON is
  identical to today's, character for character (254,431 characters without water).
- With commuter rail (`[0, 1, 2]`): 22 lines and 148 shapes before and after. 70 of the 145
  distinct shapes are cut, none is dropped, none splits into more than one run. The extent
  becomes the whole core square, and the rail JSON falls from 1,190,717 to 732,668 characters.
- Every expected value in the tests of Tasks 3 and 4 is the scratch app's output, except
  `RefusesACityWhoseRailOnlyPassesAroundItsSquare`, which was reasoned from the fixture and
  not run.

## Global Constraints

- Target framework `net10.0`, SDK 10.0.401.
- **The developer commits.** Every task ends with a hand-off step that supplies a commit
  message. No step runs `git add`, `git commit`, `git mv`, or any other git write. Files are
  moved with plain `mv`.
- Commit messages: Conventional Commits prefix, imperative subject of 50 characters or
  fewer, body only when the why is not obvious, no trailers of any kind.
- **Naming:** readable words only. No single letters and no two- or three-character stubs,
  including locals, lambda parameters and generics. `X` and `Y` only as members of a
  coordinate type.
- **Unit suffixes** on every unit-bearing name: `radiusKm`, `LengthM`.
- Public types carry `<summary>`/`<param>` doc comments in the style of `CityConfig.cs`.
- Comments the developer wrote in `NetworkSceneBuilder.cs` stay wherever their line stays.
- TDD: write the failing test, watch it fail, write the minimal code, watch it pass.
- Builds finish with zero warnings.
- A city whose rail fits inside its core square must come out exactly as it does today.

## Review Focus

The five inputs most likely to bite that the spec implies but does not spell out. Each has a
pinned test in the task that owns the code.

1. **A line that leaves the frame and comes back.** A loop or a branch can cross the edge more
   than twice. Joining the pieces would draw a straight line across the outside.
   → `GivesOneRunPerVisit` (Task 3), `SplitsAShapeThatLeavesAndReturnsIntoNumberedRuns` (Task 4)
2. **A segment with both ends outside that still crosses the frame.** Testing only the
   endpoints would drop it and leave a gap at a corner.
   → `KeepsASegmentThatCrossesWithBothEndsOutside` (Task 3)
3. **A point exactly on the edge.** A vertex on the boundary, or a line that only touches a
   corner, must not produce a run of no length or split one run in two.
   → `DrawsNothingForALineThatOnlyTouchesACorner`, `KeepsOneRunWhenALineTurnsBackAtTheEdge`,
   `EndsTheRunAtAVertexOnTheEdge` (Task 3)
4. **A core point in the wrong place.** A typo in `extent.core` leaves no rail in the square.
   Startup must say so and name the point, not advise deleting the cached feed, and must write
   the numbers with decimal points whatever the machine's culture.
   → `RefusesACityWithNoRailNearItsCore`, `RefusesACityWhoseRailOnlyPassesAroundItsSquare`
   (Task 4), `NamesTheCoreWhenNoRailIsInsideTheSquare` (Task 4)
5. **A network that already fits.** Most cities sit inside their square. Their lines must come
   back point for point, repeated points included.
   → `ReturnsALineInsideUntouched` (Task 3), the existing builder tests (Task 4), and the
   comparison against today's output (Task 5)

## File Structure

```
src/MetroDisplay.Spatial/
  GeoBox.cs                 moved from src/MetroDisplay.Osm/, namespace MetroDisplay.Spatial
  ExtentRectangle.cs        + Intersect
  MercatorProjector.cs      + Unproject
  PolylineClipper.cs        new: polyline + rectangle -> the runs inside it

src/MetroDisplay.Osm/
  OverpassQuery.cs          + one using

src/MetroDisplay.Gtfs.Static/
  Pipeline/NetworkSceneBuilder.cs   extent, clipping, run ids, the no-rail error

tests/MetroDisplay.Spatial.Tests/
  GeoBoxTests.cs            moved from tests/MetroDisplay.Osm.Tests/
  ExtentRectangleTests.cs   + 4 tests
  MercatorProjectorTests.cs + 1 theory (4 cases)
  PolylineClipperTests.cs   new, 13 tests

tests/MetroDisplay.Osm.Tests/
  OverpassQueryTests.cs     + one using

tests/MetroDisplay.Gtfs.Static.Tests/
  NetworkSceneBuilderTests.cs       + 5 tests

tests/MetroDisplay.Server.Tests/
  NetworkEndpointTests.cs           + 1 test
```

Test counts, starting from 119 (Spatial 20, Gtfs.Static 37, Osm 42, Server 20):

| After task | Spatial | Gtfs.Static | Osm | Server | Total |
|---|---|---|---|---|---|
| 1 | 22 | 37 | 40 | 20 | 119 |
| 2 | 30 | 37 | 40 | 20 | 127 |
| 3 | 43 | 37 | 40 | 20 | 140 |
| 4 | 43 | 42 | 40 | 21 | 146 |

---

### Task 1: Move `GeoBox` into `Spatial`

**Files:**
- Move: `src/MetroDisplay.Osm/GeoBox.cs` → `src/MetroDisplay.Spatial/GeoBox.cs`
- Move: `tests/MetroDisplay.Osm.Tests/GeoBoxTests.cs` → `tests/MetroDisplay.Spatial.Tests/GeoBoxTests.cs`
- Modify: `src/MetroDisplay.Osm/OverpassQuery.cs` (one `using`)
- Modify: `tests/MetroDisplay.Osm.Tests/OverpassQueryTests.cs` (one `using`)
- Modify: `docs/specs/2026-09-22-metrodisplay-design.md` §3, if it lists `GeoBox` under `Osm`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MetroDisplay.Spatial.GeoBox`, unchanged apart from its namespace:
  `GeoBox(double South, double West, double North, double East)`, `CentreLatitude`,
  `static GeoBox Around(GeoPoint centre, double radiusKm)`, `ExtentRectangle ToPlane()`.

This is a move, not a change. `Gtfs.Static` needs the core square, and it must not depend on
`Osm`. Both already depend on `Spatial`. There is no new test; the two `GeoBoxTests` move with
the type and the rest of the suite is the safety net.

- [x] **Step 1: Confirm the starting point**

```bash
dotnet test MetroDisplay.slnx
```

Expected: 119 passed, 0 failed, 0 warnings.

- [x] **Step 2: Move the tests first**

```bash
mv tests/MetroDisplay.Osm.Tests/GeoBoxTests.cs tests/MetroDisplay.Spatial.Tests/GeoBoxTests.cs
```

In the moved file, replace the first four lines:

```csharp
using MetroDisplay.Spatial;
using Xunit;

namespace MetroDisplay.Osm.Tests;
```

with:

```csharp
using Xunit;

namespace MetroDisplay.Spatial.Tests;
```

- [x] **Step 3: Run to see it fail**

```bash
dotnet build tests/MetroDisplay.Spatial.Tests
```

Expected: error CS0246, the type or namespace `GeoBox` could not be found.

- [x] **Step 4: Move the type**

```bash
mv src/MetroDisplay.Osm/GeoBox.cs src/MetroDisplay.Spatial/GeoBox.cs
```

In the moved file, replace the first three lines:

```csharp
using MetroDisplay.Spatial;

namespace MetroDisplay.Osm;
```

with:

```csharp
namespace MetroDisplay.Spatial;
```

- [x] **Step 5: Point the two files that lose sight of it at the new namespace**

`src/MetroDisplay.Osm/OverpassQuery.cs`: the usings become

```csharp
using System.Globalization;
using MetroDisplay.Spatial;
```

`tests/MetroDisplay.Osm.Tests/OverpassQueryTests.cs`: the usings become

```csharp
using System.Globalization;
using MetroDisplay.Spatial;
using Xunit;
```

Every other file that names `GeoBox` already imports `MetroDisplay.Spatial`:
`WaterLayerLoader.cs`, `WaterLayerBuilderTests.cs`, `WaterLayerLoaderTests.cs`,
`NetworkEndpointTests.cs`.

- [x] **Step 6: Run everything**

```bash
dotnet test MetroDisplay.slnx
```

Expected: 119 passed (Spatial 22, Gtfs.Static 37, Osm 40, Server 20), 0 warnings.

- [x] **Step 7: Keep the spec's project list true**

```bash
grep -n "GeoBox" docs/specs/2026-09-22-metrodisplay-design.md
```

If §3 lists `GeoBox` among `Osm`'s types, move the name to the `Spatial` line. If it is not
mentioned, change nothing.

- [x] **Step 8: Hand off**

```
refactor: move GeoBox into Spatial

The rail needs the core square that water already builds, and
Gtfs.Static must not depend on Osm.
```

---

### Task 2: Rectangle intersection and the inverse projection

**Files:**
- Modify: `src/MetroDisplay.Spatial/ExtentRectangle.cs`
- Modify: `src/MetroDisplay.Spatial/MercatorProjector.cs`
- Test: `tests/MetroDisplay.Spatial.Tests/ExtentRectangleTests.cs`
- Test: `tests/MetroDisplay.Spatial.Tests/MercatorProjectorTests.cs`

**Interfaces:**
- Consumes: `ExtentRectangle`, `PlanePoint`, `GeoPoint`, `MercatorProjector.Project`.
- Produces:
  - `ExtentRectangle? ExtentRectangle.Intersect(ExtentRectangle other)`: the overlap, or
    `null` when the two share no point. Rectangles that share only an edge or a corner overlap
    in a rectangle of no width or no height, which is returned, not `null`.
  - `static GeoPoint MercatorProjector.Unproject(PlanePoint point)`: the inverse of `Project`.

`Unproject` exists because a cut point is found on the plane, and a run's ground length is
measured between positions on the globe.

- [x] **Step 1: Write the failing tests**

Add to `ExtentRectangleTests`:

```csharp
    [Fact]
    public void IntersectsToTheAreaBothRectanglesCover()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var second = new ExtentRectangle(MinX: 60, MinY: -20, MaxX: 140, MaxY: 70);

        Assert.Equal(new ExtentRectangle(MinX: 60, MinY: 0, MaxX: 100, MaxY: 70), first.Intersect(second));
        Assert.Equal(first.Intersect(second), second.Intersect(first));
    }

    [Fact]
    public void IntersectsToTheSmallerRectangleWhenItLiesInsideTheOther()
    {
        var outer = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var inner = new ExtentRectangle(MinX: 20, MinY: 30, MaxX: 40, MaxY: 50);

        Assert.Equal(inner, outer.Intersect(inner));
        Assert.Equal(inner, inner.Intersect(outer));
    }

    [Fact]
    public void HasNoIntersectionWithARectangleItDoesNotReach()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

        Assert.Null(first.Intersect(new ExtentRectangle(MinX: 101, MinY: 0, MaxX: 200, MaxY: 100)));
        Assert.Null(first.Intersect(new ExtentRectangle(MinX: 0, MinY: -50, MaxX: 100, MaxY: -1)));
    }

    [Fact]
    public void IntersectsRectanglesThatShareOnlyAnEdgeInALineOfNoWidth()
    {
        var first = new ExtentRectangle(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);
        var second = new ExtentRectangle(MinX: 100, MinY: 20, MaxX: 200, MaxY: 60);

        Assert.Equal(new ExtentRectangle(MinX: 100, MinY: 20, MaxX: 100, MaxY: 60), first.Intersect(second));
    }
```

Add to `MercatorProjectorTests`:

```csharp
    [Theory]
    [InlineData(42.3555, -71.0605)]
    [InlineData(-33.87, 151.21)]
    [InlineData(60.17, 24.94)]
    [InlineData(0, 0)]
    public void UnprojectsBackToWhereAPointCameFrom(double latitude, double longitude)
    {
        // Eleven decimal places of a degree is about a micrometre on the ground.
        GeoPoint returned = MercatorProjector.Unproject(MercatorProjector.Project(new GeoPoint(latitude, longitude)));

        Assert.Equal(latitude, returned.Latitude, precision: 11);
        Assert.Equal(longitude, returned.Longitude, precision: 11);
    }
```

- [x] **Step 2: Run to see them fail**

```bash
dotnet build tests/MetroDisplay.Spatial.Tests
```

Expected: error CS1061, `ExtentRectangle` has no definition for `Intersect`, and error CS0117,
`MercatorProjector` has no definition for `Unproject`.

- [x] **Step 3: Write `Intersect`**

In `ExtentRectangle.cs`, after the `Aspect` property:

```csharp
    /// <summary>
    /// The part of the plane both rectangles cover.
    /// </summary>
    /// <returns>
    /// The overlap, or <c>null</c> when the rectangles share no point. Rectangles that share
    /// only an edge or a corner overlap in a rectangle of no width or no height.
    /// </returns>
    public ExtentRectangle? Intersect(ExtentRectangle other)
    {
        double minX = Math.Max(MinX, other.MinX);
        double minY = Math.Max(MinY, other.MinY);
        double maxX = Math.Min(MaxX, other.MaxX);
        double maxY = Math.Min(MaxY, other.MaxY);

        if (minX > maxX || minY > maxY)
        {
            return null;
        }

        return new ExtentRectangle(minX, minY, maxX, maxY);
    }
```

- [x] **Step 4: Write `Unproject`**

In `MercatorProjector.cs`, after `Project`:

```csharp
    /// <summary>
    /// The inverse of <see cref="Project"/>: the position on the globe a plane point stands for.
    /// </summary>
    /// <param name="point">A point in Web Mercator plane units.</param>
    public static GeoPoint Unproject(PlanePoint point)
    {
        double longitude = point.X / EarthRadiusM * RadiansToDegrees;
        double latitude = (2.0 * Math.Atan(Math.Exp(point.Y / EarthRadiusM)) - Math.PI / 2.0) * RadiansToDegrees;
        return new GeoPoint(latitude, longitude);
    }
```

and beside the `DegreesToRadians` constant at the top of the class:

```csharp
    private const double RadiansToDegrees = 180.0 / Math.PI;
```

- [x] **Step 5: Run to see them pass**

```bash
dotnet test tests/MetroDisplay.Spatial.Tests
```

Expected: 30 passed.

- [x] **Step 6: Hand off**

```
feat: intersect rectangles and unproject plane points
```

---

### Task 3: The polyline clipper

**Files:**
- Create: `src/MetroDisplay.Spatial/PolylineClipper.cs`
- Test: `tests/MetroDisplay.Spatial.Tests/PolylineClipperTests.cs`

**Interfaces:**
- Consumes: `PlanePoint`, `ExtentRectangle`.
- Produces: `static IReadOnlyList<IReadOnlyList<PlanePoint>> PolylineClipper.Clip(IReadOnlyList<PlanePoint> points, ExtentRectangle rectangle)`.
  Each inner list is one run: a stretch of the polyline that lies inside the rectangle, in the
  polyline's own direction, with at least two points. Runs come in the order the polyline
  visits them. A polyline wholly outside gives an empty list. The rectangle's edges count as
  inside.

How it works. Each segment is cut with the Liang–Barsky method: the segment is written as
`start + fraction * (end - start)` for `fraction` from 0 to 1, and each of the four edges
narrows the range of fractions that lie inside. What is left is the entry fraction and the exit
fraction. A segment that enters at fraction 0 began inside, so it carries on the run the last
segment left open. Any other segment that reaches the rectangle starts a new run at its entry
point. A segment that exits before fraction 1 ends the run.

Two details keep the output honest:

- **Fractions 0 and 1 return the original vertex itself,** not `start + 1 * (end - start)`,
  which can differ in the last binary digit. That is what lets a line inside come back
  identical.
- **A segment that has length but meets the rectangle at a single point is treated as a
  miss.** That is a line grazing a corner, or leaving from a vertex that sits on the edge.
  A repeated point, a segment of no length, is kept when it is inside, because the source
  data has them and an untouched line must stay untouched.

- [x] **Step 1: Write the failing tests**

`tests/MetroDisplay.Spatial.Tests/PolylineClipperTests.cs`:

```csharp
using Xunit;

namespace MetroDisplay.Spatial.Tests;

public class PolylineClipperTests
{
    private static readonly ExtentRectangle Rectangle = new(MinX: 0, MinY: 0, MaxX: 100, MaxY: 100);

    private static List<PlanePoint> Line(params (double X, double Y)[] points) =>
        points.Select(point => new PlanePoint(point.X, point.Y)).ToList();

    [Fact]
    public void ReturnsALineInsideUntouched()
    {
        // The repeated first point is how real feeds arrive. It must survive.
        List<PlanePoint> line = Line((10, 10), (10, 10), (50, 60), (90, 20));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Fact]
    public void DrawsNothingForALineOutside()
    {
        Assert.Empty(PolylineClipper.Clip(Line((-50, 10), (-10, 60), (-20, 120)), Rectangle));
        // Parallel to an edge and one unit beyond it.
        Assert.Empty(PolylineClipper.Clip(Line((20, 101), (80, 101)), Rectangle));
    }

    [Fact]
    public void CutsALineWhereItLeaves()
    {
        // Rising one unit for every two across, the line meets the east edge at y = 75.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((50, 50), (150, 100)), Rectangle));

        Assert.Equal(Line((50, 50), (100, 75)), run);
    }

    [Fact]
    public void CutsALineWhereItEnters()
    {
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((150, 50), (50, 50)), Rectangle));

        Assert.Equal(Line((100, 50), (50, 50)), run);
    }

    [Fact]
    public void CutsBothEndsOfALineThatCrossesTheWholeRectangle()
    {
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((-50, 30), (150, 30)), Rectangle));

        Assert.Equal(Line((0, 30), (100, 30)), run);
    }

    [Fact]
    public void GivesOneRunPerVisit()
    {
        // Out through the east edge, back in, out through the north edge, back in.
        List<PlanePoint> line = Line((50, 10), (150, 10), (150, 50), (50, 50), (50, 150), (90, 150), (90, 90));

        IReadOnlyList<IReadOnlyList<PlanePoint>> runs = PolylineClipper.Clip(line, Rectangle);

        Assert.Equal(3, runs.Count);
        Assert.Equal(Line((50, 10), (100, 10)), runs[0]);
        Assert.Equal(Line((100, 50), (50, 50), (50, 100)), runs[1]);
        Assert.Equal(Line((90, 100), (90, 90)), runs[2]);
    }

    [Fact]
    public void KeepsASegmentThatCrossesWithBothEndsOutside()
    {
        // Neither end is inside, but the segment cuts across the north-east corner.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((60, 120), (120, 60)), Rectangle));

        Assert.Equal(Line((80, 100), (100, 80)), run);
    }

    [Fact]
    public void DrawsNothingForALineThatOnlyTouchesACorner()
    {
        // Passes through (100, 100) and nowhere else in the rectangle.
        Assert.Empty(PolylineClipper.Clip(Line((90, 110), (110, 90)), Rectangle));
    }

    [Fact]
    public void KeepsOneRunWhenALineTurnsBackAtTheEdge()
    {
        List<PlanePoint> line = Line((50, 50), (100, 50), (50, 80));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Fact]
    public void EndsTheRunAtAVertexOnTheEdge()
    {
        // The second point sits exactly on the east edge and the line carries on outside.
        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(Line((50, 50), (100, 50), (150, 50)), Rectangle));

        Assert.Equal(Line((50, 50), (100, 50)), run);
    }

    [Fact]
    public void KeepsALineThatRunsAlongTheEdge()
    {
        List<PlanePoint> line = Line((20, 100), (80, 100));

        IReadOnlyList<PlanePoint> run = Assert.Single(PolylineClipper.Clip(line, Rectangle));

        Assert.Equal(line, run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DrawsNothingForFewerThanTwoPoints(int pointCount)
    {
        List<PlanePoint> points = Enumerable.Repeat(new PlanePoint(10, 10), pointCount).ToList();

        Assert.Empty(PolylineClipper.Clip(points, Rectangle));
    }
}
```

- [x] **Step 2: Run to see them fail**

```bash
dotnet build tests/MetroDisplay.Spatial.Tests
```

Expected: error CS0103, the name `PolylineClipper` does not exist.

- [x] **Step 3: Write the clipper**

`src/MetroDisplay.Spatial/PolylineClipper.cs`:

```csharp
namespace MetroDisplay.Spatial;

/// <summary>
/// Cuts a polyline down to the parts that lie inside a rectangle.
/// </summary>
public static class PolylineClipper
{
    /// <summary>
    /// Cuts mid-segment wherever the polyline crosses the rectangle's edge. The edge itself
    /// counts as inside.
    /// </summary>
    /// <param name="points">The polyline, in order.</param>
    /// <param name="rectangle">The area to keep.</param>
    /// <returns>
    /// One run per visit to the rectangle, in the order the polyline makes them, each with at
    /// least two points. Empty when the polyline never enters. A polyline wholly inside comes
    /// back as one run holding its own points, unchanged.
    /// </returns>
    public static IReadOnlyList<IReadOnlyList<PlanePoint>> Clip(IReadOnlyList<PlanePoint> points, ExtentRectangle rectangle)
    {
        var runs = new List<IReadOnlyList<PlanePoint>>();
        List<PlanePoint>? openRun = null;

        for (int index = 1; index < points.Count; index++)
        {
            PlanePoint start = points[index - 1];
            PlanePoint end = points[index];

            if (!TryClipSegment(start, end, rectangle, out double entryFraction, out double exitFraction))
            {
                openRun = null;
                continue;
            }

            // A segment that begins inside carries on the run the last one left open.
            if (openRun is null || entryFraction > 0)
            {
                openRun = [Along(start, end, entryFraction)];
                runs.Add(openRun);
            }
            openRun.Add(Along(start, end, exitFraction));

            if (exitFraction < 1)
            {
                openRun = null;
            }
        }

        return runs;
    }

    /// <summary>
    /// The point a fraction of the way from start to end. The ends themselves are returned as
    /// they are, so a vertex that is kept is never moved by rounding.
    /// </summary>
    private static PlanePoint Along(PlanePoint start, PlanePoint end, double fraction)
    {
        if (fraction == 0)
        {
            return start;
        }
        if (fraction == 1)
        {
            return end;
        }
        return new PlanePoint(start.X + fraction * (end.X - start.X), start.Y + fraction * (end.Y - start.Y));
    }

    /// <summary>
    /// Liang–Barsky: each edge narrows the range of fractions along the segment that lie
    /// inside the rectangle.
    /// </summary>
    /// <returns>False when no part of the segment is worth drawing.</returns>
    private static bool TryClipSegment(PlanePoint start, PlanePoint end, ExtentRectangle rectangle, out double entryFraction, out double exitFraction)
    {
        entryFraction = 0;
        exitFraction = 1;
        double changeX = end.X - start.X;
        double changeY = end.Y - start.Y;

        bool reaches =
            Narrow(-changeX, start.X - rectangle.MinX, ref entryFraction, ref exitFraction) &&
            Narrow(changeX, rectangle.MaxX - start.X, ref entryFraction, ref exitFraction) &&
            Narrow(-changeY, start.Y - rectangle.MinY, ref entryFraction, ref exitFraction) &&
            Narrow(changeY, rectangle.MaxY - start.Y, ref entryFraction, ref exitFraction);
        if (!reaches)
        {
            return false;
        }

        // A segment with length that meets the rectangle at one point only grazes it. A
        // repeated point has no length and is kept, so a line inside comes back unchanged.
        bool hasLength = changeX != 0 || changeY != 0;
        return !(hasLength && entryFraction == exitFraction);
    }

    /// <summary>
    /// Applies one edge. <paramref name="direction"/> is how fast the segment moves toward
    /// the outside of that edge, and <paramref name="distance"/> how far inside it starts.
    /// </summary>
    /// <returns>False when the edge puts the whole segment outside.</returns>
    private static bool Narrow(double direction, double distance, ref double entryFraction, ref double exitFraction)
    {
        // Parallel to the edge: inside or outside it for the segment's whole length.
        if (direction == 0)
        {
            return distance >= 0;
        }

        double fraction = distance / direction;
        if (direction < 0)
        {
            // Moving inward across this edge: the crossing is where the segment enters.
            if (fraction > exitFraction)
            {
                return false;
            }
            entryFraction = Math.Max(entryFraction, fraction);
        }
        else
        {
            // Moving outward across this edge: the crossing is where the segment exits.
            if (fraction < entryFraction)
            {
                return false;
            }
            exitFraction = Math.Min(exitFraction, fraction);
        }
        return true;
    }
}
```

- [x] **Step 4: Run to see them pass**

```bash
dotnet test tests/MetroDisplay.Spatial.Tests
```

Expected: 43 passed.

- [x] **Step 5: Hand off**

```
feat: clip polylines to a rectangle

Cuts mid-segment and returns one run per visit, so a line that
leaves the map and comes back is never joined across the outside.
```

---

### Task 4: Frame the rail on its extent and clip to it

**Files:**
- Modify: `src/MetroDisplay.Gtfs.Static/Pipeline/NetworkSceneBuilder.cs`
- Test: `tests/MetroDisplay.Gtfs.Static.Tests/NetworkSceneBuilderTests.cs`
- Test: `tests/MetroDisplay.Server.Tests/NetworkEndpointTests.cs`

**Interfaces:**
- Consumes: `GeoBox.Around(GeoPoint, double).ToPlane()` (Task 1),
  `ExtentRectangle.Intersect` and `MercatorProjector.Unproject` (Task 2),
  `PolylineClipper.Clip` (Task 3).
- Produces: no new names. `NetworkSceneBuilder.BuildLayer` keeps its signature.
  `RailLayer.Bounds` is now the extent. `BuildLayer` and `Build` gain one failure:
  `InvalidOperationException` when no rail lies inside the core square.

What changes in the output:

- `extent.aspect` and `extent.spanKm` describe the extent, not the rail's full bounds.
- A shape cut by the extent is sent as its runs. The first run keeps the shape's id; later
  runs carry `#1`, `#2`.
- `lengthM` is the ground length of the run, measured between the unprojected run points.
- A shape with no run is not sent. A route left with no shape is left off, by the rule
  already in `BuildLines`.

The extent is the content's bounding box intersected with the core square, as spec §5 defines
it. That is not always the bounding box of what survives the clip: content far outside can
widen the box on one axis without putting any rail in the part that is kept. So two checks
guard the error. The intersection can be empty, and it can be non-empty with no rail in it.

The error is an `InvalidOperationException`, not the `InvalidDataException` the feed checks
throw. `Program.cs` catches `InvalidDataException` and adds advice to delete the cached feed,
which would be wrong here: the feed is fine and the config is not.

- [x] **Step 1: Save today's output for Task 5**

Before any change in this task, with the working tree as Task 3 left it:

```bash
mkdir -p .superpowers/sdd/2026-10-11-slice-03-extent-and-clip
dotnet run --project src/MetroDisplay.Server
```

Wait for `Now listening on: http://localhost:5180`, then in a second shell:

```bash
curl -s http://localhost:5180/api/network -o .superpowers/sdd/2026-10-11-slice-03-extent-and-clip/network-before.json
```

Stop the Server with Ctrl+C. `.superpowers/` is gitignored, so the file stays out of the commit.

- [x] **Step 2: Write the failing builder tests**

Add `using System.Globalization;` to the usings of `NetworkSceneBuilderTests.cs`, and these
tests to the class:

```csharp
    [Fact]
    public void CutsShapesAtTheEdgeOfTheCoreSquare()
    {
        // 2 km around 42.34 N 71.08 W reaches 71.104334 W and 71.055666 W. Green runs from
        // 71.12 W to 71.06 W, so it is cut at the west edge and ends short of the east one.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([42.34, -71.08], CoreRadiusKm: 2) };

        NetworkScene scene = NetworkSceneBuilder.Build(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), config);

        ShapeGeometry green = scene.Lines.Single(line => line.Id == "Green").Shapes.Single();
        Assert.Equal("shape-green", green.Id);
        Assert.Equal(new[] { 0, 0.193, 0.2945, 0.1665, 0.911, 0.1108 }, green.Points);
        // The whole shape is 4950.5 m. What is drawn is shorter.
        Assert.Equal(3657.9, green.LengthM);
        // The frame is the square's 4 km top to bottom, and the rail's own width across.
        Assert.Equal(new ExtentInfo(Aspect: 0.911, CoreRadiusKm: 2, SpanKm: 4), scene.Extent);
    }

    [Fact]
    public void LeavesOffALineWithNothingInsideTheSquare()
    {
        // 1 km around the core reaches 42.348993 N. Green runs along 42.35 N and above.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([42.34, -71.08], CoreRadiusKm: 1) };

        RailLayer layer = NetworkSceneBuilder.BuildLayer(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), config);

        Assert.Equal(new[] { "Red" }, layer.Scene.Lines.Select(line => line.Id));
        // The rail runs past the square on every side, so the frame is the square itself.
        Assert.Equal(GeoBox.Around(new GeoPoint(42.34, -71.08), radiusKm: 1).ToPlane(), layer.Bounds);
        Assert.Equal(1, layer.Scene.Extent.Aspect);
        Assert.Equal(2, layer.Scene.Extent.SpanKm);
    }

    [Fact]
    public void SplitsAShapeThatLeavesAndReturnsIntoNumberedRuns()
    {
        // shape-red heads east along 42.34 N, leaves the 2 km square at 71.055666 W, turns
        // north outside it and comes back west along 42.35 N.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([42.34, -71.08], CoreRadiusKm: 2) };
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3400,-71.0900,1
            shape-red,42.3400,-71.0400,2
            shape-red,42.3500,-71.0400,3
            shape-red,42.3500,-71.0900,4
            shape-red-rev,42.3400,-71.0900,1
            shape-red-rev,42.3400,-71.0800,2
            shape-green,42.3500,-71.1200,1
            shape-green,42.3520,-71.1100,2
            """);

        NetworkScene scene = NetworkSceneBuilder.Build(fixture.Build(), config);

        // Green lies wholly west of the square, so only Red is drawn.
        LineScene red = Assert.Single(scene.Lines);
        Assert.Equal(new[] { "shape-red", "shape-red#1", "shape-red-rev" }, red.Shapes.Select(shape => shape.Id));
        Assert.Equal(new[] { 0.2945, 0.3336, 1, 0.3336 }, red.Shapes[0].Points);
        Assert.Equal(new[] { 1, 0.0556, 0.2945, 0.0556 }, red.Shapes[1].Points);
        // Each run is the 2.82 km from 71.09 W to the square's east edge, not the shape's whole length.
        Assert.Equal(2821.9, red.Shapes[0].LengthM);
        Assert.Equal(2821.5, red.Shapes[1].LengthM);
    }

    [Fact]
    public void RefusesACityWithNoRailNearItsCore()
    {
        // A typo in the core's latitude puts the square 130 km north of the rail.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([43.5, -71.08], CoreRadiusKm: 22) };
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // A culture that writes decimals with a comma must not reach the message.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var exception = Assert.Throws<InvalidOperationException>(
                () => NetworkSceneBuilder.Build(GtfsFixtureBuilder.TwoRailLinesAndABus().Build(), config));

            Assert.Contains("'test'", exception.Message);
            Assert.Contains("within 22 km", exception.Message);
            Assert.Contains("(43.5, -71.08)", exception.Message);
            Assert.Contains("extent.core", exception.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RefusesACityWhoseRailOnlyPassesAroundItsSquare()
    {
        // One line runs north along 71.20 W and one east along 42.40 N. Their bounding box
        // covers the 2 km square around the core, but neither line enters it.
        CityConfig config = TestConfig() with { Extent = new ExtentConfig([42.34, -71.08], CoreRadiusKm: 2) };
        GtfsFixtureBuilder fixture = GtfsFixtureBuilder.TwoRailLinesAndABus().WithFile("shapes.txt", """
            shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
            shape-red,42.3000,-71.2000,1
            shape-red,42.4000,-71.2000,2
            shape-green,42.4000,-71.2000,1
            shape-green,42.4000,-71.0000,2
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => NetworkSceneBuilder.Build(fixture.Build(), config));

        Assert.Contains("within 2 km", exception.Message);
    }
```

- [x] **Step 3: Run to see them fail**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests --filter "FullyQualifiedName~NetworkSceneBuilderTests"
```

Expected: the five new tests fail. `CutsShapesAtTheEdgeOfTheCoreSquare` reports six points
that differ and an aspect of 1.1087; the two `Refuses` tests report that no exception was
thrown. The existing tests pass.

- [x] **Step 4: Change the builder**

In `NetworkSceneBuilder.cs`:

Add to the usings:

```csharp
using System.Globalization;
```

Replace the `<remarks>` on the class:

```csharp
/// <remarks>
/// Lines only, framed on the extent of spec §5 and cut at its edge. Edge labels, stations and
/// simplification each arrive in a later slice (spec §14).
/// </remarks>
```

Update the doc comment on `RailLayer`'s `Bounds`:

```csharp
/// <param name="Bounds">The extent: the plane rectangle every normalized coordinate in <paramref name="Scene"/> is relative to. Other layers normalize with it so they line up with the rail.</param>
```

Add a second `<exception>` line to both `Build` and `BuildLayer`:

```csharp
    /// <exception cref="InvalidOperationException">No rail lies inside the city's core square: the config's core point or radius is wrong.</exception>
```

In `BuildLayer`, replace everything from the comment `// create a bounding box that tightly
fits all shapes combined` down to the end of the method with:

```csharp
        // create a bounding box that tightly fits all shapes combined
        ExtentRectangle content = ExtentRectangle.Enclosing(planePointsByShapeId.Values.SelectMany(points => points));

        // The extent is that box cut down to the core square, so the map's scale is set by
        // real distance from the core and not by how far the rail happens to run.
        GeoPoint core = new(config.Extent.Core[0], config.Extent.Core[1]);
        ExtentRectangle coreSquare = GeoBox.Around(core, config.Extent.CoreRadiusKm).ToPlane();
        ExtentRectangle extent = content.Intersect(coreSquare) ?? throw NoRailInTheCoreSquare(config);

        // The box can overlap the square without any rail running through the overlap.
        List<LineScene> lines = BuildLines(selection, planePointsByShapeId, extent);
        if (lines.Count == 0)
        {
            throw NoRailInTheCoreSquare(config);
        }

        // Mercator stretches based on latitude, and using the extent's middle latitude minimizes the error
        double southLatitude = MercatorProjector.Unproject(new PlanePoint(extent.MinX, extent.MinY)).Latitude;
        double northLatitude = MercatorProjector.Unproject(new PlanePoint(extent.MinX, extent.MaxY)).Latitude;
        double centerLatitude = (southLatitude + northLatitude) / 2.0;

        // create a new extend window
        ExtentInfo extentInfo = new ExtentInfo(
            Aspect: Math.Round(extent.Aspect, 4),
            CoreRadiusKm: config.Extent.CoreRadiusKm,
            SpanKm: Math.Round(MercatorProjector.PlaneMetresToGround(extent.LongestSpan, centerLatitude) / 1000.0, 2));

        NetworkScene scene = new(
            ArtifactVersion: ContentVersion(config.Id, zipBytes),
            City: new CityMetadata(config.Id, config.Name, config.Agency, config.Timezone),
            Extent: extentInfo,
            Lines: lines,
            Stations: [],
            EdgeLabels: [],
            Water: []);

        return new RailLayer(scene, extent);
    }
```

Replace `BuildLines` with:

```csharp
    private static List<LineScene> BuildLines(RailSelection selection, Dictionary<string, List<PlanePoint>> planePointsByShapeId, ExtentRectangle extent)
    {
        // Trips whose shape is not in shapes.txt drop out here
        // Create a Lookup linking each routeId to all valid shapeIds associated with its trips
        ILookup<string, string> shapeIdsByRouteId = selection.Trips
            .Where(trip => planePointsByShapeId.ContainsKey(trip.ShapeId))
            .ToLookup(trip => trip.RouteId, trip => trip.ShapeId, StringComparer.Ordinal);

        List<LineScene> lines = new();
        foreach (GtfsRoute route in selection.Routes.OrderBy(route => route.RouteId, StringComparer.Ordinal))
        {
            List<ShapeGeometry> shapes = shapeIdsByRouteId[route.RouteId] // pull all shapeIds linked to this route
                .Distinct(StringComparer.Ordinal) // Remove duplicate shapeIds
                .Order(StringComparer.Ordinal) // Sort them alphbetically
                .SelectMany(shapeId => ClipToRuns(shapeId, planePointsByShapeId[shapeId], extent)) // cut each shape to the extent; a shape outside it gives nothing
                .ToList();

            // A route listed in routes.txt with nothing to draw, such as a seasonal route or
            // one that lies outside the extent, is left off the map instead of being sent as
            // an empty line.
            if (shapes.Count == 0)
            {
                continue;
            }

            lines.Add(new LineScene(route.RouteId, DisplayName(route), FormatColor(route.RouteColor), shapes));
        }

        // return LineScene objects for rendering
        return lines;
    }

    /// <summary>
    /// The parts of one shape that lie inside the extent, each as its own polyline. The first
    /// keeps the shape's id; a shape that leaves and returns gives further runs, numbered
    /// <c>#1</c>, <c>#2</c> in the order the shape visits them.
    /// </summary>
    private static IEnumerable<ShapeGeometry> ClipToRuns(string shapeId, IReadOnlyList<PlanePoint> points, ExtentRectangle extent)
        => PolylineClipper.Clip(points, extent).Select((run, runIndex) => new ShapeGeometry(
            runIndex == 0 ? shapeId : $"{shapeId}#{runIndex}",
            CoordinateNormalizer.Flatten(run, extent), // scale the points relative to the extent
            Math.Round(GroundDistance.PolylineLengthM(run.Select(MercatorProjector.Unproject).ToList()), 1))); // the real world length of what is drawn, rounded to 1 decimal place

    /// <summary>
    /// The feed is sound and the config is not, so this is not the feed's exception type:
    /// the Server answers a bad feed by naming the cached file to delete.
    /// </summary>
    private static InvalidOperationException NoRailInTheCoreSquare(CityConfig config)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"No rail for '{config.Id}' lies within {config.Extent.CoreRadiusKm} km of its core point ({config.Extent.Core[0]}, {config.Extent.Core[1]}). Check extent.core and extent.coreRadiusKm in the city's config."));
```

`geoPointsByShapeId` is still built and still feeds the "no rail shapes" check and the
projection. The `latitudes` list and the old `bounds` variable are gone.

- [x] **Step 5: Run the builder tests**

```bash
dotnet test tests/MetroDisplay.Gtfs.Static.Tests
```

Expected: 42 passed. The tests that predate this task pass unchanged: their fixture sits
inside the 22 km square, so nothing is cut and the frame is the rail's own bounds.

- [x] **Step 6: Write the failing Server test**

Add to `NetworkEndpointTests`:

```csharp
    [Fact]
    public void NamesTheCoreWhenNoRailIsInsideTheSquare()
    {
        // The feed is fine; the config points 130 km away. Advice to delete the cached feed
        // would send the reader after the wrong file.
        string misplacedCityJson = TestCityJson.Replace("\"core\": [42.34, -71.08]", "\"core\": [43.5, -71.08]");
        Assert.Contains("43.5", misplacedCityJson);
        using WebApplicationFactory<Program> factory = CreateFactory("test", misplacedCityJson);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("core point (43.5, -71.08)", exception.ToString());
        Assert.DoesNotContain("Delete it to download the feed again", exception.ToString());
    }
```

- [x] **Step 7: Run it**

```bash
dotnet test tests/MetroDisplay.Server.Tests --filter "FullyQualifiedName~NamesTheCoreWhenNoRailIsInsideTheSquare"
```

Expected: PASS. `Program.cs` needs no change, because its `catch` is for
`InvalidDataException` only. The test is the guard on that: change the builder's exception to
`InvalidDataException` for a moment and watch the second assertion fail, then change it back.

- [x] **Step 8: Run everything**

```bash
dotnet test MetroDisplay.slnx
```

Expected: 146 passed (Spatial 43, Gtfs.Static 42, Osm 40, Server 21), 0 warnings.

- [x] **Step 9: Hand off**

```
feat: frame the rail on its extent and clip to it

The extent is the rail's bounds cut to the square reaching
coreRadiusKm from the core. Shapes are cut at its edge; one that
leaves and returns becomes numbered runs. A city with no rail in
its square stops startup and names the core point.
```

---

### Task 5: Check against the real feed

**Files:**
- Modify, then restore: `cities/mbta.json`
- Modify: `docs/plans/2026-10-11-slice-03-extent-and-clip.md` (tick the boxes)

No code changes here. Tests are blind to the two things this task looks at: that Boston as
configured is untouched, and that a network larger than its square is cut where it should be.

- [x] **Step 1: Boston as configured comes out the same**

```bash
dotnet run --project src/MetroDisplay.Server
```

Expected in the log: `Built mbta@<version>: 8 lines, 67 shapes, 254 water areas`, the same
counts as before this slice. In a second shell:

```bash
curl -s http://localhost:5180/api/network -o .superpowers/sdd/2026-10-11-slice-03-extent-and-clip/network-after.json
cmp .superpowers/sdd/2026-10-11-slice-03-extent-and-clip/network-before.json .superpowers/sdd/2026-10-11-slice-03-extent-and-clip/network-after.json && echo identical
```

Expected: `identical`. Boston's subway and light rail lie inside the 22 km square, so the
extent is the rail's own bounds as before, and water is normalized in the same frame.
If the files differ, stop: find the first difference before going on. Stop the Server.

- [x] **Step 2: Switch commuter rail on, locally**

In `cities/mbta.json` change `"routeTypes": [0, 1]` to `"routeTypes": [0, 1, 2]`. Start the
Server and `npm run dev` in `web/`, then open `http://localhost:5173`.

Expected in the log: `22 lines, 148 shapes`. Expected on screen:

- The map is square: 44 km across and 44 km top to bottom.
- Commuter lines run out from the centre and stop dead at the square's edge, on all four sides.
- The subway is smaller than before, in the middle, the same shape as before.
- Water fills the same square and no longer runs past the rail's frame. On a screen wider
  than it is tall there are empty bands left and right. That is the known limit in spec §14.

```bash
curl -s http://localhost:5180/api/network | grep -o '"extent":{[^}]*}'
```

Expected: `"extent":{"aspect":1,"coreRadiusKm":22,"spanKm":44.05}`. The span reads 44.05
and not 44 because the square is sized with the mean Earth radius and projected with the
equatorial one, 0.11% larger.

- [x] **Step 3: Switch it back**

Restore `"routeTypes": [0, 1]` in `cities/mbta.json`, stop the Server, then:

```bash
git status --short cities/
```

Expected: no output. Commuter rail stays out of the committed config.

- [x] **Step 4: Tick the plan and hand off**

Tick every box in this plan. The commit carries the plan only:

```
docs: record slice 3 as built
```

---

## Done when

- `dotnet test MetroDisplay.slnx` passes with 146 tests and no warnings.
- `GET /api/network` for Boston as configured is byte-identical to its output before this slice.
- With `routeTypes` `[0, 1, 2]` set locally, commuter rail is visibly cut at the edge of a
  44 km square, and `cities/mbta.json` is back to `[0, 1]` afterwards.

## Carried into later slices

- **Water past a full square.** When the rail fills its square, water has nowhere to run on
  to and a wide screen shows empty bands. Fetching water for a wider box is a decision for
  when that case is a city on screen (spec §14).
- **Where a run starts along its shape.** Vehicle positions (slice 5) place a vehicle by its
  distance along the original shape. A cut run needs the distance at which it begins.
  `ClipToRuns` is where that is known.
- **Edge labels (slice 7)** sit at the cut points. The clipper knows which run ends were cut
  and which are the shape's own ends; today it does not say.
- **The span reads 44.05 km for a 44 km square.** `GeoBox` uses the mean Earth radius and the
  projection the equatorial one. Making them agree changes the water query, and so refetches
  the layer.
- **Two routes that share a shape clip it twice.** Harmless at Boston's size. Slice 9's shape
  dedupe removes the repeat.
