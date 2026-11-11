using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace Rail.Api.Services;

public static class GoogleAuthentication
{
    public static void Configure(GoogleOptions options, IConfiguration configuration)
    {
        options.ClientId = configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        options.RemoteAuthenticationTimeout = GoogleLoginSessions.Lifetime;
        // The default /oauth2/v3/userinfo endpoint uses the OpenID Connect field name.
        options.ClaimActions.MapJsonKey("rail:google-email-verified", "email_verified");
        options.Events.OnTicketReceived = async context =>
        {
            var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            var verified = bool.TryParse(context.Principal?.FindFirstValue("rail:google-email-verified"), out var value) && value;
            string? id = null;
            context.Properties?.Items.TryGetValue(GoogleLoginSessions.PropertyKey, out id);
            context.HttpContext.RequestServices.GetRequiredService<GoogleLoginSessions>().Complete(id,
                !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(email)
                    ? new GoogleAccount(subject, email, verified) : null);
            // Do not issue an Identity cookie or put Rail tokens in the browser URL.
            context.HandleResponse();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Google verification completed. Return to Rail to finish signing in.");
        };
        options.Events.OnRemoteFailure = async context =>
        {
            string? id = null;
            context.Properties?.Items.TryGetValue(GoogleLoginSessions.PropertyKey, out id);
            context.HttpContext.RequestServices.GetRequiredService<GoogleLoginSessions>().Complete(id, null);
            context.HandleResponse();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Google sign-in was cancelled or failed. Return to Rail and try again.");
        };
    }
}
