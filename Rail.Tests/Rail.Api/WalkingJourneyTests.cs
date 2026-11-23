using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Rail.Api.Services;
using Rail.Api.Controllers;
using Xunit;

namespace Rail.Tests.Api;

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
        Assert.Equal(910, response.Access.Seconds);
        Assert.Equal(438, response.Exit.Seconds);
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

    [Theory]
    [InlineData(null, "departAt")]
    [InlineData("2026-11-14T11:00", "departAt")]
    [InlineData("2026-11-14T11:00", "arriveBy")]
    [InlineData("2026-11-14T07:30Z", "departAt")]
    [InlineData("2026-12-14T11:00", "departAt")]
    public async Task ControllerUsesStaticCarRoutingForCurrentAndFutureSearches(string? dateTime, string timeMode)
    {
        await using var db = Database();
        await Seed(db);
        var station = (await db.Stations.FindAsync("moniriyeh"))!;
        station.Latitude = 35.68m; station.Longitude = 51.4m;
        await db.SaveChangesAsync();
        using var handler = new RecordingRoutingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:8989/") };
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { }).Build();
        var controller = new NavigationController(db, new JourneyPlanner(db), new FixedClock(), new StreetRouting(http, config, cache));
        await controller.FindWalkingJourney(35.681, 51.401, 35.69, 51.41,
            dateTime: dateTime, timeMode: timeMode, accessMode: "car", exitMode: "car");
        var cars = handler.Uris.Where(uri => uri.Contains("profile=car")).ToArray();
        Assert.Equal(2, cars.Length);
        Assert.All(cars, uri => {
            Assert.Contains("/route?", uri);
            Assert.DoesNotContain("dateTime", uri);
            Assert.DoesNotContain("routingType", uri);
        });
    }

    private sealed class RecordingRoutingHandler : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent("{\"paths\":[]}") });
        }
    }

    [Fact]
    public async Task GraphHopperMillisecondsKeepMetroConnectionsForArrivalDeadline()
    {
        await using var db = Database();
        await Seed(db);
        var origin = (await db.Stations.FindAsync("moniriyeh"))!;
        origin.Latitude = 35.68m; origin.Longitude = 51.4m;
        var destination = (await db.Stations.FindAsync("shahid-ghodousi"))!;
        destination.Latitude = 35.73m; destination.Longitude = 51.44m;
        await db.SaveChangesAsync();
        using var handler = new GraphHopperSecondsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:8989/") };
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { }).Build();
        var controller = new NavigationController(db, new JourneyPlanner(db), new FixedClock(), new StreetRouting(http, config, cache));
        var response = (await controller.FindWalkingJourney(35.6801, 51.4001, 35.7301, 51.4401,
            dateTime: "2026-11-14T10:40", timeMode: "arriveBy", accessMode: "car", exitMode: "walk")).Value!;
        Assert.False(response.WalkOnly);
        Assert.NotEmpty(response.Legs);
        Assert.Equal("graphhopper", response.Access!.Path!.Source);
        Assert.Equal(482, response.Access.Path.BaselineSeconds);
        Assert.Equal("assumed_day_hour_v1", response.Access.Path.TrafficModel);
        Assert.True(response.Access.Path.Seconds > 482);
        Assert.Equal(response.Access.Path.Seconds + 300, response.Access.Seconds); // Includes five minutes to the platform.
        Assert.True(response.ArrivalLocal <= Start.AddMinutes(40));
        Assert.Equal(response.Legs[0].DepartureLocal.AddSeconds(-response.Access.Seconds), response.DepartureLocal);
        Assert.True(response.DepartureLocal < response.Legs[0].DepartureLocal.AddSeconds(-(482 + 300)));
    }

    private sealed class GraphHopperSecondsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            var direct = uri.Contains("point=35.6801,51.4001&point=35.7301,51.4401");
            var duration = uri.Contains("profile=car") ? "482000" : direct ? "7200000" : "584000";
            var json = """{"paths":[{"distance":1758.537,"time":DURATION,"points":{"type":"LineString","coordinates":[[51.4,35.68],[51.44,35.73]]},"instructions":[]}]}""".Replace("DURATION", duration);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredStationsIncludeIntermediateStopsOnTheSameTrain(bool arriveBy)
    {
        await using var db = Database();
        await Seed(db);
        AddTrip(db, 100, 1, ["moniriyeh", "far-exit", "shahid-ghodousi"], [15, 25, 35]);
        AddTrip(db, 101, 1, ["moniriyeh", "shahid-ghodousi"], [20, 30]);
        await db.SaveChangesAsync();
        var planner = new JourneyPlanner(db);
        var access = new Dictionary<string, int> { ["moniriyeh"] = 0 };
        var exit = new Dictionary<string, int> { ["shahid-ghodousi"] = 0 };
        var unrestricted = await planner.FindAsync(access, exit, Start, Start.AddHours(1), 2, default, arriveBy);
        Assert.NotEqual(100, Assert.Single(unrestricted!).TripId);
        var required = await planner.FindAsync(access, exit, Start, Start.AddHours(1), 2, default, arriveBy,
            viaStationIds: ["far-exit", "moniriyeh", "shahid-ghodousi"]);
        Assert.Equal(100, Assert.Single(required!).TripId);
        Assert.Equal(2, required![0].StopCount); // No artificial transfer at the required station.
        Assert.Null(await planner.FindAsync(access, exit, Start, Start.AddHours(1), 2, default, arriveBy,
            viaStationIds: ["nearest"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredStationProgressSurvivesTransfersAndFasterUnconstrainedLabels(bool arriveBy)
    {
        await using var db = Database();
        await Seed(db);
        AddTrip(db, 100, 1, ["moniriyeh", "far-exit", "shahid-ghodousi"], [1, 10, 25]);
        AddTrip(db, 300, 2, ["shahid-ghodousi", "nearest"], [28, 40]);
        await db.SaveChangesAsync();
        var legs = await new JourneyPlanner(db).FindAsync(new Dictionary<string, int> { ["moniriyeh"] = 0 },
            new Dictionary<string, int> { ["nearest"] = 0 }, Start, Start.AddHours(1), 2, default, arriveBy,
            viaStationIds: ["far-exit", "shahid-ghodousi"]);
        Assert.Equal(new long[] { 100, 300 }, legs!.Select(leg => leg.TripId));
    }

    [Fact]
    public async Task RequiredStationsRejectUnknownIdsAndNeverFallBackToDirectWalking()
    {
        await using var db = Database();
        await Seed(db);
        var station = (await db.Stations.FindAsync("moniriyeh"))!;
        station.Latitude = 35.68m; station.Longitude = 51.4m;
        await db.SaveChangesAsync();
        var controller = new NavigationController(db, new JourneyPlanner(db), new FixedClock());
        var unknown = await controller.FindWalkingJourney(35.68, 51.4, 35.68, 51.4, viaStationIds: ["unknown"]);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(unknown.Result);
        var impossible = await controller.FindWalkingJourney(35.68, 51.4, 35.68, 51.4,
            dateTime: "2026-11-14T15:00", viaStationIds: ["far-exit"]);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundObjectResult>(impossible.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoordinateChoicesAreDistinctSortedByTotalTimeAndDefaultToShortest(bool arriveBy)
    {
        await using var db = Database();
        await Seed(db);
        foreach (var (id, lat, lng) in new[] {
            ("moniriyeh", 35.68m, 51.4m), ("nearest", 35.685m, 51.405m),
            ("far-exit", 35.725m, 51.435m), ("shahid-ghodousi", 35.73m, 51.44m) })
        {
            var station = (await db.Stations.FindAsync(id))!;
            station.Latitude = lat; station.Longitude = lng;
        }
        await db.SaveChangesAsync();
        var controller = new NavigationController(db, new JourneyPlanner(db), new FixedClock());
        var result = (await controller.FindWalkingJourney(35.6801, 51.4001, 35.7301, 51.4401,
            dateTime: arriveBy ? "2026-11-14T11:00" : "2026-11-14T10:00", timeMode: arriveBy ? "arriveBy" : "departAt")).Value!;
        Assert.InRange(result.Options.Length, 2, 6);
        Assert.Equal(result.Options.OrderBy(option => option.TotalSeconds), result.Options);
        Assert.Equal(result.Options.Length, result.Options.Select(option => option.Id).Distinct().Count());
        Assert.Equal(result.Options[0].Id, result.Id);
        Assert.Equal(result.Options[0].TotalSeconds, result.TotalSeconds);
        foreach (var option in result.Options)
        {
            Assert.Equal((int)Math.Ceiling((option.ArrivalLocal - option.DepartureLocal).TotalSeconds), option.TotalSeconds);
            if (arriveBy) Assert.True(option.ArrivalLocal <= Start.AddHours(1));
            else Assert.Equal(Start, option.DepartureLocal);
        }
        var required = (await controller.FindWalkingJourney(35.6801, 51.4001, 35.7301, 51.4401,
            viaStationIds: ["far-exit"])).Value!;
        Assert.NotEmpty(required.Options);
        Assert.All(required.Options, option => {
            Assert.False(option.WalkOnly);
            Assert.Contains(option.Legs, leg => leg.ToStationId == "far-exit" || leg.FromStationId == "far-exit");
        });
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 6, 30, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task TimeDependentExitCanChangeTheWinningStationAndUsesTrainArrival()
    {
        await using var db = Database();
        await Seed(db);
        var access = new Dictionary<string, int> { ["moniriyeh"] = 0 };
        var exit = new Dictionary<string, int> { ["far-exit"] = 60, ["shahid-ghodousi"] = 120 };
        var planner = new JourneyPlanner(db);
        var baseline = await planner.FindAlternativesAsync(access, exit, Start, Start.AddHours(1), 2, default);
        Assert.Equal("far-exit", baseline[0][^1].ToStationId);
        var atTimes = new List<DateTime>();
        var adjusted = await planner.FindAlternativesAsync(access, exit, Start, Start.AddHours(1), 2, default,
            exitDuration: (station, at, backwards) => {
                Assert.False(backwards); atTimes.Add(at);
                return station == "far-exit" ? 1200 : 120;
            });
        Assert.Equal("shahid-ghodousi", adjusted[0][^1].ToStationId);
        Assert.Contains(Start.AddMinutes(20), atTimes);
    }

    [Fact]
    public async Task ReverseSearchUsesDeadlineForExitAndBoardingTimeForAccess()
    {
        await using var db = Database();
        await Seed(db);
        var access = new Dictionary<string, int> { ["moniriyeh"] = 60 };
        var exit = new Dictionary<string, int> { ["shahid-ghodousi"] = 60 };
        var deadline = Start.AddMinutes(30);
        var accessTimes = new List<DateTime>();
        var routes = await new JourneyPlanner(db).FindAlternativesAsync(access, exit, Start, deadline, 2, default,
            arriveBy: true,
            accessDuration: (_, at, backwards) => { Assert.True(backwards); accessTimes.Add(at); return 600; },
            exitDuration: (_, at, backwards) => { if (backwards) Assert.Equal(deadline, at); return 600; });
        // Trip 1's 10:05 departure is unreachable from 10:00 with ten minutes of access.
        // Trip 2 arrives at 10:25, too late for ten minutes of exit before 10:30.
        Assert.Empty(routes);
        Assert.Contains(Start.AddMinutes(5), accessTimes);
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
