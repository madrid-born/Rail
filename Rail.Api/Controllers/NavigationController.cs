using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/navigation")]
public sealed class NavigationController(AppDbContext db, JourneyPlanner planner, TimeProvider clock) : ControllerBase
{
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
