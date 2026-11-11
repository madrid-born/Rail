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
    public async Task AllUpcomingTripsAreReturnedWithoutPagination()
    {
        await using var app = new CacheApp();
        await app.Seed(db =>
        {
            for (var index = 1; index <= 251; index++)
                db.ScheduledTrips.Add(Trip(index, Weekday, new TimeOnly(11, 0), new TimeOnly(11, 20)));
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-11T10:11:00");
        Assert.Equal(251, data.Length);
        Assert.All(data, trip => Assert.Equal(new TimeTablePosition(null, null, null), trip.Position));
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
        });
        using var client = app.Client();
        var data = await Read(client, "2026-11-12T00:05:00");
        Assert.Equal(2, data.Length);
        var carryover = data.Single(trip => trip.Id == 1);
        Assert.Equal("a", carryover.Position.FromStation);
        Assert.Equal("b", carryover.Position.ToStation);
        Assert.Contains(data, trip => trip.Id == 2);
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
        Assert.Equal(3, data.Length);
        Assert.All(data, trip => Assert.Equal(new TimeTablePosition(null, null, null), trip.Position));
    }

    [Fact]
    public async Task OmittedDateAndExplicitOffsetsUseTehranEvenAcrossMidnight()
    {
        await using var app = new CacheApp();
        app.Clock.Now = new DateTimeOffset(2026, 11, 11, 22, 0, 0, TimeSpan.Zero);
        await app.Seed(db => db.ScheduledTrips.Add(Trip(1, ServiceDayType.Thursday,
            new TimeOnly(1, 0), new TimeOnly(2, 0))));
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
        return (await response.Content.ReadFromJsonAsync<TimeTableCacheTrip[]>())!;
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
