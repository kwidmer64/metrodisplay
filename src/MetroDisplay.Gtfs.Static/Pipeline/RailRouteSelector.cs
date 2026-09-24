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
/// Narrows the agency feed to the rail routes this city will draw, and to the shapes those
/// routes actually use. Everything downstream works on this subset.
/// </summary>
public static class RailRouteSelector
{
    /// <summary>
    /// Selects the specific routes, trips, and shape IDs from the entire agency feed, and filters routes based on the input filter.
    /// </summary>
    /// <param name="archive">The whole feed.</param>
    /// <param name="routeTypes">GTFS <c>route_type</c> values to keep.</param>
    /// <param name="filter">Optional include or exclude list by <c>route_id</c>. When both are set, include wins.</param>
    public static RailSelection Select(GtfsArchive archive, IReadOnlyList<int> routeTypes, RouteFilter? filter)
    {
        // Convert routeTypes to a HashSet
        HashSet<int> wantedTypes = routeTypes.ToHashSet();

        // Filter the Routes of the GtfsArchive to routes that contain the specified route type
        IEnumerable<GtfsRoute> routeCandidates = archive.Routes.Where(route => wantedTypes.Contains(route.RouteType));

        // Check that include contains at least 1 item, set filter.Include to local variable 'include'
        if (filter?.Include is { Count: > 0 } include)
        {
            // Convert the 'include' list to a HashSet, making sure future comparison/generated hashes are done Ordinally
            HashSet<string> included = include.ToHashSet(StringComparer.Ordinal);

            // Filter the routes list only to routes that are in the included list
            routeCandidates = routeCandidates.Where(route => included.Contains(route.RouteId));
        }
        else if (filter?.Exclude is { Count: > 0 } exclude) // Same logic as the Include block, but for the Exclude list
        {
            var excluded = exclude.ToHashSet(StringComparer.Ordinal);
            routeCandidates = routeCandidates.Where(route => !excluded.Contains(route.RouteId));
        }

        // Convert the routeCandidates to a list, and cast to a HashSet
        List<GtfsRoute> routes = routeCandidates.ToList();
        HashSet<string> routeIds = routes.Select(route => route.RouteId).ToHashSet(StringComparer.Ordinal);

        // Get the trips that correspond to a route, convert to list
        List<GtfsTrip> trips = archive.Trips.Where(trip => routeIds.Contains(trip.RouteId)).ToList();

        // Get the shapeIds for each trip, convert to HashSet
        HashSet<string> shapeIds = trips
            .Select(trip => trip.ShapeId)
            .Where(shapeId => !string.IsNullOrEmpty(shapeId))
            .ToHashSet(StringComparer.Ordinal);

        return new RailSelection(routes, trips, shapeIds);
    }
}
