using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rail.Api.Models.Metro;

public enum ServiceAlertSeverity : byte
{
    Information = 1,
    Delay = 2,
    Disruption = 3,
    Closure = 4
}

public sealed class ServiceAlert
{
    public long Id { get; set; }
    public ServiceAlertSeverity Severity { get; set; }
    [MaxLength(200)] public required string TitleFa { get; set; }
    [MaxLength(200)] public string? TitleEn { get; set; }
    [MaxLength(4000)] public required string MessageFa { get; set; }
    [MaxLength(4000)] public string? MessageEn { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    [MaxLength(500)] public string? SourceUrl { get; set; }
    public ICollection<ServiceAlertLine> Lines { get; } = new List<ServiceAlertLine>();
    public ICollection<ServiceAlertStation> Stations { get; } = new List<ServiceAlertStation>();
}

[PrimaryKey(nameof(AlertId), nameof(LineId))]
public sealed class ServiceAlertLine
{
    public long AlertId { get; set; }
    public byte LineId { get; set; }
    public ServiceAlert Alert { get; set; } = null!;
    public MetroLine Line { get; set; } = null!;
}

[PrimaryKey(nameof(AlertId), nameof(StationId))]
public sealed class ServiceAlertStation
{
    public long AlertId { get; set; }
    public required string StationId { get; set; }
    public ServiceAlert Alert { get; set; } = null!;
    public Station Station { get; set; } = null!;
}

public sealed class FareProduct
{
    public int Id { get; set; }
    [Unicode(false), MaxLength(50)] public required string Code { get; set; }
    [MaxLength(200)] public required string NameFa { get; set; }
    [MaxLength(200)] public string? NameEn { get; set; }
    [MaxLength(1000)] public string? DescriptionFa { get; set; }
    [MaxLength(1000)] public string? DescriptionEn { get; set; }
    public ICollection<FarePrice> Prices { get; } = new List<FarePrice>();
}

[PrimaryKey(nameof(FareProductId), nameof(EffectiveFrom))]
public sealed class FarePrice
{
    public int FareProductId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public long AmountRials { get; set; }
    [MaxLength(500)] public string? SourceUrl { get; set; }
    public FareProduct FareProduct { get; set; } = null!;
}

[PrimaryKey(nameof(UserId), nameof(StationId))]
public sealed class FavoriteStation
{
    [MaxLength(450)] public required string UserId { get; set; }
    public required string StationId { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
