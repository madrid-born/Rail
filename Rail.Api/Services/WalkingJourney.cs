namespace Rail.Api.Services;

public sealed record WalkingAccess(string StationId, double DistanceMeters, int Seconds, StreetPath? Path = null);

public static class WalkingJourney
{
    // Walking estimates, not surveyed pedestrian directions: allow for street detours,
    // then add time to reach the platform. Keep these assumptions in the response.
    public const double DetourFactor = 1.3;
    public const double MetersPerSecond = 1.25;
    public const int PlatformSeconds = 120;

    public static double Distance(double fromLat, double fromLng, double toLat, double toLng)
    {
        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((toLat - fromLat) * radians / 2), 2)
            + Math.Cos(fromLat * radians) * Math.Cos(toLat * radians)
            * Math.Pow(Math.Sin((toLng - fromLng) * radians / 2), 2);
        return 12_742_000 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))) * DetourFactor;
    }

    public static WalkingAccess Access(string stationId, double lat, double lng, double stationLat, double stationLng)
    {
        var distance = Distance(lat, lng, stationLat, stationLng);
        return new(stationId, Math.Round(distance), (int)Math.Ceiling(distance / MetersPerSecond) + PlatformSeconds);
    }

    public static HashSet<string> Connected(IReadOnlyDictionary<string, HashSet<string>> graph,
        IEnumerable<string> starts, bool depthFirst)
    {
        var visited = new HashSet<string>();
        var pending = new LinkedList<string>(starts);
        while (pending.Count > 0)
        {
            var station = depthFirst ? pending.Last!.Value : pending.First!.Value;
            if (depthFirst) pending.RemoveLast(); else pending.RemoveFirst();
            if (!visited.Add(station) || !graph.TryGetValue(station, out var neighbors)) continue;
            foreach (var neighbor in neighbors)
                if (!visited.Contains(neighbor)) pending.AddLast(neighbor);
        }
        return visited;
    }

    public static (WalkingAccess[] Origins, WalkingAccess[] Destinations) ConnectCandidates(
        IReadOnlyDictionary<string, HashSet<string>> graph, WalkingAccess[] origins, WalkingAccess[] destinations)
    {
        // BFS and DFS from both ends rule out disconnected candidates. A timetable
        // search then compares feasible journeys by door-to-door arrival, not hop count.
        var forwardBfs = Connected(graph, origins.Select(item => item.StationId), false);
        var forwardDfs = Connected(graph, origins.Select(item => item.StationId), true);
        var reverseBfs = Connected(graph, destinations.Select(item => item.StationId), false);
        var reverseDfs = Connected(graph, destinations.Select(item => item.StationId), true);
        return (origins.Where(item => reverseBfs.Contains(item.StationId) && reverseDfs.Contains(item.StationId)).ToArray(),
            destinations.Where(item => forwardBfs.Contains(item.StationId) && forwardDfs.Contains(item.StationId)).ToArray());
    }
}
