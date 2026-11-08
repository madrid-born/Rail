using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rail.Api.Models.Metro;

public sealed class MetroLine
{
    public byte Id { get; set; }
    [MaxLength(100)] public required string NameFa { get; set; }
    [MaxLength(100)] public required string NameEn { get; set; }
    [Unicode(false), MaxLength(7)] public required string Color { get; set; }
    public ICollection<MetroRoute> Routes { get; } = new List<MetroRoute>();
    public ICollection<StationLine> Stations { get; } = new List<StationLine>();
}

public sealed class Station
{
    [Unicode(false), MaxLength(100)] public required string Id { get; set; }
    [MaxLength(200)] public required string NameFa { get; set; }
    [MaxLength(200)] public required string NameEn { get; set; }
    [Precision(10, 7)] public decimal Latitude { get; set; }
    [Precision(10, 7)] public decimal Longitude { get; set; }
    [Unicode(false), MaxLength(30)] public required string InfrastructureStatus { get; set; }
    public ICollection<StationLine> Lines { get; } = new List<StationLine>();
    public ICollection<StationEntrance> Entrances { get; } = new List<StationEntrance>();
    public ICollection<StationAmenity> Amenities { get; } = new List<StationAmenity>();
}

[PrimaryKey(nameof(StationId), nameof(LineId))]
public sealed class StationLine
{
    public required string StationId { get; set; }
    public byte LineId { get; set; }
    public bool IsOperational { get; set; }
    public Station Station { get; set; } = null!;
    public MetroLine Line { get; set; } = null!;
}

public sealed class MetroRoute
{
    [Unicode(false), MaxLength(100)] public required string Id { get; set; }
    public byte LineId { get; set; }
    [Unicode(false), MaxLength(50)] public string? BranchCode { get; set; }
    public MetroLine Line { get; set; } = null!;
    public ICollection<RouteStop> Stops { get; } = new List<RouteStop>();
}

[PrimaryKey(nameof(RouteId), nameof(Sequence))]
public sealed class RouteStop
{
    public required string RouteId { get; set; }
    public short Sequence { get; set; }
    public required string StationId { get; set; }
    [Unicode(false), MaxLength(30)] public required string Status { get; set; }
    public MetroRoute Route { get; set; } = null!;
    public Station Station { get; set; } = null!;
}

public sealed class TrackSegment
{
    [Unicode(false), MaxLength(220)] public required string Id { get; set; }
    public required string RouteId { get; set; }
    public required string FromStationId { get; set; }
    public required string ToStationId { get; set; }
    [Unicode(false), MaxLength(30)] public required string Status { get; set; }
    public MetroRoute Route { get; set; } = null!;
    public Station FromStation { get; set; } = null!;
    public Station ToStation { get; set; } = null!;
}

public sealed class TransferRule
{
    public short Id { get; set; }
    public required string StationId { get; set; }
    public byte FromLineId { get; set; }
    public byte ToLineId { get; set; }
    public string? FromRouteId { get; set; }
    public string? ToRouteId { get; set; }
    public short WalkSeconds { get; set; }
    [Unicode(false), MaxLength(20)] public required string Confidence { get; set; }
    public StationLine FromLine { get; set; } = null!;
    public StationLine ToLine { get; set; } = null!;
    public MetroRoute? FromRoute { get; set; }
    public MetroRoute? ToRoute { get; set; }
}
