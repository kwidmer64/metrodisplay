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
