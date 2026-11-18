using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Rail.Api.Services;
using Xunit;

namespace Rail.Tests.Api;

public sealed class StreetRoutingTests
{
    [Fact]
    public async Task UsesModeAndDirectionalCoordinatesAndReturnsDecodedStreetGeometryAndSteps()
    {
        using var handler = new RoutingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.neshan.org/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Neshan:RoutingApiKey"] = "test-key" }).Build();
        var service = new StreetRouting(http, config, cache);
        var walking = await service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default);
        Assert.Contains("type=pedestrian", handler.Uris[0]);
        Assert.Contains("/v4/direction?", handler.Uris[0]);
        Assert.DoesNotContain("no-traffic", handler.Uris[0]);
        Assert.Contains("origin=35.68,51.4&destination=35.69,51.41", handler.Uris[0]);
        Assert.Equal("neshan", walking.Source);
        Assert.Equal(584, walking.Seconds);
        Assert.Equal(821, walking.DistanceMeters);
        Assert.Equal(new[] { -120.2, 38.5 }, walking.Coordinates[0]);
        Assert.Equal(3, walking.Coordinates.Length);
        Assert.Equal("Turn right", Assert.Single(walking.Steps).Instruction);
        Assert.Same(walking, await service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default));
        Assert.Single(handler.Uris);
        await service.RouteAsync(35.69, 51.41, 35.68, 51.40, "car", default);
        Assert.Contains("type=car", handler.Uris[1]);
        Assert.Contains("/v4/direction?", handler.Uris[1]);
        Assert.All(handler.Uris, uri => Assert.DoesNotContain("no-traffic", uri));
        Assert.Contains("origin=35.69,51.41&destination=35.68,51.4", handler.Uris[1]);
        await service.RouteAsync(35.69, 51.41, 35.68, 51.40, "motorcycle", default);
        Assert.Contains("type=motorcycle", handler.Uris[2]);
    }

    [Theory]
    [InlineData(false, "DepartAt")]
    [InlineData(true, "ArriveAt")]
    public async Task ScheduledCarsUseHistoricalTrafficAndItsResponseFormat(bool arriveBy, string routingType)
    {
        using var handler = new RoutingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.neshan.org/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Neshan:RoutingApiKey"] = "test-key" }).Build();
        var service = new StreetRouting(http, config, cache);
        var at = new DateTime(2026, 11, 19, 17, 30, 0);
        var path = await service.RouteAsync(35.68, 51.4, 35.69, 51.41, "car", default, at, arriveBy);
        Assert.Contains("/v1/direction/historical?", handler.Uris[0]);
        Assert.Contains($"routingType={routingType}", handler.Uris[0]);
        Assert.Contains("dateTime=2026-11-19T17:30", Uri.UnescapeDataString(handler.Uris[0]));
        Assert.Equal("neshan", path.Source);
        Assert.Equal(482, path.Seconds);
        Assert.Equal(821, path.DistanceMeters);
        Assert.Equal(3, path.Coordinates.Length);
        Assert.Equal(483, Assert.Single(path.Steps).Seconds);
        Assert.Same(path, await service.RouteAsync(35.68, 51.4, 35.69, 51.41, "car", default, at, arriveBy));
        Assert.Single(handler.Uris);
        await service.RouteAsync(35.68, 51.4, 35.69, 51.41, "car", default, at.AddHours(1), arriveBy);
        await service.RouteAsync(35.68, 51.4, 35.69, 51.41, "car", default, at, !arriveBy);
        await service.RouteAsync(35.68, 51.4, 35.69, 51.41, "car", default);
        Assert.Equal(4, handler.Uris.Count);
        Assert.Contains("/v4/direction?", handler.Uris[^1]);
        Assert.All(handler.Uris, uri => Assert.DoesNotContain("no-traffic", uri));
    }

    [Fact]
    public async Task UnavailableProviderDoesNotInventStreetGeometry()
    {
        using var http = new HttpClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new StreetRouting(http, new ConfigurationBuilder().Build(), cache);
        var path = await service.RouteAsync(35.68, 51.40, 35.69, 51.41, "walk", default);
        Assert.Equal("estimated", path.Source);
        Assert.Empty(path.Coordinates);
        Assert.True(path.Seconds > 0);
        Assert.Throws<FormatException>(() => StreetRouting.DecodePolyline("~"));
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("Api-Key")));
            Uris.Add(request.RequestUri!.ToString());
            if (request.RequestUri.AbsolutePath.EndsWith("historical"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    """{"routes":[{"distance":821,"duration":482.0,"dateTime":"2026-11-19T17:21:58Z","geometry":"_p~iF~ps|U_ulLnnqC_mqNvxq`@","legs":[{"distance":821,"duration":482.93,"steps":[{"name":"Street","distance":821,"duration":482.93}]}]}]}""",
                    Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"routes":[{"legs":[{"distance":{"value":821},"duration":{"value":584},"steps":[{"instruction":"Turn right","distance":{"value":821},"duration":{"value":584},"polyline":"_p~iF~ps|U_ulLnnqC_mqNvxq`@"}]}]}]}""",
                Encoding.UTF8, "application/json") });
        }
    }
}
