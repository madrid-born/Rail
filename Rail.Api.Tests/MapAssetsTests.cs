using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Rail.Api.Tests;

public class MapAssetsTests
{
    [Fact]
    public async Task MapArchiveSupportsRangesHeadCachingAndWebViewCors()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rail-map-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(directory, "tehran-region.pmtiles"), bytes);
            await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Maps:DataPath"] = directory })));
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost:7130") });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/maps/tehran-region.pmtiles");
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 126);
            request.Headers.Add("Origin", "https://appdir");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(bytes[..127], await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("bytes 0-126/256", response.Content.Headers.ContentRange!.ToString());
            Assert.NotNull(response.Headers.ETag);
            Assert.Equal("https://appdir", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Contains("ETag", string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")));

            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/maps/tehran-region.pmtiles"));
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Equal(256, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync());

            using var cachedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/maps/tehran-region.pmtiles");
            cachedRequest.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var cached = await client.SendAsync(cachedRequest);
            Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
            using var missing = await client.GetAsync("/api/maps/missing.pmtiles");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
