using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;

namespace Rail.Api.Services;

public sealed class TimeTableCacheBuilder(AppDbContext db, HolidayCalendar holidays)
{
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

    private static TimeTablePosition? Position(DateTimeOffset at, TimedStop[] stops)
    {
        if (stops.Length == 0 || stops.Any(stop => stop.DepartureAt < stop.ArrivalAt)
            || stops.Zip(stops.Skip(1)).Any(pair => pair.Second.ArrivalAt < (pair.First.DepartureAt ?? pair.First.ArrivalAt)))
            return null;

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

    private sealed record TripTemplate(long Id, string RouteId, byte LineId, string DayTypeCode,
        string DirectionStationId,
        TimeOnly DepartureTime, byte DepartureDayOffset, TimeOnly ArrivalTime, byte ArrivalDayOffset, StopTemplate[] Stops);
    private sealed record StopTemplate(short Sequence, string Station, TimeOnly ArrivalTime,
        byte ArrivalDayOffset, TimeOnly? DepartureTime, byte? DepartureDayOffset);
    private sealed record TimedStop(short Sequence, string Station, DateTimeOffset ArrivalAt, DateTimeOffset? DepartureAt);
}

public sealed record TimeTableCacheTrip(long Id, string RouteId, byte LineId, string DirectionStationId,
    TimeTablePosition Position);
public sealed record TimeTablePosition(string? Station, string? FromStation, string? ToStation);
