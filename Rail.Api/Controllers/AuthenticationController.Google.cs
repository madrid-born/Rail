using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Rail.Api.Models;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

public partial class AuthenticationController
{
    [AllowAnonymous]
    [HttpPost("google/start")]
    public Task<IActionResult> StartGoogleLogin() => CreateGoogleSession(null);

    [HttpPost("google/link/start")]
    public async Task<IActionResult> StartGoogleLink()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            return Unauthorized();
        return await CreateGoogleSession(user.Id);
    }

    [AllowAnonymous]
    [HttpGet("google")]
    public async Task<IActionResult> GoogleLogin([Required, StringLength(43, MinimumLength = 43)] string sessionId)
    {
        Response.Headers.CacheControl = "no-store";
        if (await authenticationSchemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            return GoogleUnavailable();
        if (!googleSessions.Begin(sessionId))
            return BadRequest(new { message = "This Google login has expired or already started." });
        var properties = new AuthenticationProperties { RedirectUri = "/" };
        properties.Items[GoogleLoginSessions.PropertyKey] = sessionId;
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpPost("google/exchange")]
    public async Task<IActionResult> ExchangeGoogleLogin(GoogleExchangeRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var session = googleSessions.Find(request.SessionId, request.ExchangeKey);
        if (session is null) return Unauthorized(new { message = "Invalid or expired Google login." });

        // Serialize exchanges so a completed login can issue tokens only once.
        await session.ExchangeLock.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (session.Consumed || googleSessions.Find(request.SessionId) is null) return Unauthorized();
            GoogleAccount? account;
            lock (session)
            {
                if (!session.Completed)
                {
                    Response.Headers.RetryAfter = "2";
                    return Accepted(new { status = "pending" });
                }
                account = session.Account;
            }
            if (account is null)
            {
                ConsumeGoogleSession(request.SessionId, session);
                return Unauthorized(new { message = "Google sign-in was cancelled or failed. Start again." });
            }

            if (session.LinkUserId is not null)
            {
                var linkingUser = await users.GetUserAsync(User);
                if (linkingUser?.Id != session.LinkUserId || !await signIn.CanSignInAsync(linkingUser) ||
                    await users.IsLockedOutAsync(linkingUser)) return Unauthorized();
                ConsumeGoogleSession(request.SessionId, session);
                if (!account.EmailVerified) return BadRequest(new { message = "Google must verify your email first." });
                var owner = await users.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, account.Subject);
                if (owner is not null)
                    return owner.Id == linkingUser.Id ? Ok(new { status = "linked" })
                        : Conflict(new { message = "This Google account is already linked to another Rail account." });
                var linked = await users.AddLoginAsync(linkingUser,
                    new UserLoginInfo(GoogleDefaults.AuthenticationScheme, account.Subject, "Google"));
                return linked.Succeeded ? Ok(new { status = "linked" }) : IdentityErrors(linked);
            }

            var user = await users.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, account.Subject);
            if (user is null)
            {
                if (!account.EmailVerified || !new EmailAddressAttribute().IsValid(account.Email))
                {
                    ConsumeGoogleSession(request.SessionId, session);
                    return BadRequest(new { message = "Google must provide a verified email address." });
                }
                if (await users.FindByEmailAsync(account.Email) is not null)
                {
                    ConsumeGoogleSession(request.SessionId, session);
                    return Conflict(new { code = "account_link_required", message = "Sign in to your existing Rail account, then link Google." });
                }
                user = new ApplicationUser { UserName = account.Email, Email = account.Email, EmailConfirmed = true };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    ConsumeGoogleSession(request.SessionId, session);
                    return IdentityErrors(created);
                }
                var linked = await users.AddLoginAsync(user,
                    new UserLoginInfo(GoogleDefaults.AuthenticationScheme, account.Subject, "Google"));
                if (!linked.Succeeded)
                {
                    // A concurrent login may have linked the subject first; don't leave a passwordless orphan.
                    var deleted = await users.DeleteAsync(user);
                    if (!deleted.Succeeded) logger.LogError("Failed to clean up an unlinked Google registration.");
                    ConsumeGoogleSession(request.SessionId, session);
                    return IdentityErrors(linked);
                }
                if (!string.IsNullOrWhiteSpace(account.DisplayName))
                {
                    var named = await users.AddClaimAsync(user,
                        new System.Security.Claims.Claim("rail:display-name", account.DisplayName));
                    if (!named.Succeeded) logger.LogWarning("Could not save the Google display name.");
                }
            }

            if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            {
                ConsumeGoogleSession(request.SessionId, session);
                return Unauthorized();
            }
            if (await users.GetTwoFactorEnabledAsync(user))
            {
                bool valid;
                if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
                    valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider,
                        request.TwoFactorCode.Replace(" ", "").Replace("-", ""));
                else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode))
                    valid = (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.TwoFactorRecoveryCode.Replace(" ", ""))).Succeeded;
                else
                    return Unauthorized(new { code = "requires_two_factor", message = "Enter your Rail authenticator or recovery code." });
                if (!valid)
                {
                    await users.AccessFailedAsync(user);
                    if (await users.IsLockedOutAsync(user)) ConsumeGoogleSession(request.SessionId, session);
                    return Unauthorized(new { code = "invalid_two_factor", message = "Invalid two-factor code." });
                }
            }
            var reset = await users.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded) return IdentityErrors(reset);
            ConsumeGoogleSession(request.SessionId, session);
            // Use the same bearer handler and refresh endpoint as password login.
            return SignIn(await signIn.CreateUserPrincipalAsync(user), IdentityConstants.BearerScheme);
        }
        finally
        {
            session.ExchangeLock.Release();
        }
    }

    private async Task<IActionResult> CreateGoogleSession(string? linkUserId)
    {
        Response.Headers.CacheControl = "no-store";
        if (await authenticationSchemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            return GoogleUnavailable();
        var session = googleSessions.Create(linkUserId);
        if (googleSessions.Find(session.Id) is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Too many pending Google logins. Try again shortly." });
        var authorizationUrl = Url.ActionLink(nameof(GoogleLogin), values: new { sessionId = session.Id })
            ?? throw new InvalidOperationException("Could not generate Google login URL.");
        return Ok(new { sessionId = session.Id, exchangeKey = session.ExchangeKey, authorizationUrl, expiresAt = session.ExpiresAt });
    }

    private ObjectResult GoogleUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { message = "Google login is not configured on this API." });

    private void ConsumeGoogleSession(string id, GoogleLoginSessions.Session session)
    {
        session.Consumed = true;
        googleSessions.Remove(id);
    }
}

public sealed record GoogleExchangeRequest(
    [Required, StringLength(43, MinimumLength = 43)] string SessionId,
    [Required, StringLength(43, MinimumLength = 43)] string ExchangeKey,
    [StringLength(32)] string? TwoFactorCode = null,
    [StringLength(128)] string? TwoFactorRecoveryCode = null);
