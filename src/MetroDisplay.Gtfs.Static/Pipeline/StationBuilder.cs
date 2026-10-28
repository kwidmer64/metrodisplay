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

        // Init two dictionaries to track the first and last calls (or stops) for each trip
        // Key is the trip ID, value is a Tuple with the sequence and the Station ID
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

            // Map the station Ids to a unique set of colors
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

        // Selects the station id from the first call and last call, resulting in a hashset of terminus station ids
        // reminder: hashsets automatically handle deduplication
        HashSet<string> terminusIds = firstCallByTripId.Values
            .Concat(lastCallByTripId.Values)
            .Select(call => call.StationId)
            .ToHashSet(StringComparer.Ordinal);

        List<Station> stations = new();
        foreach ((string stationId, HashSet<string> colors) in colorsByStationId.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            GtfsStop station = stopsById[stationId];

            // skip station if long or lat is not a double
            if (station.Latitude is not double latitude || station.Longitude is not double longitude)
            {
                continue;
            }

            // set the rank
            int rank;
            if (colors.Count >= 2) // stations with more than 2 lines have the most importance
            {
                rank = InterchangeRank;
            } else if (terminusIds.Contains(stationId)) // if station is a terminus
            {
                rank = TerminusRank;
            } else
            {
                rank = OtherRank;
            }

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
