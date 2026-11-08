using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/service-calendar")]
public sealed class ServiceCalendarController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<CalendarDayInfo> Day([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var serviceDate = date ?? DateOnly.FromDateTime(TehranTime.Now(clock));
        var entry = await db.ServiceCalendarDates.AsNoTracking().Where(d => d.ServiceDate == serviceDate)
            .Select(CalendarDayInfo.Projection).SingleOrDefaultAsync(cancellationToken);
        return entry ?? new CalendarDayInfo(serviceDate, ServiceDayType.DefaultCodeFor(serviceDate), null, null, null, []);
    }

    [HttpGet("overrides")]
    public async Task<ActionResult<CalendarDayInfo[]>> Overrides(
        [FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to,
        [FromQuery] bool holidaysOnly,
        CancellationToken cancellationToken)
    {
        if (to!.Value.DayNumber - from!.Value.DayNumber is < 0 or > 365)
            return BadRequest(new ProblemDetails { Detail = "Use an inclusive date range of at most 366 days." });

        return await db.ServiceCalendarDates.AsNoTracking()
            .Where(d => d.ServiceDate >= from.Value && d.ServiceDate <= to.Value && (!holidaysOnly || d.IsOfficialHoliday))
            .OrderBy(d => d.ServiceDate).Select(CalendarDayInfo.Projection).ToArrayAsync(cancellationToken);
    }

    // TODO : Select an authoritative holiday calendar and operator exception source. Keep the Tehran service date
    // as the key, preserve source and verification time, handle corrections idempotently, and define which operator
    // overrides take precedence over public holidays. Until then, expose missing holiday verification as unknown.
}

public sealed record CalendarDayInfo(DateOnly ServiceDate, string DayTypeCode, bool? IsOfficialHoliday, string? Source,
    DateTimeOffset? VerifiedAtUtc, HolidayInfo[] Holidays)
{
    internal static readonly Expression<Func<ServiceCalendarDate, CalendarDayInfo>> Projection = date =>
        new CalendarDayInfo(date.ServiceDate, date.DayTypeCode, date.IsOfficialHoliday, date.Source, date.VerifiedAtUtc,
            date.HolidayEvents.OrderBy(e => e.Id).Select(e => new HolidayInfo(e.Id, e.Title)).ToArray());
}

public sealed record HolidayInfo(string Id, string Title);
