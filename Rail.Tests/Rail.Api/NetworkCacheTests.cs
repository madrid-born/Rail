using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rail.Api.Controllers;
using Rail.Api.Models;
using Rail.Api.Models.Metro;
using Xunit;

namespace Rail.Tests.Api;

public class NetworkCacheTests
{
    [Fact]
    public async Task SnapshotContainsAllStationsOrderedRoutesAndInterchangeMemberships()
    {
        var database = Guid.NewGuid().ToString();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database));
            });
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130") });
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MetroLines.AddRange(
                new MetroLine { Id = 2, NameFa = "خط ۲", NameEn = "Line 2", Color = "#112233" },
                new MetroLine { Id = 1, NameFa = "خط ۱", NameEn = "Line 1", Color = "#445566" });
            db.Stations.AddRange(Enumerable.Range(1, 251).Select(index => new Station
            {
                Id = $"station-{index:000}", NameFa = $"ایستگاه {index}", NameEn = $"Station {index}",
                Latitude = 35.7m, Longitude = 51.4m,
                InfrastructureStatus = index == 2 ? "under_construction" : "operational"
            }));
            db.StationLines.AddRange(
                new StationLine { StationId = "station-001", LineId = 1, IsOperational = true },
                new StationLine { StationId = "station-001", LineId = 2, IsOperational = false });
            db.MetroRoutes.AddRange(
                new MetroRoute { Id = "branch", LineId = 1, BranchCode = "branch" },
                new MetroRoute { Id = "main", LineId = 1 },
                new MetroRoute { Id = "line-two", LineId = 2 });
            db.RouteStops.AddRange(
                new RouteStop { RouteId = "main", Sequence = 2, StationId = "station-002", Status = "under_construction" },
                new RouteStop { RouteId = "main", Sequence = 1, StationId = "station-001", Status = "operational" },
                new RouteStop { RouteId = "branch", Sequence = 1, StationId = "station-001", Status = "operational" });
            var segment = new TrackSegment { Id = 1, SourceId = "original-key", RouteId = "main",
                FromStationId = "station-001", ToStationId = "station-002", Status = "operational" };
            segment.Points.Add(new TrackSegmentPoint { Sequence = 1, Longitude = 51.42m, Latitude = 35.72m });
            segment.Points.Add(new TrackSegmentPoint { Sequence = 0, Longitude = 51.4m, Latitude = 35.7m });
            db.TrackSegments.Add(segment);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/network/load-cached-data");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var snapshot = (await response.Content.ReadFromJsonAsync<NetworkCacheData>())!;
        Assert.Equal(1, snapshot.SchemaVersion);
        var geometry = Assert.Single(snapshot.Segments!);
        Assert.Equal(1, geometry.Id);
        Assert.Equal(new decimal[] { 51.4m, 35.7m }, geometry.Coordinates[0]);
        Assert.Equal(new decimal[] { 51.42m, 35.72m }, geometry.Coordinates[1]);
        Assert.NotEqual(default, snapshot.GeneratedAtUtc);
        Assert.Equal(new byte[] { 1, 2 }, snapshot.Lines.Select(line => line.Id));
        Assert.Equal(251, snapshot.Stations.Length); // No 200/250-item endpoint limit.
        Assert.Equal(new[] { "main", "branch", "line-two" }, snapshot.Routes.Select(route => route.Id));
        Assert.Equal(new short[] { 1, 2 }, snapshot.Routes[0].Stops.Select(stop => stop.Sequence));
        Assert.Equal("under_construction", snapshot.Routes[0].Stops[1].Status);
        var shared = Assert.Single(snapshot.Stations, station => station.Id == "station-001");
        Assert.Equal(35.7m, shared.Latitude);
        Assert.Equal(51.4m, shared.Longitude);
        Assert.Equal(2, shared.Lines.Length);
        Assert.False(shared.Lines[1].IsOperational);
        Assert.Equal(new[] { "branch", "main" }, snapshot.Lines[0].Routes.Select(route => route.Id));

        // Explicit client refresh must receive new data, not a stale HTTP/server snapshot.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Stations.FindAsync("station-001"))!.NameFa = "نام جدید";
            await db.SaveChangesAsync();
        }
        var refreshed = (await client.GetFromJsonAsync<NetworkCacheData>("/api/network/load-cached-data"))!;
        Assert.Equal("نام جدید", refreshed.Stations.Single(station => station.Id == "station-001").NameFa);
        Assert.Equal("نام جدید", refreshed.Routes[0].Stops[0].NameFa);
    }
}
