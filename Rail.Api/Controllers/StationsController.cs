using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/stations")]
public sealed class StationsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<StationSummary[]> List(
        [FromQuery, StringLength(200)] string? q,
        [FromQuery] byte? lineId,
        [FromQuery] bool operationalOnly = false,
        [FromQuery, Range(0, 100_000)] int offset = 0,
        [FromQuery, Range(1, 250)] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var stations = FilterStations(lineId, operationalOnly).OrderBy(s => s.NameEn).ThenBy(s => s.Id)
            .Select(StationSummary.Projection);
        if (string.IsNullOrWhiteSpace(q))
            return await stations.Skip(offset).Take(limit).ToArrayAsync(cancellationToken);

        var term = NormalizeSearch(q);
        var candidates = await stations.ToArrayAsync(cancellationToken);
        return candidates.Where(s => NormalizeSearch(s.NameFa).Contains(term, StringComparison.OrdinalIgnoreCase)
                || NormalizeSearch(s.NameEn).Contains(term, StringComparison.OrdinalIgnoreCase)
                || s.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Skip(offset).Take(limit).ToArray();
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<NearbyStation[]>> Nearby(
        [FromQuery, Required, Range(-90d, 90d)] double? latitude,
        [FromQuery, Required, Range(-180d, 180d)] double? longitude,
        [FromQuery, Range(1, 100_000)] int? radiusMeters,
        [FromQuery, Range(1, 50)] int limit = 5,
        [FromQuery] bool operationalOnly = true,
        CancellationToken cancellationToken = default)
    {
        var stations = await FilterStations(null, operationalOnly).Select(StationSummary.Projection)
            .ToArrayAsync(cancellationToken);
        return stations.Select(s => new NearbyStation(s, DistanceMeters(latitude!.Value, longitude!.Value, s)))
            .Where(s => radiusMeters is null || s.DistanceMeters <= radiusMeters)
            .OrderBy(s => s.DistanceMeters).ThenBy(s => s.Station.Id, StringComparer.Ordinal)
            .Take(limit).ToArray();
    }

    [HttpGet("{stationId}")]
    public async Task<ActionResult<StationDetails>> Get(string stationId, CancellationToken cancellationToken)
    {
        var station = await db.Stations.AsNoTracking().AsSplitQuery()
            .Where(s => s.Id == stationId)
            .Select(s => new StationDetails(s.Id, s.NameFa, s.NameEn, s.Latitude, s.Longitude, s.InfrastructureStatus,
                s.Lines.OrderBy(l => l.LineId).Select(l => new StationLineInfo(l.LineId, l.IsOperational)).ToArray(),
                s.Amenities.OrderBy(a => a.Type).Select(a => new AmenityInfo(a.Type, a.IsAvailable, a.IsVerified)).ToArray(),
                s.Entrances.OrderBy(e => e.Code).Select(e => new EntranceInfo(e.Code, e.NameFa, e.NameEn, e.AddressFa,
                    e.AddressEn, e.Latitude, e.Longitude, e.IsStepFree)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
        return station is null ? NotFound() : station;
    }

    [HttpGet("{stationId}/departures")]
    public async Task<ActionResult<DepartureBoard>> Departures(
        string stationId,
        [FromQuery] DateOnly? date,
        [FromQuery] TimeOnly? after,
        [FromQuery] byte? lineId,
        [FromQuery, StringLength(100)] string? directionStationId,
        [FromQuery, Range(1, 200)] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var station = await db.Stations.AsNoTracking().Where(s => s.Id == stationId)
            .Select(s => s.Id).SingleOrDefaultAsync(cancellationToken);
        if (station is null)
            return NotFound();

        var now = TehranTime.Now(clock);
        var serviceDate = date ?? DateOnly.FromDateTime(now);
        var from = after ?? (date.HasValue ? TimeOnly.MinValue : TimeOnly.FromDateTime(now));
        var dayType = await db.GetServiceDayTypeCodeAsync(serviceDate, cancellationToken);
        var departures = await db.ScheduledStopTimes.AsNoTracking()
            .Where(s => s.StationId == station && s.Trip.DayTypeCode == dayType
                && s.DepartureTime != null && (s.DepartureDayOffset > 0 || s.DepartureTime >= from)
                && (lineId == null || s.Trip.ScheduleGroup.Route.LineId == lineId)
                && (directionStationId == null || s.Trip.DirectionStationId == directionStationId)
                && s.Station.InfrastructureStatus == "operational"
                && s.Station.Lines.Any(l => l.IsOperational && l.LineId == s.Trip.ScheduleGroup.Route.LineId)
                && s.Trip.ScheduleGroup.Route.Stops.Any(rs => rs.StationId == s.StationId && rs.Status == "operational"))
            .OrderBy(s => s.DepartureDayOffset).ThenBy(s => s.DepartureTime).ThenBy(s => s.TripId).ThenBy(s => s.Sequence)
            .Take(limit)
            .Select(s => new DepartureInfo(s.TripId, s.Trip.ScheduleGroup.RouteId, s.Trip.ScheduleGroup.Route.LineId,
                s.Trip.DirectionStationId, s.Trip.DirectionStation.NameFa, s.Trip.DirectionStation.NameEn,
                s.Trip.DestinationStationId, s.Trip.IsExpress, s.DepartureTime!.Value, s.DepartureDayOffset!.Value))
            .ToArrayAsync(cancellationToken);
        return new DepartureBoard(station, serviceDate, dayType, departures);
    }

    // TODO : For indoor routing, obtain surveyed entrance, platform and corridor geometry with elevator/stair
    // edges, accessible directions and opening hours. Straight-line distance is only useful for finding nearby stations.
    // TODO : Decide how station edits are verified and who can publish them. Require a staff policy, source attribution,
    // optimistic concurrency and an audit trail before adding write endpoints for station details and accessibility.

    private IQueryable<Station> FilterStations(byte? lineId, bool operationalOnly) =>
        db.Stations.AsNoTracking().Where(s => (!operationalOnly || s.InfrastructureStatus == "operational")
            && (lineId == null || s.Lines.Any(l => l.LineId == lineId && (!operationalOnly || l.IsOperational))));

    private static string NormalizeSearch(string value) => value.Trim().Normalize(NormalizationForm.FormC)
        .Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace("\u200c", "");

    private static double DistanceMeters(double latitude, double longitude, StationSummary station)
    {
        const double radians = Math.PI / 180;
        const double earthRadiusMeters = 6_371_000;
        var deltaLatitude = ((double)station.Latitude - latitude) * radians;
        var deltaLongitude = ((double)station.Longitude - longitude) * radians;
        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2), 2)
            + Math.Cos(latitude * radians) * Math.Cos((double)station.Latitude * radians)
            * Math.Pow(Math.Sin(deltaLongitude / 2), 2);
        return 2 * earthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
    }
}

public sealed record StationSummary(string Id, string NameFa, string NameEn, decimal Latitude, decimal Longitude,
    string InfrastructureStatus, StationLineInfo[] Lines)
{
    internal static readonly Expression<Func<Station, StationSummary>> Projection =
        s => new StationSummary(s.Id, s.NameFa, s.NameEn, s.Latitude, s.Longitude, s.InfrastructureStatus,
            s.Lines.OrderBy(l => l.LineId).Select(l => new StationLineInfo(l.LineId, l.IsOperational)).ToArray());
}

public sealed record StationLineInfo(byte LineId, bool IsOperational);
public sealed record NearbyStation(StationSummary Station, double DistanceMeters);
public sealed record AmenityInfo(AmenityType Type, bool IsAvailable, bool IsVerified);
public sealed record EntranceInfo(string Code, string NameFa, string? NameEn, string? AddressFa, string? AddressEn,
    decimal? Latitude, decimal? Longitude, bool? IsStepFree);
public sealed record StationDetails(string Id, string NameFa, string NameEn, decimal Latitude, decimal Longitude,
    string InfrastructureStatus, StationLineInfo[] Lines, AmenityInfo[] Amenities, EntranceInfo[] Entrances);
public sealed record DepartureBoard(string StationId, DateOnly ServiceDate, string DayTypeCode, DepartureInfo[] Departures);
public sealed record DepartureInfo(long TripId, string RouteId, byte LineId, string DirectionStationId,
    string DirectionNameFa, string DirectionNameEn, string DestinationStationId, bool IsExpress, TimeOnly DepartureTime,
    byte DepartureDayOffset);
