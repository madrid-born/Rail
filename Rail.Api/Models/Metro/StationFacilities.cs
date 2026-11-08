using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rail.Api.Models.Metro;

public enum AmenityType : byte
{
    Restroom = 1,
    Elevator = 2,
    Atm = 3,
    CoffeeShop = 4,
    FastFood = 5,
    GroceryStore = 6,
    FreeWifi = 7,
    PrayerRoom = 8,
    Parking = 9,
    Police = 10
}

[PrimaryKey(nameof(StationId), nameof(Type))]
public sealed class StationAmenity
{
    public required string StationId { get; set; }
    public AmenityType Type { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsVerified { get; set; }
    public Station Station { get; set; } = null!;
}

public sealed class StationEntrance
{
    public int Id { get; set; }
    public required string StationId { get; set; }
    [MaxLength(50)] public required string Code { get; set; }
    [MaxLength(200)] public required string NameFa { get; set; }
    [MaxLength(200)] public string? NameEn { get; set; }
    [MaxLength(500)] public string? AddressFa { get; set; }
    [MaxLength(500)] public string? AddressEn { get; set; }
    [Precision(10, 7)] public decimal? Latitude { get; set; }
    [Precision(10, 7)] public decimal? Longitude { get; set; }
    public bool? IsStepFree { get; set; }
    public Station Station { get; set; } = null!;
}
