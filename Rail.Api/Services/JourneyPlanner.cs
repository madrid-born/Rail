using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;

namespace Rail.Api.Services;

public sealed class JourneyPlanner(AppDbContext db)
{
    public async Task<JourneyLeg[]?> FindAsync(string origin, string destination, DateTime start, DateTime end,
        int fallbackTransferMinutes, CancellationToken cancellationToken)
    {
        var maxOffset = await db.ScheduledStopTimes.MaxAsync(s => s.DepartureDayOffset, cancellationToken) ?? 0;
        var startDate = DateOnly.FromDateTime(start);
        var firstDate = startDate.AddDays(-Math.Min(maxOffset, startDate.DayNumber));
        var lastDate = DateOnly.FromDateTime(end);
        var overrides = await db.ServiceCalendarDates.AsNoTracking()
            .Where(d => d.ServiceDate >= firstDate && d.ServiceDate <= lastDate)
            .ToDictionaryAsync(d => d.ServiceDate, d => d.DayTypeCode, cancellationToken);
        var datesByType = Enumerable.Range(firstDate.DayNumber, lastDate.DayNumber - firstDate.DayNumber + 1)
            .Select(DateOnly.FromDayNumber)
            .ToLookup(date => overrides.TryGetValue(date, out var code) ? code : ServiceDayType.DefaultCodeFor(date));
        var dayTypes = datesByType.Select(group => group.Key).ToArray();

        var stops = await db.ScheduledStopTimes.AsNoTracking()
            .Where(s => dayTypes.Contains(s.Trip.DayTypeCode))
            .OrderBy(s => s.TripId).ThenBy(s => s.Sequence)
            .Select(s => new TimedStop(s.TripId, s.Trip.DayTypeCode, s.Trip.ScheduleGroup.RouteId,
                s.Trip.ScheduleGroup.Route.LineId, s.Trip.DirectionStationId, s.Trip.IsExpress, s.StationId,
                s.ArrivalTime, s.ArrivalDayOffset, s.DepartureTime, s.DepartureDayOffset,
                s.Station.InfrastructureStatus == "operational"
                    && s.Station.Lines.Any(l => l.IsOperational && l.LineId == s.Trip.ScheduleGroup.Route.LineId)
                    && s.Trip.ScheduleGroup.Route.Stops.Any(rs => rs.StationId == s.StationId && rs.Status == "operational")))
            .ToArrayAsync(cancellationToken);
        var transferRules = (await db.TransferRules.AsNoTracking().ToArrayAsync(cancellationToken))
            .ToLookup(r => (r.StationId, r.FromLineId, r.ToLineId));

        var boardings = BuildBoardings(stops, datesByType, start.Ticks, end.Ticks, cancellationToken);
        return Search(origin, destination, start.Ticks, end.Ticks, fallbackTransferMinutes * 60,
            boardings, transferRules, cancellationToken);
    }

    private static Dictionary<string, IGrouping<ServiceRoute, Boarding>[]> BuildBoardings(TimedStop[] stops,
        ILookup<string, DateOnly> datesByType, long start, long end, CancellationToken cancellationToken)
    {
        var boardings = new List<Boarding>();
        foreach (var group in stops.GroupBy(s => s.TripId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trip = group.ToArray();
            var first = trip[0];
            // Do not route through a malformed trip whose stop times go backwards.
            if (trip.Zip(trip.Skip(1)).Any(pair => pair.Second.ArrivalTicks < (pair.First.DepartureTicks ?? pair.First.ArrivalTicks)))
                continue;

            foreach (var date in datesByType[first.DayTypeCode])
            {
                var run = new Run(date, first.TripId, new ServiceRoute(first.RouteId, first.LineId),
                    first.DirectionStationId, first.IsExpress, trip, date.ToDateTime(TimeOnly.MinValue).Ticks);
                for (var index = 0; index < trip.Length - 1; index++)
                {
                    var stop = trip[index];
                    if (stop.CanUseStation && stop.DepartureTicks is { } relativeDeparture)
                    {
                        var departure = run.MidnightTicks + relativeDeparture;
                        if (departure >= start && departure <= end)
                            boardings.Add(new Boarding(run, index, departure));
                    }
                }
            }
        }

        return boardings.GroupBy(b => b.Run.Stops[b.StopIndex].StationId)
            .ToDictionary(station => station.Key, station => station.OrderBy(b => b.Departure)
                .GroupBy(b => b.Run.Route).ToArray());
    }

    private static JourneyLeg[]? Search(string origin, string destination, long start, long end, int fallbackSeconds,
        Dictionary<string, IGrouping<ServiceRoute, Boarding>[]> boardings,
        ILookup<(string StationId, byte FromLineId, byte ToLineId), TransferRule> transferRules,
        CancellationToken cancellationToken)
    {
        var initial = new Label(origin, null, start, null, null);
        var best = new Dictionary<(string, ServiceRoute?), long> { [(origin, null)] = start };
        var queue = new PriorityQueue<Label, long>();
        queue.Enqueue(initial, start);
        var firstBoardedStop = new Dictionary<(DateOnly, long), int>();

        while (queue.TryDequeue(out var label, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (label.Arrival != best[(label.StationId, label.Route)])
                continue;
            if (label.StationId == destination)
                return ReadLegs(label);
            if (!boardings.TryGetValue(label.StationId, out var routes))
                continue;

            foreach (var route in routes)
            {
                var transfer = GetTransfer(label, route.Key, transferRules, fallbackSeconds);
                var earliestBoarding = label.Arrival + (transfer?.MinimumSeconds ?? 0) * TimeSpan.TicksPerSecond;
                foreach (var boarding in route)
                {
                    if (boarding.Departure < earliestBoarding)
                        continue;

                    var run = boarding.Run;
                    var runKey = (run.ServiceDate, run.TripId);
                    // A run boarded upstream already offers the same arrivals at all later stops.
                    var lastNewStop = run.Stops.Length - 1;
                    if (firstBoardedStop.TryGetValue(runKey, out var previousStop))
                    {
                        if (previousStop <= boarding.StopIndex)
                            continue;
                        lastNewStop = previousStop;
                    }
                    firstBoardedStop[runKey] = boarding.StopIndex;

                    for (var index = boarding.StopIndex + 1; index <= lastNewStop; index++)
                    {
                        var stop = run.Stops[index];
                        var arrival = run.MidnightTicks + stop.ArrivalTicks;
                        if (arrival > end)
                            break;
                        if (!stop.CanUseStation)
                            continue;

                        var key = (stop.StationId, (ServiceRoute?)run.Route);
                        if (best.TryGetValue(key, out var previousArrival) && previousArrival <= arrival)
                            continue;

                        var leg = new JourneyLeg(run.TripId, run.ServiceDate, run.Route.RouteId, run.Route.LineId,
                            run.DirectionStationId, run.IsExpress, label.StationId, stop.StationId, new DateTime(boarding.Departure),
                            new DateTime(arrival), index - boarding.StopIndex, transfer);
                        best[key] = arrival;
                        queue.Enqueue(new Label(stop.StationId, run.Route, arrival, label, leg), arrival);
                    }
                }
            }
        }
        return null;
    }

    private static TransferInfo? GetTransfer(Label label, ServiceRoute route,
        ILookup<(string StationId, byte FromLineId, byte ToLineId), TransferRule> rules, int fallbackSeconds)
    {
        if (label.Route is not { } previous)
            return null;

        var rule = rules[(label.StationId, previous.LineId, route.LineId)]
            .Where(r => (r.FromRouteId == null || r.FromRouteId == previous.RouteId)
                && (r.ToRouteId == null || r.ToRouteId == route.RouteId))
            .OrderByDescending(r => (r.FromRouteId == null ? 0 : 1) + (r.ToRouteId == null ? 0 : 1))
            .ThenByDescending(r => r.WalkSeconds)
            .FirstOrDefault();
        return rule is null
            ? new TransferInfo(fallbackSeconds, "estimated", true)
            : new TransferInfo(rule.WalkSeconds, rule.Confidence, false);
    }

    private static JourneyLeg[] ReadLegs(Label label)
    {
        var legs = new List<JourneyLeg>();
        for (var cursor = label; cursor.Leg is not null; cursor = cursor.Previous!)
            legs.Add(cursor.Leg);
        legs.Reverse();
        return legs.ToArray();
    }

    private sealed record TimedStop(long TripId, string DayTypeCode, string RouteId, byte LineId,
        string DirectionStationId, bool IsExpress, string StationId, TimeOnly Arrival, byte ArrivalOffset,
        TimeOnly? Departure, byte? DepartureOffset, bool CanUseStation)
    {
        public long ArrivalTicks => ArrivalOffset * TimeSpan.TicksPerDay + Arrival.Ticks;
        public long? DepartureTicks => DepartureOffset * TimeSpan.TicksPerDay + Departure?.Ticks;
    }

    private readonly record struct ServiceRoute(string RouteId, byte LineId);
    private sealed record Run(DateOnly ServiceDate, long TripId, ServiceRoute Route, string DirectionStationId,
        bool IsExpress, TimedStop[] Stops, long MidnightTicks);
    private sealed record Boarding(Run Run, int StopIndex, long Departure);
    private sealed record Label(string StationId, ServiceRoute? Route, long Arrival, Label? Previous, JourneyLeg? Leg);
}

public sealed record JourneyLeg(long TripId, DateOnly ServiceDate, string RouteId, byte LineId, string DirectionStationId,
    bool IsExpress, string FromStationId, string ToStationId, DateTime DepartureLocal, DateTime ArrivalLocal,
    int StopCount, TransferInfo? Transfer);
public sealed record TransferInfo(int MinimumSeconds, string Confidence, bool UsedFallback);
