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
            .Select(r => new RouteDetails(r.Id, r.LineId, r.BranchCode, r.Stops.OrderBy(s => s.Sequence)
                .Select(s => new RouteStation(s.StationId, s.Sequence, s.Status, s.Station.NameFa, s.Station.NameEn)).ToArray()))
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
public sealed record RouteDetails(string Id, byte LineId, string? BranchCode, RouteStation[] Stops);
public sealed record RouteStation(string StationId, short Sequence, string Status, string NameFa, string NameEn);
public sealed record TrackSegmentInfo(string Id, string RouteId, byte LineId, string FromStationId, string ToStationId, string Status);
public sealed record TransferRuleInfo(string StationId, byte FromLineId, byte ToLineId, string? FromRouteId,
    string? ToRouteId, short WalkSeconds, string Confidence);
