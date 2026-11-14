using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rail.Api.Models;
using Rail.Api.Services;
using Xunit;

namespace Rail.Api.Tests;

public class GoogleLoginTests
{
    [Fact]
    public async Task BrowserLoginIssuesRailTokensOnceAndSupportsRefresh()
    {
        await using var app = new GoogleApp();
        using var client = app.NewClient();
        var session = await Start(client);
        Assert.Equal(HttpStatusCode.Accepted, (await Exchange(client, session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Exchange(client, session with { ExchangeKey = new string('x', 43) })).StatusCode);
        await app.CompleteBrowserLogin(session);

        // Google verification alone does not register a Rail account; the client completes the exchange.
        await app.WithUsers(async users => Assert.Null(await users.FindByEmailAsync("rider@gmail.com")));
        var response = await Exchange(client, session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.WithUsers(async users => Assert.NotNull(await users.FindByLoginAsync("Google", "google-rider")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("accessToken").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(client, session)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/authentication/me");
        Assert.Equal("rider@gmail.com", me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal("Rider", me.GetProperty("displayName").GetString());
        var refreshed = await client.PostAsJsonAsync("/api/authentication/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task PersistentSessionRenewsAfterLongAbsenceAndLogoutRevokesRefresh()
    {
        await using var app = new GoogleApp();
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session);
        var tokens = await (await Exchange(client, session)).Content.ReadFromJsonAsync<JsonElement>();
        app.Clock.Advance(TimeSpan.FromDays(179));
        var renewed = await client.PostAsJsonAsync("/api/authentication/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var freshTokens = await renewed.Content.ReadFromJsonAsync<JsonElement>();
        app.Clock.Advance(TimeSpan.FromDays(179));
        var renewedAgain = await client.PostAsJsonAsync("/api/authentication/refresh",
            new { refreshToken = freshTokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, renewedAgain.StatusCode);
        var latest = await renewedAgain.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", latest.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/authentication/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/authentication/refresh",
            new { refreshToken = latest.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [Fact]
    public async Task CallbackRequiresBrowserCorrelationCookie()
    {
        await using var app = new GoogleApp();
        using var client = app.NewClient();
        var session = await Start(client);
        var redirect = await client.GetAsync(session.AuthorizationUrl);
        var state = QueryHelpers.ParseQuery(redirect.Headers.Location!.Query)["state"].ToString();
        using var otherBrowser = app.NewClient();
        var response = await otherBrowser.GetAsync("/signin-google?code=test-code&state=" + Uri.EscapeDataString(state));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(client, session)).StatusCode);
    }

    [Fact]
    public async Task ExistingEmailRequiresAuthenticatedLinkAndCannotBeTakenOver()
    {
        await using var app = new GoogleApp();
        await app.WithUsers(async users =>
        {
            Assert.True((await users.CreateAsync(new ApplicationUser
                { UserName = "rider@gmail.com", Email = "rider@gmail.com", EmailConfirmed = true }, "Rail-Test123!")).Succeeded);
        });
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session);
        Assert.Equal(HttpStatusCode.Conflict, (await Exchange(client, session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/authentication/google/link/start", null)).StatusCode);

        var passwordLogin = await client.PostAsJsonAsync("/api/authentication/login", new { email = "rider@gmail.com", password = "Rail-Test123!" });
        var tokens = await passwordLogin.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        var linkSession = await Start(client, "google/link/start");
        await app.CompleteBrowserLogin(linkSession);
        using var anonymous = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(anonymous, linkSession)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Exchange(client, linkSession)).StatusCode);
        var loginSession = await Start(anonymous);
        await app.CompleteBrowserLogin(loginSession);
        Assert.Equal(HttpStatusCode.OK, (await Exchange(anonymous, loginSession)).StatusCode);
    }

    [Fact]
    public async Task GoogleLoginPreservesRailTwoFactorAndLockout()
    {
        await using var app = new GoogleApp();
        string recovery = "";
        await app.WithUsers(async users =>
        {
            var user = new ApplicationUser { UserName = "rider@gmail.com", Email = "rider@gmail.com", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("Google", "google-rider", "Google"))).Succeeded);
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            recovery = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        });
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session);
        var required = await Exchange(client, session);
        Assert.Equal("requires_two_factor", (await required.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Exchange(client, session, recovery)).StatusCode);

        var lockedSession = await Start(client);
        await app.CompleteBrowserLogin(lockedSession);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(client, lockedSession, "invalid-recovery-code")).StatusCode);
        // A fresh Google login cannot bypass a locked Rail account.
        var freshSession = await Start(client);
        await app.CompleteBrowserLogin(freshSession);
        var locked = await Exchange(client, freshSession);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.DoesNotContain("requires_two_factor", await locked.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnverifiedEmailAndCancelledLoginDoNotCreateAccounts(bool cancel)
    {
        await using var app = new GoogleApp { VerifiedEmail = false };
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session, cancel);
        Assert.Equal(cancel ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest,
            (await Exchange(client, session)).StatusCode);
        await app.WithUsers(async users => Assert.Null(await users.FindByEmailAsync("rider@gmail.com")));
    }

    [Fact]
    public async Task MissingEmailVerificationCannotRegisterAnAccount()
    {
        await using var app = new GoogleApp { IncludeEmailVerification = false };
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(client, session)).StatusCode);
        await app.WithUsers(async users => Assert.Null(await users.FindByEmailAsync("rider@gmail.com")));
    }

    [Fact]
    public async Task ConcurrentExchangesOnlyIssueOneSetOfTokens()
    {
        await using var app = new GoogleApp();
        using var client = app.NewClient();
        var session = await Start(client);
        await app.CompleteBrowserLogin(session);
        var responses = await Task.WhenAll(Exchange(client, session), Exchange(client, session));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingCredentialsDisableGoogleWithoutBreakingPasswordLogin()
    {
        await using var app = new GoogleApp { Configured = false };
        using var client = app.NewClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsync("/api/authentication/google/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/authentication/login",
                new { email = "missing@gmail.com", password = "Rail-Test123!" })).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredSessionsCannotStartOrExchange(bool completeFirst)
    {
        await using var app = new GoogleApp();
        using var client = app.NewClient();
        var session = await Start(client);
        if (completeFirst) await app.CompleteBrowserLogin(session);
        app.Clock.Advance(GoogleLoginSessions.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(session.AuthorizationUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(client, session)).StatusCode);
    }

    private static async Task<LoginSession> Start(HttpClient client, string path = "google/start")
    {
        var response = await client.PostAsync("/api/authentication/" + path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginSession>())!;
    }

    private static Task<HttpResponseMessage> Exchange(HttpClient client, LoginSession session, string? recovery = null) =>
        client.PostAsJsonAsync("/api/authentication/google/exchange",
            new { session.SessionId, session.ExchangeKey, twoFactorRecoveryCode = recovery });

    private sealed record LoginSession(string SessionId, string ExchangeKey, string AuthorizationUrl);

    private sealed class GoogleApp : WebApplicationFactory<Program>
    {
        public bool VerifiedEmail { get; init; } = true;
        public bool IncludeEmailVerification { get; init; } = true;
        public bool Configured { get; init; } = true;
        public TestClock Clock { get; } = new();
        private readonly string database = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Google:ClientId", "test-client");
            builder.UseSetting("Authentication:Google:ClientSecret", Configured ? "test-secret" : "");
            builder.ConfigureTestServices(services =>
            {
                services.AddLogging(logging => logging.ClearProviders().AddConsole());
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.PostConfigure<GoogleOptions>("Google", options =>
                    options.Backchannel = new HttpClient(new GoogleBackchannel(this)));
            });
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost:7130"), AllowAutoRedirect = false });

        public async Task CompleteBrowserLogin(LoginSession session, bool cancel = false)
        {
            using var browser = NewClient();
            var start = await browser.GetAsync(session.AuthorizationUrl);
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            Assert.Equal("accounts.google.com", start.Headers.Location!.Host);
            var query = QueryHelpers.ParseQuery(start.Headers.Location.Query);
            Assert.Equal("https://localhost:7130/signin-google", query["redirect_uri"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            var state = Uri.EscapeDataString(query["state"].ToString());
            var response = await browser.GetAsync("/signin-google?" + (cancel ? "error=access_denied" : "code=test-code") + "&state=" + state);
            Assert.Equal(cancel ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
                value => value.Contains(".AspNetCore.Identity"));
        }

        public async Task WithUsers(Func<UserManager<ApplicationUser>, Task> action)
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        }
    }

    private sealed class GoogleBackchannel(GoogleApp app) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object data;
            if (request.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token")
                data = new { access_token = "fake-google-token", token_type = "Bearer", expires_in = 3600 };
            else if (request.RequestUri.AbsoluteUri == "https://www.googleapis.com/oauth2/v3/userinfo")
            {
                // Match Google's v3 payload, rather than the legacy v2 id/verified_email fields.
                var userInfo = new Dictionary<string, object>
                    { ["sub"] = "google-rider", ["email"] = "rider@gmail.com", ["name"] = "Rider" };
                if (app.IncludeEmailVerification) userInfo["email_verified"] = app.VerifiedEmail;
                data = userInfo;
            }
            else
                throw new InvalidOperationException("Unexpected Google backchannel endpoint.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
