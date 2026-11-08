using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rail.Api.Models.Metro;

public sealed class ScheduleGroup
{
    public short Id { get; set; }
    public required string RouteId { get; set; }
    [Unicode(false), MaxLength(10)] public required string ScheduleKey { get; set; }
    public bool IsBranchService { get; set; }
    public required string FromTerminalId { get; set; }
    public required string ToTerminalId { get; set; }
    public MetroRoute Route { get; set; } = null!;
    public Station FromTerminal { get; set; } = null!;
    public Station ToTerminal { get; set; } = null!;
    public ICollection<ScheduledTrip> Trips { get; } = new List<ScheduledTrip>();
}

public sealed class ScheduledTrip
{
    public long Id { get; set; }
    public short ScheduleGroupId { get; set; }
    public required string DayTypeCode { get; set; }
    public required string DirectionStationId { get; set; }
    public required string OriginStationId { get; set; }
    public required string DestinationStationId { get; set; }
    public bool IsExpress { get; set; }
    public TimeOnly DepartureTime { get; set; }
    public byte DepartureDayOffset { get; set; }
    public TimeOnly ArrivalTime { get; set; }
    public byte ArrivalDayOffset { get; set; }
    public ScheduleGroup ScheduleGroup { get; set; } = null!;
    public ServiceDayType DayType { get; set; } = null!;
    public Station DirectionStation { get; set; } = null!;
    public Station OriginStation { get; set; } = null!;
    public Station DestinationStation { get; set; } = null!;
    public ICollection<ScheduledStopTime> StopTimes { get; } = new List<ScheduledStopTime>();
}

[PrimaryKey(nameof(TripId), nameof(Sequence))]
public sealed class ScheduledStopTime
{
    public long TripId { get; set; }
    public short Sequence { get; set; }
    public required string StationId { get; set; }
    public TimeOnly ArrivalTime { get; set; }
    public byte ArrivalDayOffset { get; set; }
    public TimeOnly? DepartureTime { get; set; }
    public byte? DepartureDayOffset { get; set; }
    public ScheduledTrip Trip { get; set; } = null!;
    public Station Station { get; set; } = null!;
}

public sealed class ServiceDayType
{
    public const string SaturdayToWednesday = "saturday_wednesday";
    public const string Thursday = "thursday";
    public const string FridayOrHoliday = "friday";

    [Key, Unicode(false), MaxLength(30)] public required string Code { get; set; }
    [MaxLength(300)] public required string Description { get; set; }

    public static string DefaultCodeFor(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => FridayOrHoliday,
        _ => SaturdayToWednesday
    };
}

public sealed class ServiceCalendarDate
{
    [Key] public DateOnly ServiceDate { get; set; }
    public required string DayTypeCode { get; set; }
    public bool IsOfficialHoliday { get; set; }
    [MaxLength(100)] public string? Source { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public ServiceDayType DayType { get; set; } = null!;
    public ICollection<HolidayEvent> HolidayEvents { get; } = new List<HolidayEvent>();
}

public sealed class HolidayEvent
{
    [MaxLength(100)] public required string Id { get; set; }
    public DateOnly ServiceDate { get; set; }
    [MaxLength(500)] public required string Title { get; set; }
    public ServiceCalendarDate CalendarDate { get; set; } = null!;
}
