using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/navigation")]
public sealed class NavigationController(AppDbContext db, JourneyPlanner planner, TimeProvider clock,
    StreetRouting? streetRouting = null, HolidayCalendar? holidays = null) : ControllerBase
{
    [HttpGet("walking-journeys")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<WalkingJourneyResult>> FindWalkingJourney(
        [FromQuery, Required, Range(-90d, 90d)] double? fromLatitude,
        [FromQuery, Required, Range(-180d, 180d)] double? fromLongitude,
        [FromQuery, Required, Range(-90d, 90d)] double? toLatitude,
        [FromQuery, Required, Range(-180d, 180d)] double? toLongitude,
        [FromQuery, Range(1, 90)] int maxWalkMinutes = 30,
        CancellationToken cancellationToken = default,
        [FromQuery, StringLength(64)] string? dateTime = null,
        [FromQuery, RegularExpression("^(departAt|arriveBy)$")] string timeMode = "departAt",
        [FromQuery, RegularExpression("^(walk|car|motorcycle)$")] string accessMode = "walk",
        [FromQuery, RegularExpression("^(walk|car|motorcycle)$")] string exitMode = "walk",
        [FromQuery, MaxLength(8)] string[]? viaStationIds = null)
    {
        viaStationIds = (viaStationIds ?? []).Select(id => id.Trim()).Distinct().ToArray();
        if (viaStationIds.Length > 8 || viaStationIds.Any(id => string.IsNullOrEmpty(id) || id.Length > 100))
            return BadRequest(new ProblemDetails { Detail = "Choose up to eight valid required stations." });
        const int transferMinutes = 2;
        var now = TehranTime.Now(clock);
        var requested = now;
        if (!string.IsNullOrWhiteSpace(dateTime))
        {
            if (!TehranTime.TryParse(dateTime, out var at))
                return BadRequest(new ProblemDetails { Detail = "Use a valid ISO date and time in Asia/Tehran." });
            requested = at.DateTime;
        }
        if (requested.Year is < 2000 or > 2100)
            return BadRequest(new ProblemDetails { Detail = "The selected date is outside the supported range." });
        var arriveBy = timeMode == "arriveBy";
        var start = arriveBy ? requested.AddHours(-24) : requested;
        var end = arriveBy ? requested : requested.AddHours(24);
        var serviceDays = new Dictionary<DateOnly, string>();
        if (accessMode != "walk" || exitMode != "walk")
        {
            var first = DateOnly.FromDateTime(start).AddDays(-1);
            var last = DateOnly.FromDateTime(end).AddDays(1);
            var overrides = await db.ServiceCalendarDates.AsNoTracking()
                .Where(d => d.ServiceDate >= first && d.ServiceDate <= last)
                .ToDictionaryAsync(d => d.ServiceDate, d => d.DayTypeCode, cancellationToken);
            try
            {
                for (var day = first; day <= last; day = day.AddDays(1))
                    serviceDays[day] = overrides.TryGetValue(day, out var code) ? code
                        : holidays is null ? ServiceDayType.DefaultCodeFor(day)
                        : (await holidays.GetDayAsync(day, cancellationToken)).DayTypeCode;
            }
            catch (HolidayCalendarException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ProblemDetails { Detail = "The holiday calendar could not verify the traffic day type. Try again later." });
            }
        }
        var traffic = new TrafficEstimate(day => serviceDays.GetValueOrDefault(day) ?? ServiceDayType.DefaultCodeFor(day));
        var stations = await db.Stations.AsNoTracking().Where(station => station.InfrastructureStatus == "operational"
                && station.Lines.Any(line => line.IsOperational)
                && (station.Latitude != 0 || station.Longitude != 0))
            .Select(station => new { station.Id, station.Latitude, station.Longitude,
                Lines = station.Lines.Where(line => line.IsOperational).Select(line => line.LineId).ToArray() }).ToArrayAsync(cancellationToken);
        var stops = await db.RouteStops.AsNoTracking()
            .Where(stop => stop.Status == "operational" && stop.Station.InfrastructureStatus == "operational"
                && stop.Station.Lines.Any(line => line.IsOperational && line.LineId == stop.Route.LineId))
            .Select(stop => new { stop.RouteId, stop.Sequence, stop.StationId }).ToArrayAsync(cancellationToken);
        var graph = new Dictionary<string, HashSet<string>>();
        foreach (var route in stops.GroupBy(stop => stop.RouteId))
        {
            var ordered = route.OrderBy(stop => stop.Sequence).ToArray();
            foreach (var stop in ordered) graph.TryAdd(stop.StationId, []);
            foreach (var pair in ordered.Zip(ordered.Skip(1)))
            {
                graph[pair.First.StationId].Add(pair.Second.StationId);
                graph[pair.Second.StationId].Add(pair.First.StationId);
            }
        }
        if (viaStationIds.Any(id => !graph.ContainsKey(id)))
            return BadRequest(new ProblemDetails { Detail = "A required station is unavailable or was not found." });
        async Task<WalkingAccess[]> Candidates(double lat, double lng, string mode, bool exiting)
        {
            var ordered = stations.Where(station => graph.ContainsKey(station.Id))
                .OrderBy(station => WalkingJourney.Distance(lat, lng, (double)station.Latitude, (double)station.Longitude)).ToArray();
            // Keep nearby options on each line, so one line's dense stations cannot
            // crowd out a slightly farther station offering a direct journey.
            var candidates = ordered.Take(4).Concat(ordered.SelectMany(station => station.Lines.Select(line => (station, line)))
                .GroupBy(item => item.line).SelectMany(group => group.Take(2).Select(item => item.station)))
                .DistinctBy(station => station.Id).ToArray();
            using var concurrency = new SemaphoreSlim(4);
            var routes = await Task.WhenAll(candidates.Select(async station =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    var fallback = WalkingJourney.Access(station.Id, lat, lng, (double)station.Latitude, (double)station.Longitude);
                    if (streetRouting is null) return fallback;
                    var path = exiting
                        ? await streetRouting.RouteAsync((double)station.Latitude, (double)station.Longitude, lat, lng, mode, cancellationToken)
                        : await streetRouting.RouteAsync(lat, lng, (double)station.Latitude, (double)station.Longitude, mode, cancellationToken);
                    return new WalkingAccess(station.Id, path.DistanceMeters, path.Seconds + WalkingJourney.PlatformSeconds, path);
                }
                finally { concurrency.Release(); }
            }));
            return routes.Where(access => access.Seconds <= maxWalkMinutes * 60 && access.Path?.Source != "no_route").ToArray();
        }
        var originTask = Candidates(fromLatitude!.Value, fromLongitude!.Value, accessMode, false);
        var destinationTask = Candidates(toLatitude!.Value, toLongitude!.Value, exitMode, true);
        await Task.WhenAll(originTask, destinationTask);
        var rawOrigins = (await originTask).ToDictionary(a => a.StationId);
        var rawExits = (await destinationTask).ToDictionary(a => a.StationId);
        var adjusted = new Dictionary<(string, long, bool, bool), WalkingAccess>();
        WalkingAccess Adjust(WalkingAccess access, DateTime at, bool backwards, bool exiting)
        {
            if (access.Path is not { Mode: "car" or "motorcycle" } path) return access;
            var key = (access.StationId, at.Ticks, backwards, exiting);
            if (adjusted.TryGetValue(key, out var saved)) return saved;
            // Platform time is outside street travel: after access, before exit.
            var streetAt = at.AddSeconds(backwards && !exiting ? -WalkingJourney.PlatformSeconds
                : !backwards && exiting ? WalkingJourney.PlatformSeconds : 0);
            var result = traffic.Apply(path, streetAt, backwards);
            return adjusted[key] = access with { Seconds = result.Seconds + WalkingJourney.PlatformSeconds, Path = result };
        }
        int Duration(string station, DateTime at, bool backwards, bool exiting)
        {
            var value = Adjust((exiting ? rawExits : rawOrigins)[station], at, backwards, exiting).Seconds;
            // Searches span at most 24 hours; this sentinel makes over-budget legs infeasible.
            return value <= maxWalkMinutes * 60 ? value : 86401;
        }
        var (origins, destinations) = WalkingJourney.ConnectCandidates(graph,
            rawOrigins.Values.Where(a => arriveBy || Duration(a.StationId, requested, false, false) <= maxWalkMinutes * 60).ToArray(),
            rawExits.Values.Where(a => !arriveBy || Duration(a.StationId, requested, true, true) <= maxWalkMinutes * 60).ToArray());
        var directMeters = WalkingJourney.Distance(fromLatitude.Value, fromLongitude.Value, toLatitude.Value, toLongitude.Value);
        var directSeconds = (int)Math.Ceiling(directMeters / WalkingJourney.MetersPerSecond);
        var directPath = streetRouting is null ? null : await streetRouting.RouteAsync(fromLatitude.Value, fromLongitude.Value,
            toLatitude.Value, toLongitude.Value, "walk", cancellationToken);
        if (directPath is not null) { directSeconds = directPath.Seconds; directMeters = directPath.DistanceMeters; }
        var routes = origins.Length == 0 || destinations.Length == 0 ? [] : await planner.FindAlternativesAsync(
            origins.ToDictionary(item => item.StationId, item => item.Seconds),
            destinations.ToDictionary(item => item.StationId, item => item.Seconds), start, end, transferMinutes, cancellationToken,
            arriveBy, true, now, viaStationIds,
            accessDuration: (station, at, backwards) => Duration(station, at, backwards, false),
            exitDuration: (station, at, backwards) => Duration(station, at, backwards, true), serviceDays: serviceDays);
        var options = new List<JourneyOption>();
        foreach (var legs in routes)
        {
            var access = Adjust(rawOrigins[legs[0].FromStationId], arriveBy ? legs[0].DepartureLocal : requested, arriveBy, false);
            var exit = Adjust(rawExits[legs[^1].ToStationId], legs[^1].ArrivalLocal, false, true);
            var departure = arriveBy ? legs[0].DepartureLocal.AddSeconds(-access.Seconds) : requested;
            var arrival = legs[^1].ArrivalLocal.AddSeconds(exit.Seconds);
            if (access.Seconds > maxWalkMinutes * 60 || exit.Seconds > maxWalkMinutes * 60
                || arriveBy && arrival > requested) continue;
            var id = string.Join("/", legs.Select(leg => $"{leg.RouteId}:{leg.FromStationId}:{leg.ToStationId}"));
            options.Add(new(id, false, access, exit, legs, departure, arrival, (int)Math.Ceiling((arrival - departure).TotalSeconds)));
        }
        if (viaStationIds.Length == 0 && directPath?.Source != "no_route")
        {
            var departure = arriveBy ? requested.AddSeconds(-directSeconds) : requested;
            options.Add(new("walk", true, null, null, [], departure, departure.AddSeconds(directSeconds), directSeconds));
        }
        var orderedOptions = options.OrderBy(option => option.TotalSeconds).ThenBy(option => option.Legs.Length).Take(6).ToArray();
        if (orderedOptions.Length == 0)
            return NotFound(new ProblemDetails { Detail = "No feasible journey found in the selected time window." });
        var selected = orderedOptions[0];
        return new WalkingJourneyResult(now, selected.ArrivalLocal,
            "Asia/Tehran", "timetable", selected.WalkOnly, selected.Access, selected.Exit, directSeconds, Math.Round(directMeters),
            WalkingJourney.MetersPerSecond, WalkingJourney.DetourFactor, WalkingJourney.PlatformSeconds,
            selected.Legs, selected.DepartureLocal, requested, timeMode, accessMode, exitMode, transferMinutes, directPath, viaStationIds,
            selected.Id, selected.TotalSeconds, orderedOptions);
    }

    [HttpGet("journeys")]
    public async Task<ActionResult<JourneySearchResult>> FindJourney(
        [FromQuery, Required, StringLength(100)] string fromStationId,
        [FromQuery, Required, StringLength(100)] string toStationId,
        [FromQuery] DateOnly? date,
        [FromQuery] TimeOnly? after,
        [FromQuery, Range(1, 24)] int searchHours = 6,
        [FromQuery, Range(0, 60)] int fallbackTransferMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        fromStationId = fromStationId.Trim();
        toStationId = toStationId.Trim();
        var stations = await db.Stations.AsNoTracking().Where(s => s.Id == fromStationId || s.Id == toStationId)
            .Select(s => new { s.Id, s.InfrastructureStatus }).ToArrayAsync(cancellationToken);
        var origin = stations.FirstOrDefault(s => s.Id.Equals(fromStationId, StringComparison.OrdinalIgnoreCase));
        var destination = stations.FirstOrDefault(s => s.Id.Equals(toStationId, StringComparison.OrdinalIgnoreCase));
        if (origin is null || destination is null)
            return NotFound(new ProblemDetails { Detail = "One or both station IDs were not found." });

        if (origin.Id == destination.Id)
            return BadRequest(new ProblemDetails { Detail = "Choose different origin and destination stations." });

        var now = TehranTime.Now(clock);
        var serviceDate = date ?? DateOnly.FromDateTime(now);
        var departureTime = after ?? (date.HasValue ? TimeOnly.MinValue : TimeOnly.FromDateTime(now));
        var departure = serviceDate.ToDateTime(departureTime);
        if (departure > DateTime.MaxValue.AddHours(-searchHours))
            return BadRequest(new ProblemDetails { Detail = "The search window exceeds the supported date range." });

        var searchEnd = departure.AddHours(searchHours);
        var legs = origin.InfrastructureStatus == "operational" && destination.InfrastructureStatus == "operational"
            ? await planner.FindAsync(origin.Id, destination.Id, departure, searchEnd, fallbackTransferMinutes, cancellationToken)
            : null;
        return new JourneySearchResult(origin.Id, destination.Id, departure, searchEnd, "Asia/Tehran", legs);
    }

    // TODO : For routing around disruptions, ingest structured suspensions and cancellations with time windows
    // and affected routes, track segments or trips. Define operator precedence over the published timetable,
    // including reroutes and replacement service. Free-text alerts are displayed separately.
    // TODO : Offer step-free journeys after validating entrance-to-platform and interchange paths in each
    // direction and obtaining elevator outage data. A station-wide elevator flag cannot verify a complete path.
}

public sealed record JourneySearchResult(string FromStationId, string ToStationId, DateTime DepartureAfterLocal,
    DateTime SearchEndLocal, string TimeZone, JourneyLeg[]? Legs);

public sealed record WalkingJourneyResult(DateTime RequestedAtLocal, DateTime ArrivalLocal, string TimeZone,
    string DataSource, bool WalkOnly, WalkingAccess? Access, WalkingAccess? Exit, int DirectWalkSeconds,
    double DirectWalkMeters, double WalkingMetersPerSecond, double WalkingDetourFactor, int PlatformSeconds,
    JourneyLeg[] Legs, DateTime DepartureLocal, DateTime RequestedTimeLocal, string TimeMode,
    string AccessMode, string ExitMode, int TransferMinutes, StreetPath? DirectPath, string[] ViaStationIds,
    string Id, int TotalSeconds, JourneyOption[] Options);

public sealed record JourneyOption(string Id, bool WalkOnly, WalkingAccess? Access, WalkingAccess? Exit,
    JourneyLeg[] Legs, DateTime DepartureLocal, DateTime ArrivalLocal, int TotalSeconds);
