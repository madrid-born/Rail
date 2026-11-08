using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/service-alerts")]
public sealed class ServiceAlertsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public Task<ServiceAlertInfo[]> List(
        [FromQuery] byte? lineId,
        [FromQuery, StringLength(100)] string? stationId,
        [FromQuery] DateTimeOffset? at,
        [FromQuery] bool includeUpcoming = false,
        [FromQuery, Range(0, 100_000)] int offset = 0,
        [FromQuery, Range(1, 200)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var instant = at ?? clock.GetUtcNow();
        var now = clock.GetUtcNow();
        return db.ServiceAlerts.AsNoTracking().AsSplitQuery()
            .Where(a => a.PublishedAtUtc <= now
                && (a.EndsAtUtc == null || a.EndsAtUtc > instant)
                && (includeUpcoming || a.StartsAtUtc <= instant)
                && ((lineId == null && stationId == null)
                    || (!a.Lines.Any() && !a.Stations.Any())
                    || (lineId != null && (a.Lines.Any(l => l.LineId == lineId)
                        || a.Stations.Any(s => s.Station.Lines.Any(l => l.LineId == lineId))))
                    || (stationId != null && (a.Stations.Any(s => s.StationId == stationId)
                        || a.Lines.Any(l => l.Line.Stations.Any(s => s.StationId == stationId))))))
            .OrderByDescending(a => a.Severity).ThenBy(a => a.StartsAtUtc).ThenBy(a => a.Id).Skip(offset).Take(limit)
            .Select(ServiceAlertInfo.Projection).ToArrayAsync(cancellationToken);
    }

    [HttpGet("{alertId:long}")]
    public async Task<ActionResult<ServiceAlertInfo>> Get(long alertId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var alert = await db.ServiceAlerts.AsNoTracking().AsSplitQuery()
            .Where(a => a.Id == alertId && a.PublishedAtUtc <= now).Select(ServiceAlertInfo.Projection)
            .SingleOrDefaultAsync(cancellationToken);
        return alert is null ? NotFound() : alert;
    }

    // TODO : Decide which operator roles can draft, approve, publish and retract alerts. Add audit history and
    // concurrency checks. For subscriptions, agree on consent, delivery channels and deduplication before
    // enqueueing notifications. For rider reports, define moderation, rate limits, visibility and data retention.
}

public sealed record ServiceAlertInfo(long Id, ServiceAlertSeverity Severity, string TitleFa, string? TitleEn,
    string MessageFa, string? MessageEn, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc, DateTimeOffset? PublishedAtUtc,
    string? SourceUrl, int[] LineIds, string[] StationIds)
{
    internal static readonly Expression<Func<ServiceAlert, ServiceAlertInfo>> Projection = alert =>
        new ServiceAlertInfo(alert.Id, alert.Severity, alert.TitleFa, alert.TitleEn, alert.MessageFa, alert.MessageEn,
            alert.StartsAtUtc, alert.EndsAtUtc, alert.PublishedAtUtc, alert.SourceUrl,
            alert.Lines.OrderBy(l => l.LineId).Select(l => (int)l.LineId).ToArray(),
            alert.Stations.OrderBy(s => s.StationId).Select(s => s.StationId).ToArray());
}
