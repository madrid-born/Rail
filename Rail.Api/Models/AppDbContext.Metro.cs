using Microsoft.EntityFrameworkCore;
using Rail.Api.Models.Metro;

namespace Rail.Api.Models;

public partial class AppDbContext
{
    private const string MetroSchema = "Metro";

    public DbSet<MetroLine> MetroLines => Set<MetroLine>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationLine> StationLines => Set<StationLine>();
    public DbSet<MetroRoute> MetroRoutes => Set<MetroRoute>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<TrackSegment> TrackSegments => Set<TrackSegment>();
    public DbSet<TransferRule> TransferRules => Set<TransferRule>();
    public DbSet<ScheduleGroup> ScheduleGroups => Set<ScheduleGroup>();
    public DbSet<ScheduledTrip> ScheduledTrips => Set<ScheduledTrip>();
    public DbSet<ScheduledStopTime> ScheduledStopTimes => Set<ScheduledStopTime>();
    public DbSet<ServiceDayType> ServiceDayTypes => Set<ServiceDayType>();
    public DbSet<ServiceCalendarDate> ServiceCalendarDates => Set<ServiceCalendarDate>();
    public DbSet<HolidayEvent> HolidayEvents => Set<HolidayEvent>();
    public DbSet<StationAmenity> StationAmenities => Set<StationAmenity>();
    public DbSet<StationEntrance> StationEntrances => Set<StationEntrance>();
    public DbSet<ServiceAlert> ServiceAlerts => Set<ServiceAlert>();
    public DbSet<FareProduct> FareProducts => Set<FareProduct>();
    public DbSet<FarePrice> FarePrices => Set<FarePrice>();
    public DbSet<FavoriteStation> FavoriteStations => Set<FavoriteStation>();

    public async Task<string> GetServiceDayTypeCodeAsync(DateOnly serviceDate, CancellationToken cancellationToken = default)
    {
        var calendarCode = await ServiceCalendarDates
            .Where(date => date.ServiceDate == serviceDate)
            .Select(date => date.DayTypeCode)
            .SingleOrDefaultAsync(cancellationToken);

        return calendarCode ?? ServiceDayType.DefaultCodeFor(serviceDate);
    }

    private static void ConfigureMetro(ModelBuilder builder)
    {
        ConfigureNetwork(builder);
        ConfigureTimetable(builder);
        ConfigureRiderInformation(builder);
    }

    private static void ConfigureNetwork(ModelBuilder builder)
    {
        builder.Entity<MetroLine>(line =>
        {
            line.ToTable("Lines", MetroSchema, table => table.HasCheckConstraint("CK_Lines_Color",
                "[Color] LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'"));
            line.Property(x => x.Id).ValueGeneratedNever();
            line.Property(x => x.Color).IsFixedLength();
        });

        builder.Entity<Station>(station =>
        {
            station.ToTable("Stations", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_Stations_Latitude", "[Latitude] BETWEEN -90 AND 90");
                table.HasCheckConstraint("CK_Stations_Longitude", "[Longitude] BETWEEN -180 AND 180");
                table.HasCheckConstraint("CK_Stations_Status",
                    "[InfrastructureStatus] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
            });
        });

        builder.Entity<StationLine>(membership =>
        {
            membership.ToTable("StationLines", MetroSchema);
            membership.HasOne(x => x.Station).WithMany(x => x.Lines).OnDelete(DeleteBehavior.NoAction);
            membership.HasOne(x => x.Line).WithMany(x => x.Stations).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<MetroRoute>(route =>
        {
            route.ToTable("Routes", MetroSchema);
            route.HasAlternateKey(x => new { x.Id, x.LineId });
            route.HasOne(x => x.Line).WithMany(x => x.Routes).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<RouteStop>(stop =>
        {
            stop.ToTable("RouteStops", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_RouteStops_Sequence", "[Sequence] > 0");
                table.HasCheckConstraint("CK_RouteStops_Status",
                    "[Status] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
            });
            stop.HasOne(x => x.Route).WithMany(x => x.Stops).OnDelete(DeleteBehavior.NoAction);
            stop.HasOne(x => x.Station).WithMany().OnDelete(DeleteBehavior.NoAction);
            stop.HasIndex(x => new { x.StationId, x.RouteId, x.Sequence });
        });

        builder.Entity<TrackSegment>(segment =>
        {
            segment.ToTable("TrackSegments", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_TrackSegments_Endpoints", "[FromStationId] <> [ToStationId]");
                table.HasCheckConstraint("CK_TrackSegments_Status",
                    "[Status] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
            });
            segment.HasOne(x => x.Route).WithMany().OnDelete(DeleteBehavior.NoAction);
            segment.HasOne(x => x.FromStation).WithMany().OnDelete(DeleteBehavior.NoAction);
            segment.HasOne(x => x.ToStation).WithMany().OnDelete(DeleteBehavior.NoAction);
            segment.HasIndex(x => new { x.FromStationId, x.Status });
            segment.HasIndex(x => new { x.ToStationId, x.Status });
        });

        builder.Entity<TransferRule>(rule =>
        {
            rule.ToTable("TransferRules", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_TransferRules_WalkSeconds", "[WalkSeconds] >= 0");
                table.HasCheckConstraint("CK_TransferRules_DifferentServices",
                    "[FromLineId] <> [ToLineId] OR ([FromRouteId] IS NOT NULL AND [ToRouteId] IS NOT NULL AND [FromRouteId] <> [ToRouteId])");
            });
            rule.Property(x => x.Id).ValueGeneratedNever();
            rule.HasOne(x => x.FromLine).WithMany().HasForeignKey(x => new { x.StationId, x.FromLineId }).OnDelete(DeleteBehavior.NoAction);
            rule.HasOne(x => x.ToLine).WithMany().HasForeignKey(x => new { x.StationId, x.ToLineId }).OnDelete(DeleteBehavior.NoAction);
            rule.HasOne(x => x.FromRoute).WithMany().HasForeignKey(x => new { x.FromRouteId, x.FromLineId })
                .HasPrincipalKey(x => new { x.Id, x.LineId }).OnDelete(DeleteBehavior.NoAction);
            rule.HasOne(x => x.ToRoute).WithMany().HasForeignKey(x => new { x.ToRouteId, x.ToLineId })
                .HasPrincipalKey(x => new { x.Id, x.LineId }).OnDelete(DeleteBehavior.NoAction);
            rule.HasIndex(x => new { x.StationId, x.FromLineId, x.ToLineId, x.FromRouteId, x.ToRouteId })
                .IsUnique().HasFilter(null);
        });
    }

    private static void ConfigureTimetable(ModelBuilder builder)
    {
        builder.Entity<ScheduleGroup>(group =>
        {
            group.ToTable("ScheduleGroups", MetroSchema);
            group.Property(x => x.Id).ValueGeneratedNever();
            group.HasOne(x => x.Route).WithMany().OnDelete(DeleteBehavior.NoAction);
            group.HasOne(x => x.FromTerminal).WithMany().HasForeignKey(x => x.FromTerminalId).OnDelete(DeleteBehavior.NoAction);
            group.HasOne(x => x.ToTerminal).WithMany().HasForeignKey(x => x.ToTerminalId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ScheduledTrip>(trip =>
        {
            trip.ToTable("Trips", MetroSchema, table => table.HasCheckConstraint("CK_Trips_Arrival",
                "[ArrivalDayOffset] > [DepartureDayOffset] OR ([ArrivalDayOffset] = [DepartureDayOffset] AND [ArrivalTime] >= [DepartureTime])"));
            trip.Property(x => x.Id).ValueGeneratedNever();
            trip.HasOne(x => x.ScheduleGroup).WithMany(x => x.Trips).OnDelete(DeleteBehavior.NoAction);
            trip.HasOne(x => x.DayType).WithMany().HasForeignKey(x => x.DayTypeCode).OnDelete(DeleteBehavior.NoAction);
            trip.HasOne(x => x.DirectionStation).WithMany().OnDelete(DeleteBehavior.NoAction);
            trip.HasOne(x => x.OriginStation).WithMany().OnDelete(DeleteBehavior.NoAction);
            trip.HasOne(x => x.DestinationStation).WithMany().OnDelete(DeleteBehavior.NoAction);
            trip.HasIndex(x => new { x.ScheduleGroupId, x.DayTypeCode });
        });

        builder.Entity<ScheduledStopTime>(stop =>
        {
            stop.ToTable("StopTimes", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_StopTimes_Sequence", "[Sequence] > 0");
                table.HasCheckConstraint("CK_StopTimes_Departure", """
                    ([DepartureTime] IS NULL AND [DepartureDayOffset] IS NULL) OR
                    ([DepartureTime] IS NOT NULL AND [DepartureDayOffset] IS NOT NULL AND
                        ([DepartureDayOffset] > [ArrivalDayOffset] OR
                         ([DepartureDayOffset] = [ArrivalDayOffset] AND [DepartureTime] >= [ArrivalTime])))
                    """);
            });
            stop.HasOne(x => x.Trip).WithMany(x => x.StopTimes).OnDelete(DeleteBehavior.Cascade);
            stop.HasOne(x => x.Station).WithMany().OnDelete(DeleteBehavior.NoAction);
            stop.HasIndex(x => new { x.StationId, x.DepartureDayOffset, x.DepartureTime });
        });

        builder.Entity<ServiceDayType>(type =>
        {
            type.ToTable("ServiceDayTypes", MetroSchema);
            type.HasData(
                new ServiceDayType { Code = ServiceDayType.SaturdayToWednesday, Description = "Saturday through Wednesday timetable" },
                new ServiceDayType { Code = ServiceDayType.Thursday, Description = "Thursday timetable" },
                new ServiceDayType { Code = ServiceDayType.FridayOrHoliday, Description = "Friday and confirmed official-holiday timetable" });
        });

        builder.Entity<ServiceCalendarDate>(date =>
        {
            date.ToTable("ServiceCalendarDates", MetroSchema);
            date.HasOne(x => x.DayType).WithMany().HasForeignKey(x => x.DayTypeCode).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<HolidayEvent>(holiday =>
        {
            holiday.ToTable("HolidayEvents", MetroSchema);
            holiday.HasOne(x => x.CalendarDate).WithMany(x => x.HolidayEvents).HasForeignKey(x => x.ServiceDate)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRiderInformation(ModelBuilder builder)
    {
        builder.Entity<StationAmenity>(amenity =>
        {
            amenity.ToTable("StationAmenities", MetroSchema, table => table.HasCheckConstraint("CK_StationAmenities_Type", "[Type] BETWEEN 1 AND 10"));
            amenity.HasOne(x => x.Station).WithMany(x => x.Amenities).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StationEntrance>(entrance =>
        {
            entrance.ToTable("StationEntrances", MetroSchema, table => table.HasCheckConstraint("CK_StationEntrances_Coordinates", """
                ([Latitude] IS NULL AND [Longitude] IS NULL) OR
                ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)
                """));
            entrance.HasOne(x => x.Station).WithMany(x => x.Entrances).OnDelete(DeleteBehavior.Cascade);
            entrance.HasIndex(x => new { x.StationId, x.Code }).IsUnique();
        });

        builder.Entity<ServiceAlert>(alert =>
        {
            alert.ToTable("ServiceAlerts", MetroSchema, table =>
            {
                table.HasCheckConstraint("CK_ServiceAlerts_Dates", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
                table.HasCheckConstraint("CK_ServiceAlerts_Severity", "[Severity] BETWEEN 1 AND 4");
            });
            alert.HasIndex(x => x.StartsAtUtc).HasFilter("[PublishedAtUtc] IS NOT NULL");
        });

        builder.Entity<ServiceAlertLine>(target =>
        {
            target.ToTable("ServiceAlertLines", MetroSchema);
            target.HasOne(x => x.Alert).WithMany(x => x.Lines).OnDelete(DeleteBehavior.Cascade);
            target.HasOne(x => x.Line).WithMany().OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ServiceAlertStation>(target =>
        {
            target.ToTable("ServiceAlertStations", MetroSchema);
            target.HasOne(x => x.Alert).WithMany(x => x.Stations).OnDelete(DeleteBehavior.Cascade);
            target.HasOne(x => x.Station).WithMany().OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<FareProduct>(product =>
        {
            product.ToTable("FareProducts", MetroSchema);
            product.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<FarePrice>(price =>
        {
            price.ToTable("FarePrices", MetroSchema, table => table.HasCheckConstraint("CK_FarePrices_Amount", "[AmountRials] >= 0"));
            price.HasOne(x => x.FareProduct).WithMany(x => x.Prices).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<FavoriteStation>(favorite =>
        {
            favorite.ToTable("FavoriteStations", MetroSchema);
            favorite.HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            favorite.HasOne(x => x.Station).WithMany().OnDelete(DeleteBehavior.NoAction);
        });
    }
}
