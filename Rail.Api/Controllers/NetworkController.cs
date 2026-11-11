using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/network")]
public sealed class NetworkController(AppDbContext db) : ControllerBase
{
    [HttpGet("load-cached-data")]
    [EndpointSummary("Load Cached Data")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<NetworkCacheData> LoadCachedData(CancellationToken cancellationToken)
    {
        // Three read-only projections, independent of the number of lines/routes.
        // Only station-page fields are sent; no timetable or account data is included.
        var lines = await db.MetroLines.AsNoTracking().OrderBy(l => l.Id)
            .Select(LineInfo.Projection).ToArrayAsync(cancellationToken);
        var stations = await db.Stations.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new CachedStation(s.Id, s.NameFa, s.NameEn, s.InfrastructureStatus,
                s.Lines.OrderBy(l => l.LineId)
                    .Select(l => new StationLineInfo(l.LineId, l.IsOperational)).ToArray()))
            .ToArrayAsync(cancellationToken);
        var routes = await db.MetroRoutes.AsNoTracking().OrderBy(r => r.LineId)
            .ThenBy(r => r.BranchCode != null).ThenBy(r => r.Id)
            .Select(global::Rail.Api.Controllers.RouteDetails.Projection).ToArrayAsync(cancellationToken);
        return new NetworkCacheData(1, DateTimeOffset.UtcNow, lines, stations, routes);
    }

    [HttpGet("lines")]
    public Task<LineInfo[]> Lines(CancellationToken cancellationToken) => db.MetroLines.AsNoTracking()
        .OrderBy(l => l.Id).Select(LineInfo.Projection).ToArrayAsync(cancellationToken);

    [HttpGet("lines/{lineId}")]
    public async Task<ActionResult<LineInfo>> Line(byte lineId, CancellationToken cancellationToken)
    {
        var line = await db.MetroLines.AsNoTracking().Where(l => l.Id == lineId)
            .Select(LineInfo.Projection).SingleOrDefaultAsync(cancellationToken);
        return line is null ? NotFound() : line;
    }

    [HttpGet("routes/{routeId}")]
    public async Task<ActionResult<RouteDetails>> RouteDetails(string routeId, CancellationToken cancellationToken)
    {
        var route = await db.MetroRoutes.AsNoTracking().Where(r => r.Id == routeId)
            .Select(global::Rail.Api.Controllers.RouteDetails.Projection)
            .SingleOrDefaultAsync(cancellationToken);
        return route is null ? NotFound() : route;
    }

    [HttpGet("segments")]
    public Task<TrackSegmentInfo[]> Segments(
        [FromQuery] byte? lineId,
        [FromQuery, StringLength(100)] string? routeId,
        [FromQuery] bool operationalOnly = false,
        CancellationToken cancellationToken = default) => db.TrackSegments.AsNoTracking()
        .Where(s => (lineId == null || s.Route.LineId == lineId) && (routeId == null || s.RouteId == routeId)
            && (!operationalOnly || s.Status == "operational"))
        .OrderBy(s => s.Route.LineId).ThenBy(s => s.RouteId).ThenBy(s => s.Id)
        .Select(s => new TrackSegmentInfo(s.Id, s.RouteId, s.Route.LineId, s.FromStationId, s.ToStationId, s.Status))
        .ToArrayAsync(cancellationToken);

    [HttpGet("transfer-rules")]
    public Task<TransferRuleInfo[]> TransferRules(
        [FromQuery, StringLength(100)] string? stationId,
        CancellationToken cancellationToken) => db.TransferRules.AsNoTracking()
        .Where(r => stationId == null || r.StationId == stationId)
        .OrderBy(r => r.StationId).ThenBy(r => r.FromLineId).ThenBy(r => r.ToLineId).ThenBy(r => r.Id)
        .Select(r => new TransferRuleInfo(r.StationId, r.FromLineId, r.ToLineId, r.FromRouteId, r.ToRouteId, r.WalkSeconds, r.Confidence))
        .ToArrayAsync(cancellationToken);
}

public sealed record LineInfo(byte Id, string NameFa, string NameEn, string Color, RouteInfo[] Routes)
{
    internal static readonly Expression<Func<MetroLine, LineInfo>> Projection = line =>
        new LineInfo(line.Id, line.NameFa, line.NameEn, line.Color,
            line.Routes.OrderBy(r => r.Id).Select(r => new RouteInfo(r.Id, r.BranchCode)).ToArray());
}

public sealed record RouteInfo(string Id, string? BranchCode);
public sealed record RouteDetails(string Id, byte LineId, string? BranchCode, RouteStation[] Stops)
{
    internal static readonly Expression<Func<MetroRoute, RouteDetails>> Projection = route =>
        new RouteDetails(route.Id, route.LineId, route.BranchCode, route.Stops.OrderBy(stop => stop.Sequence)
            .Select(stop => new RouteStation(stop.StationId, stop.Sequence, stop.Status,
                stop.Station.NameFa, stop.Station.NameEn)).ToArray());
}
public sealed record NetworkCacheData(int SchemaVersion, DateTimeOffset GeneratedAtUtc,
    LineInfo[] Lines, CachedStation[] Stations, RouteDetails[] Routes);
public sealed record CachedStation(string Id, string NameFa, string NameEn, string InfrastructureStatus,
    StationLineInfo[] Lines);
public sealed record RouteStation(string StationId, short Sequence, string Status, string NameFa, string NameEn);
public sealed record TrackSegmentInfo(string Id, string RouteId, byte LineId, string FromStationId, string ToStationId, string Status);
public sealed record TransferRuleInfo(string StationId, byte FromLineId, byte ToLineId, string? FromRouteId,
    string? ToRouteId, short WalkSeconds, string Confidence);
