using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/timetable")]
public sealed class TimetableController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("day-types")]
    public Task<DayTypeInfo[]> DayTypes(CancellationToken cancellationToken) => db.ServiceDayTypes.AsNoTracking()
        .OrderBy(d => d.Code).Select(d => new DayTypeInfo(d.Code, d.Description)).ToArrayAsync(cancellationToken);

    [HttpGet("schedule-groups")]
    public Task<ScheduleGroupInfo[]> ScheduleGroups([FromQuery] byte? lineId, CancellationToken cancellationToken) =>
        db.ScheduleGroups.AsNoTracking().Where(g => lineId == null || g.Route.LineId == lineId)
            .OrderBy(g => g.Route.LineId).ThenBy(g => g.Id)
            .Select(g => new ScheduleGroupInfo(g.Id, g.RouteId, g.Route.LineId, g.ScheduleKey,
                g.IsBranchService, g.FromTerminalId, g.ToTerminalId)).ToArrayAsync(cancellationToken);

    [HttpGet("schedule-groups/{groupId}/trips")]
    public async Task<ActionResult<ScheduleGroupTimetable>> GroupTrips(
        short groupId,
        [FromQuery] DateOnly? date,
        [FromQuery] TimeOnly? after,
        [FromQuery, Range(0, 100_000)] int offset = 0,
        [FromQuery, Range(1, 200)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!await db.ScheduleGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
            return NotFound();

        var serviceDate = date ?? DateOnly.FromDateTime(TehranTime.Now(clock));
        var dayType = await db.GetServiceDayTypeCodeAsync(serviceDate, cancellationToken);
        var trips = await db.ScheduledTrips.AsNoTracking()
            .Where(t => t.ScheduleGroupId == groupId && t.DayTypeCode == dayType
                && (after == null || t.DepartureDayOffset > 0 || t.DepartureTime >= after))
            .OrderBy(t => t.DepartureDayOffset).ThenBy(t => t.DepartureTime).ThenBy(t => t.Id)
            .Skip(offset).Take(limit)
            .Select(t => new TripSummary(t.Id, t.DirectionStationId, t.OriginStationId, t.DestinationStationId, t.IsExpress,
                t.DepartureTime, t.DepartureDayOffset, t.ArrivalTime, t.ArrivalDayOffset))
            .ToArrayAsync(cancellationToken);
        return new ScheduleGroupTimetable(groupId, serviceDate, dayType, trips);
    }

    [HttpGet("trips/{tripId:long}")]
    public async Task<ActionResult<TripDetails>> Trip(long tripId, CancellationToken cancellationToken)
    {
        var trip = await db.ScheduledTrips.AsNoTracking().Where(t => t.Id == tripId)
            .Select(t => new TripDetails(t.Id, t.ScheduleGroupId, t.ScheduleGroup.RouteId, t.ScheduleGroup.Route.LineId,
                t.DayTypeCode, t.DirectionStationId, t.OriginStationId, t.DestinationStationId, t.IsExpress,
                t.DepartureTime, t.DepartureDayOffset, t.ArrivalTime, t.ArrivalDayOffset,
                t.StopTimes.OrderBy(s => s.Sequence).Select(s => new TripStopInfo(s.Sequence, s.StationId,
                    s.Station.NameFa, s.Station.NameEn, s.ArrivalTime, s.ArrivalDayOffset,
                    s.DepartureTime, s.DepartureDayOffset)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
        return trip is null ? NotFound() : trip;
    }

    // TODO : For live arrivals, choose an official operator feed and match its service-date trip IDs to these schedules.
    // Record observation times and expiry, reconcile cancelled or short-turned trains, and fall back to scheduled data
    // when the feed is stale. Expose the source and observation time so riders can distinguish live and scheduled times.
    // TODO : If the operator provides crowding data, establish its granularity, freshness and display thresholds.
    // Mark unavailable readings as unknown and avoid predicting occupancy from the published timetable alone.
}

public sealed record DayTypeInfo(string Code, string Description);
public sealed record ScheduleGroupInfo(short Id, string RouteId, byte LineId, string ScheduleKey, bool IsBranchService,
    string FromTerminalId, string ToTerminalId);
public sealed record ScheduleGroupTimetable(short ScheduleGroupId, DateOnly ServiceDate, string DayTypeCode, TripSummary[] Trips);
public sealed record TripSummary(long Id, string DirectionStationId, string OriginStationId, string DestinationStationId,
    bool IsExpress, TimeOnly DepartureTime, byte DepartureDayOffset, TimeOnly ArrivalTime, byte ArrivalDayOffset);
public sealed record TripDetails(long Id, short ScheduleGroupId, string RouteId, byte LineId, string DayTypeCode,
    string DirectionStationId, string OriginStationId, string DestinationStationId, bool IsExpress, TimeOnly DepartureTime,
    byte DepartureDayOffset, TimeOnly ArrivalTime, byte ArrivalDayOffset, TripStopInfo[] Stops);
public sealed record TripStopInfo(short Sequence, string StationId, string NameFa, string NameEn, TimeOnly ArrivalTime,
    byte ArrivalDayOffset, TimeOnly? DepartureTime, byte? DepartureDayOffset);
