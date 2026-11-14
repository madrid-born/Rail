using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;

namespace Rail.Api.Services;

public sealed class JourneyPlanner(AppDbContext db)
{
    public async Task<JourneyLeg[]?> FindAsync(string origin, string destination, DateTime start, DateTime end,
        int fallbackTransferMinutes, CancellationToken cancellationToken)
        => await FindAsync(new Dictionary<string, int> { [origin] = 0 },
            new Dictionary<string, int> { [destination] = 0 }, start, end, fallbackTransferMinutes, cancellationToken);

    public async Task<JourneyLeg[]?> FindAsync(IReadOnlyDictionary<string, int> accessSeconds,
        IReadOnlyDictionary<string, int> exitSeconds, DateTime start, DateTime end,
        int fallbackTransferMinutes, CancellationToken cancellationToken, bool arriveBy = false,
        bool useRequestedTransferTime = false, DateTime? observedAt = null)
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
        if (arriveBy) return SearchReverse(accessSeconds, exitSeconds, start.Ticks, end.Ticks,
            fallbackTransferMinutes * 60, boardings, transferRules, useRequestedTransferTime,
            (observedAt ?? start).Ticks, cancellationToken);
        return Search(accessSeconds, exitSeconds, start.Ticks, end.Ticks, fallbackTransferMinutes * 60,
            boardings, transferRules, cancellationToken, useRequestedTransferTime, (observedAt ?? start).Ticks);
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

    private static JourneyLeg[]? Search(IReadOnlyDictionary<string, int> accessSeconds,
        IReadOnlyDictionary<string, int> exitSeconds, long start, long end, int fallbackSeconds,
        Dictionary<string, IGrouping<ServiceRoute, Boarding>[]> boardings,
        ILookup<(string StationId, byte FromLineId, byte ToLineId), TransferRule> transferRules,
        CancellationToken cancellationToken, bool useRequestedTransferTime = false, long? observedAt = null)
    {
        var best = new Dictionary<(string, ServiceRoute?), long>();
        var queue = new PriorityQueue<Label, long>();
        foreach (var (station, walk) in accessSeconds)
        {
            var arrival = start + walk * TimeSpan.TicksPerSecond;
            best[(station, null)] = arrival;
            queue.Enqueue(new Label(station, null, arrival, null, null), arrival);
        }
        Label? winner = null;
        var bestFinish = long.MaxValue;
        var firstBoardedStop = new Dictionary<(DateOnly, long), int>();

        while (queue.TryDequeue(out var label, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (label.Arrival != best[(label.StationId, label.Route)])
                continue;
            if (label.Arrival > bestFinish) break;
            if (label.Leg is not null && exitSeconds.TryGetValue(label.StationId, out var exit))
            {
                var finish = label.Arrival + exit * TimeSpan.TicksPerSecond;
                if (finish < bestFinish || (finish == bestFinish && winner is not null
                    && ReadLegs(label).Length < ReadLegs(winner).Length))
                {
                    bestFinish = finish;
                    winner = label;
                }
            }
            if (!boardings.TryGetValue(label.StationId, out var routes))
                continue;

            foreach (var route in routes)
            {
                var transfer = GetTransfer(label, route.Key, transferRules, fallbackSeconds, useRequestedTransferTime);
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
                            new DateTime(arrival), index - boarding.StopIndex, transfer,
                            new DateTime(run.MidnightTicks + run.Stops[boarding.StopIndex].ArrivalTicks),
                            TrainPosition(run, observedAt ?? start));
                        best[key] = arrival;
                        queue.Enqueue(new Label(stop.StationId, run.Route, arrival, label, leg), arrival);
                    }
                }
            }
        }
        return winner is null ? null : ReadLegs(winner);
    }

    private static TransferInfo? GetTransfer(Label label, ServiceRoute route,
        ILookup<(string StationId, byte FromLineId, byte ToLineId), TransferRule> rules, int fallbackSeconds,
        bool useRequestedTransferTime = false)
    {
        if (label.Route is not { } previous)
            return null;
        if (useRequestedTransferTime) return new TransferInfo(fallbackSeconds, "user_selected", false);

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

    private static JourneyLeg[]? SearchReverse(IReadOnlyDictionary<string, int> accessSeconds,
        IReadOnlyDictionary<string, int> exitSeconds, long start, long end, int transferSeconds,
        Dictionary<string, IGrouping<ServiceRoute, Boarding>[]> boardings,
        ILookup<(string StationId, byte FromLineId, byte ToLineId), TransferRule> rules,
        bool useRequestedTransferTime, long observedAt, CancellationToken cancellationToken)
    {
        var runs = boardings.Values.SelectMany(groups => groups).SelectMany(group => group)
            .Select(boarding => boarding.Run).DistinctBy(run => (run.ServiceDate, run.TripId));
        var alightings = runs.SelectMany(run => run.Stops.Select((stop, index) => (run, stop, index)))
            .Where(item => item.index > 0 && item.stop.CanUseStation)
            .ToLookup(item => item.stop.StationId);
        var queue = new PriorityQueue<Label, long>();
        var best = new Dictionary<(string, ServiceRoute?), long>();
        foreach (var (station, seconds) in exitSeconds)
        {
            var latest = end - seconds * TimeSpan.TicksPerSecond;
            best[(station, null)] = latest;
            queue.Enqueue(new(station, null, latest, null, null), -latest);
        }
        Label? winner = null;
        var latestStart = long.MinValue;
        while (queue.TryDequeue(out var label, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (best[(label.StationId, label.Route)] != label.Arrival) continue;
            if (label.Arrival < latestStart) break;
            if (label.Leg is not null && accessSeconds.TryGetValue(label.StationId, out var access))
            {
                var departure = label.Arrival - access * TimeSpan.TicksPerSecond;
                if (departure >= start && departure > latestStart) { latestStart = departure; winner = label; }
            }
            foreach (var (run, stop, index) in alightings[label.StationId])
            {
                var transfer = label.Route is { } nextRoute
                    ? GetTransfer(label with { Route = run.Route }, nextRoute, rules, transferSeconds, useRequestedTransferTime) : null;
                var arrival = run.MidnightTicks + stop.ArrivalTicks;
                if (arrival + (transfer?.MinimumSeconds ?? 0) * TimeSpan.TicksPerSecond > label.Arrival) continue;
                var onward = label.Leg is null ? label : label with { Leg = label.Leg with { Transfer = transfer } };
                for (var from = index - 1; from >= 0; from--)
                {
                    var boarding = run.Stops[from];
                    if (!boarding.CanUseStation || boarding.DepartureTicks is not { } departureTicks) continue;
                    var departure = run.MidnightTicks + departureTicks;
                    if (departure < start) continue;
                    var key = (boarding.StationId, (ServiceRoute?)run.Route);
                    if (best.TryGetValue(key, out var known) && known >= departure) continue;
                    best[key] = departure;
                    var leg = new JourneyLeg(run.TripId, run.ServiceDate, run.Route.RouteId, run.Route.LineId,
                        run.DirectionStationId, run.IsExpress, boarding.StationId, label.StationId,
                        new(departure), new(arrival), index - from, null,
                        new DateTime(run.MidnightTicks + boarding.ArrivalTicks), TrainPosition(run, observedAt));
                    queue.Enqueue(new(boarding.StationId, run.Route, departure, onward, leg), -departure);
                }
            }
        }
        if (winner is null) return null;
        var legs = new List<JourneyLeg>();
        for (var cursor = winner; cursor.Leg is not null; cursor = cursor.Previous!) legs.Add(cursor.Leg);
        return legs.ToArray();
    }

    private static JourneyTrainPosition TrainPosition(Run run, long at)
    {
        for (var index = 0; index < run.Stops.Length; index++)
        {
            var stop = run.Stops[index];
            var arrival = run.MidnightTicks + stop.ArrivalTicks;
            var departure = run.MidnightTicks + (stop.DepartureTicks ?? stop.ArrivalTicks);
            if (at < arrival)
                return index == 0 ? new("not_departed", null, null, null)
                    : new("between_stations", null, run.Stops[index - 1].StationId, stop.StationId);
            if (at <= departure) return new("at_station", stop.StationId, null, null);
        }
        return new("finished", run.Stops[^1].StationId, null, null);
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
    int StopCount, TransferInfo? Transfer, DateTime? BoardingArrivalLocal = null, JourneyTrainPosition? TrainPosition = null);
public sealed record TransferInfo(int MinimumSeconds, string Confidence, bool UsedFallback);
public sealed record JourneyTrainPosition(string Status, string? StationId, string? FromStationId, string? ToStationId);
