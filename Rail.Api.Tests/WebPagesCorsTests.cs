using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Rail.Api.Tests;

public class WebPagesCorsTests
{
    [Theory]
    [InlineData("https://appdir", true)]
    [InlineData("https://unrelated.example", false)]
    public async Task OnlyConfiguredWebViewOriginsCanReadNetwork(string origin, bool allowed)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130") });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/network/lines");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed) Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Theory]
    [InlineData("https://appdir", true)]
    [InlineData("https://appassets.androidplatform.net", true)]
    [InlineData("https://unrelated.example", false)]
    public async Task AuthenticationPostAllowsTrustedOriginsAndAuthorization(string origin, bool allowed)
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130") });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/authentication/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Contains("POST", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
            Assert.Contains("authorization", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        }
    }
}
