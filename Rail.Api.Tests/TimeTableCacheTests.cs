using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rail.Api.Models;
using Rail.Api.Controllers;
using Rail.Api.Models.Metro;
using Rail.Api.Services;
using Xunit;

namespace Rail.Api.Tests;

public class TimeTableCacheTests
{
    private const string Weekday = ServiceDayType.SaturdayToWednesday;

    [Theory]
    [InlineData("2026-11-11", false, Weekday)]
    [InlineData("2026-11-12", false, ServiceDayType.Thursday)]
    [InlineData("2026-11-13", false, ServiceDayType.FridayOrHoliday)]
    [InlineData("2026-11-14", false, Weekday)]
    [InlineData("2026-11-11", true, ServiceDayType.FridayOrHoliday)]
    [InlineData("2026-11-12", true, ServiceDayType.FridayOrHoliday)]
    public async Task HolidaysTakePriorityThenWeekdaySelectsTrips(string date, bool holiday, string code)
    {
        await using var app = new CacheApp();
        app.Holidays.Reply = _ => Json(new { is_holiday = holiday });
        await app.Seed(db =>
        {
            AddTrip(db, 1, Weekday);
            AddTrip(db, 2, ServiceDayType.Thursday);
            AddTrip(db, 3, ServiceDayType.FridayOrHoliday);
        });
        using var client = app.Client();
        var data = await Read(client, date + "T10:11:00");
        Assert.Equal(code switch { Weekday => 1, ServiceDayType.Thursday => 2, _ => 3 }, Assert.Single(data).Id);
        Assert.Equal("https://holidayapi.ir/gregorian/" + date.Replace('-', '/').Replace("/0", "/"),
            Assert.Single(app.Holidays.Calls));
    }

    [Theory]
    [InlineData("09:59:00", null, null, null)]
    [InlineData("10:00:00", null, "a", "b")]
    [InlineData("10:05:00", null, "a", "b")]
    [InlineData("10:10:00", "b", null, null)]
    [InlineData("10:11:59", "b", null, null)]
    [InlineData("10:12:00", null, "b", "c")]
    public async Task PositionUsesScheduledArrivalDepartureBoundaries(string time,
        string? station, string? from, string? to)
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        var data = await Read(client, "2026-11-11T" + time);
        if (station is null && from is null && to is null)
        {
            Assert.Empty(data);
            return;
        }
        var trip = Assert.Single(data);
        Assert.Equal(station, trip.Position.Station);
        Assert.Equal(from, trip.Position.FromStation);
        Assert.Equal(to, trip.Position.ToStation);
    }

    [Theory]
    [InlineData("10:20:00")]
    [InlineData("10:20:01")]
    public async Task FinishedTripsAreExcludedIncludingExactTerminalArrival(string time)
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        Assert.Empty(await Read(client, "2026-11-11T" + time));
    }

    [Fact]
    public async Task AllActiveTripsAreReturnedWithoutPaginationAndUpcomingTripsAreExcluded()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            for (var index = 1; index <= 251; index++)
                AddTrip(db, index);
            db.ScheduledTrips.Add(Trip(252, Weekday, new TimeOnly(11, 0), new TimeOnly(11, 20)));
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-11T10:11:00");
        Assert.Equal(251, data.Length);
        Assert.All(data, trip => Assert.Equal(new TimeTablePosition("b", null, null), trip.Position));
        Assert.DoesNotContain(data, trip => trip.Id == 252);
    }

    [Fact]
    public async Task PreviousServiceDayContinuesAfterMidnightWithItsOwnHolidayClassification()
    {
        await using var app = new CacheApp();
        app.Holidays.Reply = uri => Json(new { is_holiday = uri.EndsWith("/12") });
        await app.Seed(db =>
        {
            var overnight = Trip(1, Weekday, new TimeOnly(23, 55), new TimeOnly(0, 20));
            overnight.ArrivalDayOffset = 1;
            db.ScheduledTrips.Add(overnight);
            db.ScheduledStopTimes.AddRange(
                Stop(1, 1, "a", new TimeOnly(23, 54), new TimeOnly(23, 55)),
                Stop(1, 2, "b", new TimeOnly(0, 10), new TimeOnly(0, 12), 1),
                Stop(1, 3, "c", new TimeOnly(0, 20), null, 1));
            db.ScheduledTrips.Add(Trip(2, ServiceDayType.FridayOrHoliday, new TimeOnly(6, 0), new TimeOnly(6, 20)));
            db.ScheduledTrips.Add(Trip(3, ServiceDayType.FridayOrHoliday, new TimeOnly(0, 0), new TimeOnly(0, 20)));
            db.ScheduledStopTimes.AddRange(
                Stop(3, 1, "a", new TimeOnly(0, 0), new TimeOnly(0, 0)),
                Stop(3, 2, "b", new TimeOnly(0, 10), new TimeOnly(0, 12)),
                Stop(3, 3, "c", new TimeOnly(0, 20), null));
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-12T00:05:00");
        Assert.Equal(2, data.Length);
        var carryover = data.Single(trip => trip.Id == 1);
        Assert.Equal("a", carryover.Position.FromStation);
        Assert.Equal("b", carryover.Position.ToStation);
        Assert.Contains(data, trip => trip.Id == 3);
        Assert.DoesNotContain(data, trip => trip.Id == 2);
        Assert.Equal(2, app.Holidays.Calls.Count);
    }

    [Fact]
    public async Task MissingOrMalformedStopDataIsNotPresentedAsAKnownPosition()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            db.ScheduledTrips.Add(Trip(1, Weekday, new TimeOnly(10, 0), new TimeOnly(10, 20)));
            AddTrip(db, 2);
            db.ScheduledStopTimes.Local.Single(stop => stop.TripId == 2 && stop.Sequence == 2).DepartureTime = null;
            db.ScheduledStopTimes.Local.Single(stop => stop.TripId == 2 && stop.Sequence == 2).DepartureDayOffset = null;
            AddTrip(db, 3);
            db.ScheduledStopTimes.Local.Single(stop => stop.TripId == 3 && stop.Sequence == 2).ArrivalTime = new TimeOnly(9, 0);
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-11T10:11:00");
        Assert.Empty(data);
    }

    [Fact]
    public async Task OmittedDateAndExplicitOffsetsUseTehranEvenAcrossMidnight()
    {
        await using var app = new CacheApp();
        app.Clock.Now = new DateTimeOffset(2026, 11, 11, 22, 0, 0, TimeSpan.Zero);
        await app.Seed(db =>
        {
            db.ScheduledTrips.Add(Trip(1, ServiceDayType.Thursday, new TimeOnly(1, 0), new TimeOnly(2, 0)));
            db.ScheduledStopTimes.AddRange(
                Stop(1, 1, "a", new TimeOnly(1, 0), new TimeOnly(1, 0)),
                Stop(1, 2, "b", new TimeOnly(1, 30), new TimeOnly(1, 31)),
                Stop(1, 3, "c", new TimeOnly(2, 0), null));
        });
        using var client = app.Client();
        var implicitTime = await Read(client);
        var explicitTime = await Read(client, "2026-11-11T22:00:00Z");
        var localTime = await Read(client, "2026-11-12T01:30:00");
        var offsetTime = await Read(client, "2026-11-12T01:30:00+03:30");
        Assert.Equal(1, Assert.Single(implicitTime).Id);
        Assert.Equal(implicitTime, explicitTime);
        Assert.Equal(implicitTime, localTime);
        Assert.Equal(implicitTime, offsetTime);
        Assert.Equal("https://holidayapi.ir/gregorian/2026/11/12", Assert.Single(app.Holidays.Calls));
    }

    [Fact]
    public async Task HolidayDecisionsAreCachedButPositionsAreRecomputed()
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        var first = await Read(client, "2026-11-11T10:05:00");
        var second = await Read(client, "2026-11-11T10:11:00");
        Assert.Equal(new TimeTablePosition(null, "a", "b"), Assert.Single(first).Position);
        Assert.Equal(new TimeTablePosition("b", null, null), Assert.Single(second).Position);
        Assert.Single(app.Holidays.Calls);
    }

    [Theory]
    [InlineData("16:39:00", null, null, null)]
    [InlineData("16:42:00", "b", null, null)]
    [InlineData("16:42:00.001", null, "b", "c")]
    [InlineData("16:44:00", null, null, null)]
    public async Task ZeroDwellTrip3172HasARealPositionOnlyWhileActive(string time, string? station, string? from, string? to)
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            db.ScheduledTrips.Add(Trip(3172, Weekday, new TimeOnly(16, 40), new TimeOnly(16, 44)));
            db.ScheduledStopTimes.AddRange(
                Stop(3172, 1, "a", new TimeOnly(16, 40), new TimeOnly(16, 40)),
                Stop(3172, 2, "b", new TimeOnly(16, 42), new TimeOnly(16, 42)),
                Stop(3172, 3, "c", new TimeOnly(16, 44), null));
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-11T" + time);
        if (station is null && from is null && to is null) Assert.Empty(data);
        else Assert.Equal(new TimeTablePosition(station, from, to), Assert.Single(data).Position);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-11-11")]
    [InlineData("2026-02-30T10:00:00")]
    [InlineData("2026-11-11T25:00:00")]
    public async Task InvalidDateTimeIsRejectedBeforeHolidayRequest(string input)
    {
        await using var app = new CacheApp();
        using var client = app.Client();
        using var response = await client.GetAsync("/api/timetable/TimeTableCache?dateTime=" + Uri.EscapeDataString(input));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Holidays.Calls);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("timeout")]
    public async Task HolidayFailureNeverFallsBackToAnUnverifiedWeekday(string failure)
    {
        await using var app = new CacheApp();
        app.Holidays.Reply = _ => failure switch
        {
            "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "invalid" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bad json") },
            "missing" => Json(new { events = Array.Empty<object>() }),
            _ => throw new TaskCanceledException("Holiday lookup timed out")
        };
        using var client = app.Client();
        using var response = await client.GetAsync("/api/timetable/TimeTableCache?dateTime=2026-11-11T10:11:00");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };

    [Fact]
    public async Task StationPageContainsAllInitialDataAndHonorsTheRequestedLine()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            AddTrip(db, 1);
            db.MetroLines.Add(new MetroLine { Id = 4, NameFa = "خط ۴", NameEn = "Line 4", Color = "#ffff00" });
            db.StationLines.Add(new StationLine { StationId = "b", LineId = 4, IsOperational = true });
            db.MetroRoutes.Add(new MetroRoute { Id = "other", LineId = 4 });
            db.RouteStops.Add(new RouteStop { RouteId = "other", StationId = "b", Sequence = 1, Status = "operational" });
            db.StationAmenities.Add(new StationAmenity { StationId = "b", Type = AmenityType.Elevator, IsAvailable = true, IsVerified = true });
            db.StationEntrances.Add(new StationEntrance { StationId = "b", Code = "A", NameFa = "ورودی شمالی" });
        });
        using var client = app.Client();
        using var response = await client.GetAsync("/api/stations/b/page?lineId=4");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<StationPageData>())!;
        Assert.Equal("b", page.Station.Id);
        Assert.Single(page.Station.Amenities);
        Assert.Single(page.Station.Entrances);
        Assert.Equal(new byte[] { 1, 4 }, page.Lines.Select(line => line.Id));
        Assert.Equal(2, page.Routes.Length);
        Assert.Equal(3, page.Stations.Length);
        Assert.Equal<byte?>(4, page.SelectedLineId);
        Assert.Equal(4, page.Board!.LineId);
        Assert.False(page.BoardUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?lineId=7")]
    public async Task StationPageDefaultsToTheLowestExistingLine(string query)
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        var page = (await client.GetFromJsonAsync<StationPageData>("/api/stations/b/page" + query))!;
        Assert.Equal<byte?>(1, page.SelectedLineId);
        Assert.Equal(1, page.Board!.LineId);
        Assert.NotEmpty(page.Board.Trips);
    }

    [Fact]
    public async Task StationPageReturns404ForAnUnknownStation()
    {
        await using var app = new CacheApp();
        await app.Seed(_ => { });
        using var client = app.Client();
        using var response = await client.GetAsync("/api/stations/missing/page");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(app.Holidays.Calls);
    }

    [Fact]
    public async Task StationBoardSkipsAnInvalidNextTrainAndLoadsTheNextValidJourney()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            AddTrip(db, 1);
            db.ScheduledStopTimes.Local.Single(stop => stop.TripId == 1 && stop.Sequence == 1).ArrivalTime = new TimeOnly(10, 15);
            AddTrip(db, 2);
        });
        using var client = app.Client();
        var board = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T09:00:00"))!;
        Assert.Equal(2, Assert.Single(board.NextArrivals).TripId);
        Assert.Equal(2, Assert.Single(board.Trips).Id);
    }

    [Fact]
    public async Task StationTemplateCacheDoesNotFreezePositionsOrArrivalTimes()
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        var first = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T10:05:00"))!;
        var second = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T10:11:00"))!;
        Assert.Equal(new TimeTablePosition(null, "a", "b"), Assert.Single(first.Trips).Position);
        Assert.Equal(new TimeTablePosition("b", null, null), Assert.Single(second.Trips).Position);
        Assert.NotEqual(first.RequestedAt, second.RequestedAt);
    }

    [Fact]
    public async Task StationBoardIncludesBothDirectionsAndFullDatedTrainJourneys()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            AddTrip(db, 1);
            var reverse = Trip(2, Weekday, new TimeOnly(10, 0), new TimeOnly(10, 20));
            reverse.DirectionStationId = "a";
            reverse.OriginStationId = "c";
            reverse.DestinationStationId = "a";
            db.ScheduledTrips.Add(reverse);
            db.ScheduledStopTimes.AddRange(Stop(2, 1, "c", new TimeOnly(10, 0), new TimeOnly(10, 0)),
                Stop(2, 2, "b", new TimeOnly(10, 9), new TimeOnly(10, 10)), Stop(2, 3, "a", new TimeOnly(10, 20), null));
        });
        using var client = app.Client();
        var board = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T10:05:00"))!;
        Assert.Equal(2, board.NextArrivals.Length);
        Assert.Equal(new[] { "a", "c" }, board.NextArrivals.Select(arrival => arrival.DirectionStationId).Order());
        Assert.All(board.Trips, trip => Assert.Equal(3, trip.Stops.Length));
        var forward = board.Trips.Single(trip => trip.Id == 1);
        Assert.Equal(new TimeTablePosition(null, "a", "b"), forward.Position);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 10, 10, 0, TimeSpan.FromHours(3.5)), forward.Stops[1].ArrivalAt);
    }

    [Fact]
    public async Task StationBoardFindsFutureTrainsAndDoesNotOfferAlreadyPassedStops()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            AddTrip(db, 1);
            AddTrip(db, 2);
            db.ScheduledTrips.Local.Single(trip => trip.Id == 2).DepartureTime = new TimeOnly(11, 0);
            db.ScheduledTrips.Local.Single(trip => trip.Id == 2).ArrivalTime = new TimeOnly(11, 20);
            foreach (var stop in db.ScheduledStopTimes.Local.Where(stop => stop.TripId == 2))
            {
                stop.ArrivalTime = stop.ArrivalTime.AddHours(1);
                if (stop.DepartureTime is { } departure) stop.DepartureTime = departure.AddHours(1);
            }
        });
        using var client = app.Client();
        var board = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T10:13:00"))!;
        Assert.Equal(2, Assert.Single(board.NextArrivals).TripId);
        Assert.Equal(new TimeTablePosition(null, "b", "c"), board.Trips.Single(trip => trip.Id == 1).Position);
        Assert.Null(board.Trips.Single(trip => trip.Id == 2).Position);
    }

    [Fact]
    public async Task StationBoardKeepsOvernightServiceDatesAndDwellTimes()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            var trip = Trip(1, Weekday, new TimeOnly(23, 55), new TimeOnly(0, 20));
            trip.ArrivalDayOffset = 1;
            db.ScheduledTrips.Add(trip);
            db.ScheduledStopTimes.AddRange(Stop(1, 1, "a", new TimeOnly(23, 55), new TimeOnly(23, 55)),
                Stop(1, 2, "b", new TimeOnly(0, 10), new TimeOnly(0, 12), 1), Stop(1, 3, "c", new TimeOnly(0, 20), null, 1));
        });
        using var client = app.Client();
        var board = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-12T00:11:00"))!;
        var trip = Assert.Single(board.Trips);
        Assert.Equal(new DateOnly(2026, 11, 11), trip.ServiceDate);
        Assert.Equal(new TimeTablePosition("b", null, null), trip.Position);
        Assert.Equal(12, Assert.Single(board.NextArrivals).DepartureAt!.Value.Minute);
        Assert.Equal(12, trip.Stops[1].ArrivalAt.Day);
    }

    [Fact]
    public async Task StationBoardDoesNotOfferBoardingAtAnInactiveStation()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            AddTrip(db, 1);
            db.Stations.Local.Single(station => station.Id == "b").InfrastructureStatus = "under_construction";
        });
        using var client = app.Client();
        var board = (await client.GetFromJsonAsync<StationTrainBoard>("/api/timetable/stations/b/board?lineId=1&dateTime=2026-11-11T10:05:00"))!;
        Assert.Empty(board.NextArrivals);
        Assert.Single(board.Trips);
    }

    [Theory]
    [InlineData("/api/timetable/stations/missing/board?lineId=1", HttpStatusCode.NotFound)]
    [InlineData("/api/timetable/stations/b/board?lineId=7", HttpStatusCode.NotFound)]
    [InlineData("/api/timetable/stations/b/board?lineId=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/timetable/stations/b/board?lineId=1&dateTime=invalid", HttpStatusCode.BadRequest)]
    public async Task StationBoardValidatesTheStationLineAndDate(string url, HttpStatusCode status)
    {
        await using var app = new CacheApp();
        await app.Seed(_ => { });
        using var client = app.Client();
        using var response = await client.GetAsync(url);
        Assert.Equal(status, response.StatusCode);
        Assert.Empty(app.Holidays.Calls);
    }

    [Fact]
    public async Task ResponseContainsOnlyTheRequestedFieldsAndStringStationIds()
    {
        await using var app = new CacheApp();
        await app.Seed(db => AddTrip(db, 1));
        using var client = app.Client();
        var json = await client.GetFromJsonAsync<JsonElement>("/api/timetable/TimeTableCache?dateTime=2026-11-11T10:05:00");
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        var trip = Assert.Single(json.EnumerateArray());
        Assert.Equal(new[] { "directionStationId", "id", "lineId", "position", "routeId" },
            trip.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        var position = trip.GetProperty("position");
        Assert.Equal(new[] { "fromStation", "station", "toStation" },
            position.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal(JsonValueKind.Null, position.GetProperty("station").ValueKind);
        Assert.Equal("a", position.GetProperty("fromStation").GetString());
        Assert.Equal("b", position.GetProperty("toStation").GetString());
    }

    private static async Task<TimeTableCacheTrip[]> Read(HttpClient client, string? time = null)
    {
        using var response = await client.GetAsync("/api/timetable/TimeTableCache" +
            (time is null ? "" : "?dateTime=" + Uri.EscapeDataString(time)));
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var trips = (await response.Content.ReadFromJsonAsync<TimeTableCacheTrip[]>())!;
        Assert.All(trips, trip => Assert.True(trip.Position.Station is not null
            || trip.Position.FromStation is not null && trip.Position.ToStation is not null));
        return trips;
    }

    private static ScheduledTrip Trip(long id, string code, TimeOnly departure, TimeOnly arrival) => new()
    {
        Id = id, ScheduleGroupId = 1, DayTypeCode = code, DirectionStationId = "c",
        OriginStationId = "a", DestinationStationId = "c", DepartureTime = departure, ArrivalTime = arrival
    };

    private static ScheduledStopTime Stop(long trip, short sequence, string station, TimeOnly arrival,
        TimeOnly? departure, byte offset = 0) => new()
    {
        TripId = trip, Sequence = sequence, StationId = station, ArrivalTime = arrival, ArrivalDayOffset = offset,
        DepartureTime = departure, DepartureDayOffset = departure.HasValue ? offset : null
    };

    private static void AddTrip(AppDbContext db, long id, string code = Weekday)
    {
        db.ScheduledTrips.Add(Trip(id, code, new TimeOnly(10, 0), new TimeOnly(10, 20)));
        // Deliberately insert out of order; sequence is the authoritative travel direction.
        db.ScheduledStopTimes.AddRange(Stop(id, 3, "c", new TimeOnly(10, 20), null),
            Stop(id, 1, "a", new TimeOnly(9, 59), new TimeOnly(10, 0)),
            Stop(id, 2, "b", new TimeOnly(10, 10), new TimeOnly(10, 12)));
    }

    private sealed class CacheApp : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        public HolidayHandler Holidays { get; } = new();
        public FakeClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddLogging(logging => logging.ClearProviders());
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddHttpClient<HolidayCalendar>().ConfigurePrimaryHttpMessageHandler(() => Holidays);
            });
        }

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130") });

        public async Task Seed(Action<AppDbContext> seed)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MetroLines.Add(new MetroLine { Id = 1, NameFa = "خط ۱", NameEn = "Line 1", Color = "#112233" });
            db.Stations.AddRange(new[] { "a", "b", "c" }.Select(id => new Station
                { Id = id, NameFa = id, NameEn = id, InfrastructureStatus = "operational" }));
            db.MetroRoutes.Add(new MetroRoute { Id = "main", LineId = 1 });
            db.StationLines.AddRange(new[] { "a", "b", "c" }.Select(id => new StationLine { StationId = id, LineId = 1, IsOperational = true }));
            db.RouteStops.AddRange(new[] { "a", "b", "c" }.Select((id, index) => new RouteStop { RouteId = "main", StationId = id, Sequence = (short)(index + 1), Status = "operational" }));
            db.ScheduleGroups.Add(new ScheduleGroup
                { Id = 1, RouteId = "main", ScheduleKey = "test", FromTerminalId = "a", ToTerminalId = "c" });
            seed(db);
            await db.SaveChangesAsync();
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 6, 40, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class HolidayHandler : HttpMessageHandler
    {
        public ConcurrentBag<string> Calls { get; } = [];
        public Func<string, HttpResponseMessage> Reply { get; set; } = _ => Json(new { is_holiday = false });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            Calls.Add(uri);
            return Task.FromResult(Reply(uri));
        }
    }
}
