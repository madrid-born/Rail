using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Rail.Api.Services;
using Rail.Api.Controllers;
using Xunit;

namespace Rail.Api.Tests;

public sealed class WalkingJourneyTests
{
    private static readonly DateTime Start = new(2026, 11, 14, 10, 0, 0);

    [Fact]
    public void BfsAndDfsFromBothEndsExcludeDisconnectedCandidatesAndHandleCycles()
    {
        var graph = new Dictionary<string, HashSet<string>> {
            ["a"] = ["b", "c"], ["b"] = ["a", "c"], ["c"] = ["a", "b"], ["isolated"] = [] };
        var (origins, destinations) = WalkingJourney.ConnectCandidates(graph,
            [new("a", 10, 10), new("isolated", 1, 1)], [new("c", 10, 10)]);
        Assert.Equal("a", Assert.Single(origins).StationId);
        Assert.Equal("c", Assert.Single(destinations).StationId);
        Assert.Equal(3, WalkingJourney.Connected(graph, ["a"], true).Count);
        Assert.Equal(3, WalkingJourney.Connected(graph, ["c"], false).Count);
    }

    [Fact]
    public async Task WalkingToLineThreeBeatsBoardingTheNearestStationWithALaterArrival()
    {
        await using var db = Database();
        await Seed(db);
        var planner = new JourneyPlanner(db);
        var legs = await planner.FindAsync(new Dictionary<string, int> { ["nearest"] = 60, ["moniriyeh"] = 600 },
            new Dictionary<string, int> { ["shahid-ghodousi"] = 180, ["far-exit"] = 1200 },
            Start, Start.AddHours(6), 5, default);
        var leg = Assert.Single(legs!);
        Assert.Equal((byte)3, leg.LineId);
        Assert.Equal("moniriyeh", leg.FromStationId);
        Assert.Equal("shahid-ghodousi", leg.ToStationId);
        Assert.Equal(2, leg.TripId); // Trip 1 departs before the user can reach the platform.
        Assert.Equal(Start.AddMinutes(11), leg.DepartureLocal);
        Assert.Equal(Start.AddMinutes(10), leg.BoardingArrivalLocal);
        Assert.Equal("not_departed", leg.TrainPosition!.Status);
    }

    [Fact]
    public async Task DestinationWalkingCanChangeTheWinningTrainAndReverseTravelUsesItsOwnSchedule()
    {
        await using var db = Database();
        await Seed(db);
        var planner = new JourneyPlanner(db);
        var legs = await planner.FindAsync(new Dictionary<string, int> { ["moniriyeh"] = 600 },
            new Dictionary<string, int> { ["far-exit"] = 1200, ["shahid-ghodousi"] = 120 },
            Start, Start.AddHours(6), 5, default);
        Assert.Equal("shahid-ghodousi", Assert.Single(legs!).ToStationId);
        var reverse = await planner.FindAsync("shahid-ghodousi", "moniriyeh", Start, Start.AddHours(6), 5, default);
        Assert.Equal(5, Assert.Single(reverse!).TripId);
        Assert.Equal("moniriyeh", reverse![0].DirectionStationId);
    }

    [Fact]
    public async Task CoordinateEndpointChoosesMoniriyehToGhodousiForTheReportedPoints()
    {
        await using var db = Database();
        await Seed(db);
        var moniriyeh = (await db.Stations.FindAsync("moniriyeh"))!;
        moniriyeh.Latitude = 35.681087m; moniriyeh.Longitude = 51.401594m;
        var ghodousi = (await db.Stations.FindAsync("shahid-ghodousi"))!;
        ghodousi.Latitude = 35.731553m; ghodousi.Longitude = 51.4445m;
        var nearest = (await db.Stations.FindAsync("nearest"))!;
        nearest.Latitude = 35.6863779m; nearest.Longitude = 51.4088914m;
        AddTrip(db, 6, 1, ["moniriyeh", "shahid-ghodousi"], [20, 40]);
        AddTrip(db, 7, 2, ["nearest", "shahid-ghodousi"], [10, 50]);
        await db.SaveChangesAsync();
        var controller = new NavigationController(db, new JourneyPlanner(db), new FixedClock());
        var response = (await controller.FindWalkingJourney(35.68432996036621, 51.40671214211443,
            35.73258986975962, 51.44378785188508)).Value!;
        Assert.False(response.WalkOnly);
        Assert.Equal("moniriyeh", response.Access!.StationId);
        Assert.Equal("shahid-ghodousi", response.Exit!.StationId);
        Assert.Equal((byte)3, Assert.Single(response.Legs).LineId);
        Assert.Equal(730, response.Access.Seconds);
        Assert.Equal(258, response.Exit.Seconds);
        Assert.Equal("timetable", response.DataSource);
        var foot = (await controller.FindWalkingJourney(35.68432996036621, 51.40671214211443,
            35.68432996036621, 51.40671214211443)).Value!;
        Assert.True(foot.WalkOnly);
        Assert.Null(foot.Access);
        Assert.Null(foot.Exit);
        Assert.Empty(foot.Legs);
        Assert.Equal(0, foot.DirectWalkSeconds);
    }

    [Fact]
    public async Task TrainPositionUsesScheduledDwellAndMovementAtTheRequestedTime()
    {
        await using var db = Database();
        await Seed(db);
        var planner = new JourneyPlanner(db);
        var dwell = await planner.FindAsync("moniriyeh", "shahid-ghodousi", Start.AddMinutes(4.5), Start.AddHours(1), 5, default);
        Assert.Equal("at_station", dwell![0].TrainPosition!.Status);
        Assert.Equal("moniriyeh", dwell[0].TrainPosition!.StationId);
        AddTrip(db, 6, 1, ["moniriyeh", "far-exit", "shahid-ghodousi"], [-10, 5, 20]);
        await db.SaveChangesAsync();
        var moving = await planner.FindAsync("far-exit", "shahid-ghodousi", Start, Start.AddHours(1), 5, default);
        Assert.Equal("between_stations", moving![0].TrainPosition!.Status);
        Assert.Equal("moniriyeh", moving[0].TrainPosition!.FromStationId);
        Assert.Equal("far-exit", moving[0].TrainPosition!.ToStationId);
    }

    [Fact]
    public async Task ArriveByFindsLatestDepartureIncludingAccessAndExitAndRejectsLateTrains()
    {
        await using var db = Database();
        await Seed(db);
        var planner = new JourneyPlanner(db);
        var access = new Dictionary<string, int> { ["moniriyeh"] = 600 };
        var exit = new Dictionary<string, int> { ["shahid-ghodousi"] = 180 };
        var legs = await planner.FindAsync(access, exit, Start.AddHours(-1), Start.AddMinutes(28), 2, default, true, true);
        Assert.Equal(2, Assert.Single(legs!).TripId);
        Assert.Equal(Start.AddMinutes(1), legs![0].DepartureLocal.AddSeconds(-600));
        Assert.Equal(Start.AddMinutes(28), legs[^1].ArrivalLocal.AddSeconds(180));
        var earlier = await planner.FindAsync(access, exit, Start.AddHours(-1), Start.AddMinutes(23), 2, default, true, true);
        Assert.Equal(1, Assert.Single(earlier!).TripId);
        var impossible = await planner.FindAsync(access, exit, Start, Start.AddMinutes(23), 2, default, true, true);
        Assert.Null(impossible);
    }

    [Fact]
    public async Task TwoMinuteTransfersAreAppliedInBothSearchDirections()
    {
        await using var db = Database();
        await Seed(db);
        AddTrip(db, 8, 2, ["nearest", "shahid-ghodousi"], [0, 13]);
        db.TransferRules.Add(new TransferRule { StationId = "shahid-ghodousi", FromLineId = 2, ToLineId = 3,
            WalkSeconds = 600, Confidence = "estimated" });
        await db.SaveChangesAsync();
        var planner = new JourneyPlanner(db);
        var access = new Dictionary<string, int> { ["nearest"] = 0 };
        var exit = new Dictionary<string, int> { ["moniriyeh"] = 0 };
        foreach (var reverse in new[] { false, true })
        {
            var legs = await planner.FindAsync(access, exit, Start, Start.AddMinutes(30), 2, default, reverse, true);
            Assert.Equal(new long[] { 8, 5 }, legs!.Select(leg => leg.TripId));
            Assert.Null(legs![0].Transfer);
            Assert.Equal(120, legs[1].Transfer!.MinimumSeconds);
            Assert.Equal("user_selected", legs[1].Transfer!.Confidence);
        }
        Assert.Null(await planner.FindAsync(access, exit, Start, Start.AddMinutes(30), 5, default));
    }

    [Fact]
    public async Task ArriveByIncludesPreviousServiceDateAcrossMidnight()
    {
        await using var db = Database();
        await Seed(db);
        db.ScheduledTrips.Add(new ScheduledTrip { Id = 99, ScheduleGroupId = 1,
            DayTypeCode = ServiceDayType.SaturdayToWednesday, OriginStationId = "moniriyeh",
            DestinationStationId = "shahid-ghodousi", DirectionStationId = "shahid-ghodousi",
            DepartureTime = new(23, 50), ArrivalTime = new(0, 20), ArrivalDayOffset = 1 });
        db.ScheduledStopTimes.AddRange(new ScheduledStopTime { TripId = 99, Sequence = 1, StationId = "moniriyeh",
                ArrivalTime = new(23, 49), DepartureTime = new(23, 50), DepartureDayOffset = 0 },
            new ScheduledStopTime { TripId = 99, Sequence = 2, StationId = "shahid-ghodousi",
                ArrivalTime = new(0, 20), ArrivalDayOffset = 1 });
        await db.SaveChangesAsync();
        var start = Start.Date.AddHours(23);
        var deadline = Start.Date.AddDays(1).AddMinutes(25);
        var legs = await new JourneyPlanner(db).FindAsync(new Dictionary<string, int> { ["moniriyeh"] = 300 },
            new Dictionary<string, int> { ["shahid-ghodousi"] = 120 }, start, deadline, 2, default, true, true);
        Assert.Equal(99, Assert.Single(legs!).TripId);
        Assert.Equal(Start.Date.AddHours(23).AddMinutes(45), legs![0].DepartureLocal.AddSeconds(-300));
        Assert.Equal(Start.Date.AddDays(1).AddMinutes(22), legs[0].ArrivalLocal.AddSeconds(120));
    }

    [Fact]
    public async Task NoUsableDepartureReturnsNoTransitJourney()
    {
        await using var db = Database();
        await Seed(db);
        var legs = await new JourneyPlanner(db).FindAsync("moniriyeh", "shahid-ghodousi",
            Start.AddHours(3), Start.AddHours(4), 5, default);
        Assert.Null(legs);
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 6, 30, 0, TimeSpan.Zero);
    }

    private static async Task Seed(AppDbContext db)
    {
        db.MetroLines.AddRange(new MetroLine { Id = 2, NameFa = "۲", NameEn = "2", Color = "#0000ff" },
            new MetroLine { Id = 3, NameFa = "۳", NameEn = "3", Color = "#00ffff" });
        foreach (var id in new[] { "nearest", "moniriyeh", "far-exit", "shahid-ghodousi" })
            db.Stations.Add(new Station { Id = id, NameFa = id, NameEn = id, InfrastructureStatus = "operational" });
        AddRoute(db, 1, "line-three", 3, ["moniriyeh", "far-exit", "shahid-ghodousi"]);
        AddRoute(db, 2, "nearest-line", 2, ["nearest", "shahid-ghodousi"]);
        AddTrip(db, 1, 1, ["moniriyeh", "shahid-ghodousi"], [5, 20]);
        AddTrip(db, 2, 1, ["moniriyeh", "shahid-ghodousi"], [11, 25]);
        AddTrip(db, 3, 2, ["nearest", "shahid-ghodousi"], [2, 50]);
        AddTrip(db, 4, 1, ["moniriyeh", "far-exit"], [12, 20]);
        AddTrip(db, 5, 1, ["shahid-ghodousi", "moniriyeh"], [15, 30]);
        await db.SaveChangesAsync();
    }

    private static void AddRoute(AppDbContext db, short group, string route, byte line, string[] ids)
    {
        db.MetroRoutes.Add(new MetroRoute { Id = route, LineId = line });
        db.ScheduleGroups.Add(new ScheduleGroup { Id = group, RouteId = route, ScheduleKey = "test",
            FromTerminalId = ids[0], ToTerminalId = ids[^1] });
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            db.StationLines.Add(new StationLine { StationId = id, LineId = line, IsOperational = true });
            db.RouteStops.Add(new RouteStop { RouteId = route, StationId = id, Sequence = (short)(index + 1), Status = "operational" });
        }
    }

    private static void AddTrip(AppDbContext db, long id, short group, string[] stations, int[] minutes)
    {
        db.ScheduledTrips.Add(new ScheduledTrip { Id = id, ScheduleGroupId = group,
            DayTypeCode = ServiceDayType.SaturdayToWednesday, DirectionStationId = stations[^1],
            OriginStationId = stations[0], DestinationStationId = stations[^1],
            DepartureTime = TimeOnly.FromDateTime(Start.AddMinutes(minutes[0])),
            ArrivalTime = TimeOnly.FromDateTime(Start.AddMinutes(minutes[^1])) });
        for (short index = 0; index < stations.Length; index++)
            db.ScheduledStopTimes.Add(new ScheduledStopTime { TripId = id, Sequence = (short)(index + 1),
                StationId = stations[index], ArrivalTime = TimeOnly.FromDateTime(Start.AddMinutes(minutes[index] - (index == 0 ? 1 : 0))),
                DepartureTime = index == stations.Length - 1 ? null : TimeOnly.FromDateTime(Start.AddMinutes(minutes[index])),
                DepartureDayOffset = index == stations.Length - 1 ? null : (byte)0 });
    }
}
