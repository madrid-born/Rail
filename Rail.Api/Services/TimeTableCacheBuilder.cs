using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Rail.Api.Models;

namespace Rail.Api.Services;

public sealed class TimeTableCacheBuilder(AppDbContext db, HolidayCalendar holidays, IMemoryCache cache)
{
    private async Task<StationTemplates> LoadStationTemplates(string stationId, byte lineId,
        CancellationToken cancellationToken)
    {
        var key = (nameof(StationTemplates), stationId, lineId);
        return (await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var boardingRoutes = (await db.MetroRoutes.AsNoTracking()
                .Where(route => route.LineId == lineId && route.Stops.Any(stop => stop.StationId == stationId
                    && stop.Status == "operational" && stop.Station.InfrastructureStatus == "operational"
                    && stop.Station.Lines.Any(line => line.LineId == lineId && line.IsOperational)))
                .Select(route => route.Id).ToArrayAsync(cancellationToken)).ToHashSet();
            var templates = await db.ScheduledTrips.AsNoTracking()
                .Where(trip => trip.ScheduleGroup.Route.LineId == lineId
                    && trip.ScheduleGroup.Route.Stops.Any(stop => stop.StationId == stationId))
                .Select(trip => new TripTemplate(trip.Id, trip.ScheduleGroup.RouteId,
                    trip.ScheduleGroup.Route.LineId, trip.DayTypeCode, trip.DirectionStationId,
                    trip.DepartureTime, trip.DepartureDayOffset, trip.ArrivalTime, trip.ArrivalDayOffset,
                    trip.StopTimes.Where(stop => stop.StationId == stationId).OrderBy(stop => stop.Sequence).Select(stop => new StopTemplate(stop.Sequence,
                        stop.StationId, stop.ArrivalTime, stop.ArrivalDayOffset, stop.DepartureTime, stop.DepartureDayOffset)).ToArray()))
                .ToArrayAsync(cancellationToken);
            return new StationTemplates(boardingRoutes, templates);
        }))!;
    }

    public async Task<StationTrainBoard> BuildStationAsync(string stationId, byte lineId, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(at.DateTime);
        var until = at.AddHours(24);
        var data = await LoadStationTemplates(stationId, lineId, cancellationToken);
        var (boardingRoutes, templates) = data;
        // First read only trip headers and this station's stop. Loading every stop
        // of every future trip dominates both SQL execution and materialization.
        var candidatesByDate = new List<(TripTemplate Trip, DateOnly Date, StationBoardStop[] Target)>();
        var maximumOffset = templates.Length == 0 ? 0 : templates.Max(trip => (int)trip.ArrivalDayOffset);
        for (var offset = -Math.Min(maximumOffset, date.DayNumber); offset <= 1; offset++)
        {
            var serviceDate = date.AddDays(offset);
            var candidates = templates.Where(trip => At(serviceDate, trip.ArrivalTime, trip.ArrivalDayOffset) >= at
                && At(serviceDate, trip.DepartureTime, trip.DepartureDayOffset) <= until).ToArray();
            if (candidates.Length == 0) continue;
            var day = await holidays.GetDayAsync(serviceDate, cancellationToken);
            foreach (var trip in candidates.Where(trip => trip.DayTypeCode == day.DayTypeCode))
            {
                var target = trip.Stops.Select(stop => new StationBoardStop(stop.Sequence, stop.Station,
                    At(serviceDate, stop.ArrivalTime, stop.ArrivalDayOffset),
                    stop.DepartureTime is { } time && stop.DepartureDayOffset is { } dayOffset
                        ? At(serviceDate, time, dayOffset) : (DateTimeOffset?)null)).ToArray();
                candidatesByDate.Add((trip, serviceDate, target));
            }
        }

        var fullStops = new Dictionary<long, StopTemplate[]>();
        var invalid = new HashSet<long>();
        StationNextArrival[] NextArrivals() => candidatesByDate.Where(item => !invalid.Contains(item.Trip.Id)
                && boardingRoutes.Contains(item.Trip.RouteId)).SelectMany(item => item.Target.Where(stop =>
                (stop.DepartureAt ?? stop.ArrivalAt) >= at && stop.ArrivalAt <= until)
            .Select(stop => new StationNextArrival(item.Trip.RouteId, item.Trip.DirectionStationId, item.Trip.Id,
                item.Date, stop.ArrivalAt, stop.DepartureAt)))
            .GroupBy(arrival => (arrival.RouteId, arrival.DirectionStationId))
            .Select(group => group.OrderBy(arrival => arrival.ArrivalAt).ThenBy(arrival => arrival.TripId).First()).ToArray();

        StationNextArrival[] next;
        // Normally one batched stop query: active trains and the next train in
        // each direction only. Invalid schedules fall through to the next valid
        // candidate without hiding a later arrival or inventing a position.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            next = NextArrivals();
            var wanted = candidatesByDate.Where(item => !invalid.Contains(item.Trip.Id)
                    && At(item.Date, item.Trip.DepartureTime, item.Trip.DepartureDayOffset) <= at
                    && At(item.Date, item.Trip.ArrivalTime, item.Trip.ArrivalDayOffset) >= at)
                .Select(item => item.Trip.Id).Concat(next.Select(arrival => arrival.TripId)).Distinct()
                .Where(id => !fullStops.ContainsKey(id)).ToArray();
            if (wanted.Length == 0) break;
            var rows = await db.ScheduledStopTimes.AsNoTracking().Where(stop => wanted.Contains(stop.TripId))
                .OrderBy(stop => stop.Sequence).Select(stop => new { stop.TripId,
                    Stop = new StopTemplate(stop.Sequence, stop.StationId, stop.ArrivalTime, stop.ArrivalDayOffset,
                        stop.DepartureTime, stop.DepartureDayOffset) }).ToArrayAsync(cancellationToken);
            var stopsByTrip = rows.ToLookup(row => row.TripId, row => row.Stop);
            foreach (var id in wanted)
            {
                var stops = stopsByTrip[id].ToArray();
                fullStops[id] = stops;
                if (!ValidStops(TimedStops(date, stops))) invalid.Add(id);
            }
        }

        var needed = next.Select(arrival => (arrival.TripId, arrival.ServiceDate)).ToHashSet();
        var occurrences = candidatesByDate.Where(item => !invalid.Contains(item.Trip.Id) && fullStops.ContainsKey(item.Trip.Id))
            .Select(item => {
                var stops = TimedStops(item.Date, fullStops[item.Trip.Id]);
                return new StationBoardTrip(item.Trip.Id, item.Date, item.Trip.RouteId, item.Trip.LineId,
                    item.Trip.DirectionStationId, Position(at, stops),
                    stops.Select(stop => new StationBoardStop(stop.Sequence, stop.Station, stop.ArrivalAt, stop.DepartureAt)).ToArray());
            });
        return new StationTrainBoard(stationId, lineId, at, until, next,
            occurrences.Where(trip => trip.Position is not null || needed.Contains((trip.Id, trip.ServiceDate)))
                .OrderBy(trip => trip.Stops[0].ArrivalAt).ThenBy(trip => trip.Id).ToArray());
    }

    public async Task<TimeTableCacheTrip[]> BuildAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var local = at.DateTime;
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        var currentDay = await holidays.GetDayAsync(date, cancellationToken);

        // Include overnight templates of other day types: their service day may be yesterday.
        var templates = await db.ScheduledTrips.AsNoTracking()
            .Where(trip => trip.ArrivalDayOffset > 0
                || (trip.DayTypeCode == currentDay.DayTypeCode && trip.DepartureDayOffset == 0
                    && trip.DepartureTime <= time && trip.ArrivalTime > time))
            .OrderBy(trip => trip.Id)
            .Select(trip => new TripTemplate(trip.Id, trip.ScheduleGroup.RouteId,
                trip.ScheduleGroup.Route.LineId, trip.DayTypeCode, trip.DirectionStationId,
                trip.DepartureTime, trip.DepartureDayOffset, trip.ArrivalTime, trip.ArrivalDayOffset,
                trip.StopTimes.OrderBy(stop => stop.Sequence).Select(stop => new StopTemplate(stop.Sequence,
                    stop.StationId,
                    stop.ArrivalTime, stop.ArrivalDayOffset, stop.DepartureTime, stop.DepartureDayOffset)).ToArray()))
            .ToArrayAsync(cancellationToken);

        var trips = new List<(DateTimeOffset DepartureAt, DateOnly ServiceDate, TimeTableCacheTrip Trip)>();
        var maximumOffset = templates.Length == 0 ? 0 : templates.Max(trip => (int)trip.ArrivalDayOffset);
        for (var offset = 0; offset <= Math.Min(maximumOffset, date.DayNumber); offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Not finished at this instant; its departure must be on/before the requested civil day.
            var candidates = templates.Where(trip => trip.DepartureDayOffset <= offset
                && (trip.ArrivalDayOffset > offset || trip.ArrivalDayOffset == offset && trip.ArrivalTime > time)).ToArray();
            if (offset > 0 && candidates.Length == 0) continue;
            var serviceDate = date.AddDays(-offset);
            var day = offset == 0 ? currentDay : await holidays.GetDayAsync(serviceDate, cancellationToken);
            foreach (var trip in candidates.Where(trip => trip.DayTypeCode == day.DayTypeCode))
            {
                var departure = At(serviceDate, trip.DepartureTime, trip.DepartureDayOffset);
                var arrival = At(serviceDate, trip.ArrivalTime, trip.ArrivalDayOffset);
                if (departure > at || arrival <= at || arrival < departure) continue;
                var stops = trip.Stops.Select(stop => new TimedStop(stop.Sequence, stop.Station,
                    At(serviceDate, stop.ArrivalTime, stop.ArrivalDayOffset),
                    stop.DepartureTime is { } departureTime && stop.DepartureDayOffset is { } departureOffset
                        ? At(serviceDate, departureTime, departureOffset) : (DateTimeOffset?)null)).ToArray();
                var position = Position(at, stops);
                if (position is null) continue;
                trips.Add((departure, serviceDate, new TimeTableCacheTrip(trip.Id,
                    trip.RouteId, trip.LineId, trip.DirectionStationId, position)));
            }
        }

        return trips.OrderBy(trip => trip.DepartureAt).ThenBy(trip => trip.Trip.Id)
            .ThenBy(trip => trip.ServiceDate).Select(trip => trip.Trip).ToArray();
    }

    private static DateTimeOffset At(DateOnly date, TimeOnly time, byte offset) =>
        TehranTime.Local(date.AddDays(offset).ToDateTime(time));

    private static TimedStop[] TimedStops(DateOnly date, StopTemplate[] stops) => stops.Select(stop => new TimedStop(
        stop.Sequence, stop.Station, At(date, stop.ArrivalTime, stop.ArrivalDayOffset),
        stop.DepartureTime is { } time && stop.DepartureDayOffset is { } offset ? At(date, time, offset) : (DateTimeOffset?)null)).ToArray();

    private static TimeTablePosition? Position(DateTimeOffset at, TimedStop[] stops)
    {
        if (!ValidStops(stops)) return null;

        for (var index = 0; index < stops.Length; index++)
        {
            var stop = stops[index];
            if (at == stop.ArrivalAt || stop.ArrivalAt <= at && at < stop.DepartureAt)
                return new(stop.Station, null, null);
            if (index + 1 < stops.Length && stop.DepartureAt is { } leave
                && leave <= at && at < stops[index + 1].ArrivalAt)
                return new(null, stop.Station, stops[index + 1].Station);
        }
        return null;
    }

    private static bool ValidStops(TimedStop[] stops) => stops.Length > 0
        && !stops.Any(stop => stop.DepartureAt < stop.ArrivalAt)
        && !stops.Zip(stops.Skip(1)).Any(pair => pair.Second.ArrivalAt < (pair.First.DepartureAt ?? pair.First.ArrivalAt));

    private sealed record TripTemplate(long Id, string RouteId, byte LineId, string DayTypeCode,
        string DirectionStationId,
        TimeOnly DepartureTime, byte DepartureDayOffset, TimeOnly ArrivalTime, byte ArrivalDayOffset, StopTemplate[] Stops);
    private sealed record StopTemplate(short Sequence, string Station, TimeOnly ArrivalTime,
        byte ArrivalDayOffset, TimeOnly? DepartureTime, byte? DepartureDayOffset);
    private sealed record TimedStop(short Sequence, string Station, DateTimeOffset ArrivalAt, DateTimeOffset? DepartureAt);
    private sealed record StationTemplates(HashSet<string> BoardingRoutes, TripTemplate[] Trips);
}

public sealed record TimeTableCacheTrip(long Id, string RouteId, byte LineId, string DirectionStationId,
    TimeTablePosition Position);
public sealed record TimeTablePosition(string? Station, string? FromStation, string? ToStation);
public sealed record StationTrainBoard(string StationId, byte LineId, DateTimeOffset RequestedAt,
    DateTimeOffset Until, StationNextArrival[] NextArrivals, StationBoardTrip[] Trips);
public sealed record StationNextArrival(string RouteId, string DirectionStationId, long TripId,
    DateOnly ServiceDate, DateTimeOffset ArrivalAt, DateTimeOffset? DepartureAt);
public sealed record StationBoardTrip(long Id, DateOnly ServiceDate, string RouteId, byte LineId,
    string DirectionStationId, TimeTablePosition? Position, StationBoardStop[] Stops);
public sealed record StationBoardStop(short Sequence, string StationId, DateTimeOffset ArrivalAt, DateTimeOffset? DepartureAt);
