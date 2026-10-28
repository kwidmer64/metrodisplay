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
