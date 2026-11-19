using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Rail.Api.Services;
using Xunit;

namespace Rail.Tests.Api;

public sealed class StreetRoutingTests
{
    private const string Success = """
        {"paths":[{"distance":821.25,"time":584001,"points":{"type":"LineString","coordinates":[[51.4,35.68],[51.405,35.685],[51.41,35.69]]},
          "instructions":[{"text":"Turn right","distance":821.25,"time":584001}]}]}
        """;

    [Fact]
    public async Task UsesModeAndDirectionalCoordinatesAndConvertsMillisecondsWithoutSwappingGeoJson()
    {
        using var fixture = new Fixture();
        var walking = await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default);
        Assert.Contains("route?point=35.68,51.4&point=35.69,51.41", fixture.Handler.Uris[0]);
        Assert.Contains("profile=foot", fixture.Handler.Uris[0]);
        Assert.Contains("points_encoded=false", fixture.Handler.Uris[0]);
        Assert.Contains("locale=fa", fixture.Handler.Uris[0]);
        Assert.Equal("graphhopper", walking.Source);
        Assert.Equal("none", walking.TrafficModel);
        Assert.Equal(585, walking.Seconds);
        Assert.Equal(821.25, walking.DistanceMeters);
        Assert.Equal(new[] { 51.4, 35.68 }, walking.Coordinates[0]);
        Assert.Equal(3, walking.Coordinates.Length);
        Assert.Equal("Turn right", Assert.Single(walking.Steps).Instruction);
        Assert.Equal(585, walking.Steps[0].Seconds);
        Assert.Same(walking, await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default));
        Assert.Single(fixture.Handler.Uris);
        await fixture.Service.RouteAsync(35.69, 51.41, 35.68, 51.40, "car", default);
        Assert.Contains("profile=car", fixture.Handler.Uris[1]);
        Assert.Contains("point=35.69,51.41&point=35.68,51.4", fixture.Handler.Uris[1]);
        await fixture.Service.RouteAsync(35.69, 51.41, 35.68, 51.40, "motorcycle", default);
        Assert.Contains("profile=motorcycle", fixture.Handler.Uris[2]);
    }

    [Fact]
    public async Task ProfileOverrideUsesInvariantCoordinateFormatting()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var fixture = new Fixture(profile: "city foot");
            await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default);
            Assert.Contains("point=35.68,51.4", fixture.Handler.Uris[0]);
            Assert.Contains("profile=city%20foot", fixture.Handler.Uris[0]);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData("{\"paths\":[]}", HttpStatusCode.OK, "no_route")]
    [InlineData("{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.ConnectionNotFoundException\"}]}", HttpStatusCode.BadRequest, "no_route")]
    [InlineData("{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.PointNotFoundException\"}]}", HttpStatusCode.BadRequest, "no_route")]
    [InlineData("{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.PointOutOfBoundsException\"}]}", HttpStatusCode.BadRequest, "no_route")]
    [InlineData("{\"message\":\"Unknown profile\"}", HttpStatusCode.BadRequest, "estimated")]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable, "estimated")]
    [InlineData("broken JSON", HttpStatusCode.OK, "estimated")]
    [InlineData("{\"paths\":[{\"distance\":-1,\"time\":1000}]}", HttpStatusCode.OK, "estimated")]
    public async Task DistinguishesUnreachableRoutesFromProviderFailuresAndDoesNotInventGeometry(
        string body, HttpStatusCode status, string expected)
    {
        using var fixture = new Fixture(body, status);
        var path = await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default);
        Assert.Equal(expected, path.Source);
        Assert.Empty(path.Coordinates);
        Assert.Empty(path.Steps);
        Assert.True(path.Seconds > 0);
    }

    [Fact]
    public async Task RejectsInvalidGeometryAndDoesNotCacheProviderFailure()
    {
        using var fixture = new Fixture(Success.Replace("[51.4,35.68]", "[181,95]"));
        Assert.Equal("estimated", (await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default)).Source);
        fixture.Handler.Body = Success;
        Assert.Equal("graphhopper", (await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default)).Source);
        Assert.Equal(2, fixture.Handler.Uris.Count);
    }

    [Fact]
    public async Task HonorsCallerCancellationButFallsBackOnProviderTimeout()
    {
        using var fixture = new Fixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", canceled.Token));
        Assert.Empty(fixture.Handler.Uris);
        fixture.Handler.Failure = new TaskCanceledException("GraphHopper timed out");
        Assert.Equal("estimated", (await fixture.Service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default)).Source);
    }

    [Fact]
    public async Task IdenticalEndpointsNeedNoProviderCall()
    {
        using var fixture = new Fixture();
        var path = await fixture.Service.RouteAsync(35.68, 51.4, 35.68, 51.4, "walk", default);
        Assert.Equal(0, path.Seconds);
        Assert.Equal(0, path.DistanceMeters);
        Assert.Empty(fixture.Handler.Uris);
    }

    private sealed class Fixture : IDisposable
    {
        public RoutingHandler Handler { get; }
        public StreetRouting Service { get; }
        private readonly HttpClient http;
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        public Fixture(string body = Success, HttpStatusCode status = HttpStatusCode.OK, string? profile = null)
        {
            Handler = new RoutingHandler { Body = body, Status = status };
            http = new HttpClient(Handler) { BaseAddress = new("http://127.0.0.1:8989/") };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["GraphHopper:Profiles:walk"] = profile }).Build();
            Service = new StreetRouting(http, config, cache);
        }
        public void Dispose() { http.Dispose(); cache.Dispose(); }
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];
        public string Body { get; set; } = Success;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Failure { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(request.Headers.Contains("Api-Key"));
            Uris.Add(request.RequestUri!.AbsoluteUri);
            if (Failure is not null) return Task.FromException<HttpResponseMessage>(Failure);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
