using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Rail.Api.Tests;

public class OpenApiAuthenticationTests
{
    [Fact]
    public async Task SwaggerDocumentDefinesBearerAndMatchesEndpointAuthorization()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddLogging(logging => logging.ClearProviders());
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130") });
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var bearer = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var paths = document.GetProperty("paths");
        AssertBearerRequired(paths.GetProperty("/api/Authentication/me").GetProperty("get"));
        AssertBearerRequired(paths.GetProperty("/api/Authentication/google/link/start").GetProperty("post"));
        AssertBearerRequired(paths.GetProperty("/api/me/favorite-stations").GetProperty("get"));
        AssertAnonymous(paths.GetProperty("/api/Authentication/login").GetProperty("post"));
        AssertAnonymous(paths.GetProperty("/api/Authentication/google/start").GetProperty("post"));
        AssertAnonymous(paths.GetProperty("/api/network/lines").GetProperty("get"));

        var exchange = paths.GetProperty("/api/Authentication/google/exchange").GetProperty("post").GetProperty("security");
        Assert.Equal(2, exchange.GetArrayLength());
        Assert.True(exchange[0].TryGetProperty("Bearer", out _));
        Assert.Empty(exchange[1].EnumerateObject());
        Assert.Contains("id=\"swagger-ui\"", await client.GetStringAsync("/swagger/index.html"));
    }

    private static void AssertBearerRequired(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Single(security.EnumerateArray());
        Assert.True(security[0].TryGetProperty("Bearer", out _));
    }

    private static void AssertAnonymous(JsonElement operation) =>
        Assert.True(!operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0);
}
