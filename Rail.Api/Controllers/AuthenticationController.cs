using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Rail.Api.Models;
using Rail.Api.Services;

namespace Rail.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class AuthenticationController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IEmailSender<ApplicationUser> emailSender,
    IOptionsMonitor<BearerTokenOptions> bearerOptions,
    TimeProvider clock,
    ILogger<AuthenticationController> logger,
    GoogleLoginSessions googleSessions,
    IAuthenticationSchemeProvider authenticationSchemes) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityErrors(result);
        await SendConfirmation(user, request.Email);
        return Ok(new { message = "Check your email to confirm your account." });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return Unauthorized();
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
                result = await signIn.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
            else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode))
                result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
        }
        // The bearer handler writes the access and refresh tokens directly to the response.
        return result.Succeeded ? new EmptyResult() : Unauthorized(new { message = "Invalid login or required two-factor code." });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var ticket = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires)
            return Unauthorized();
        var user = await signIn.ValidateSecurityStampAsync(ticket.Principal);
        if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            return Unauthorized();
        return SignIn(await signIn.CreateUserPrincipalAsync(user), IdentityConstants.BearerScheme);
    }

    [AllowAnonymous]
    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string userId, string code, string? changedEmail = null)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || !TryDecode(code, out var token)) return BadRequest();
        var result = changedEmail is null
            ? await users.ConfirmEmailAsync(user, token)
            : await users.ChangeEmailAsync(user, changedEmail, token);
        return result.Succeeded ? Ok() : IdentityErrors(result);
    }

    [AllowAnonymous]
    [HttpPost("resend-confirmation-email")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationEmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
            logger.LogInformation("Confirmation email skipped: no matching account.");
        else if (await users.IsEmailConfirmedAsync(user))
            logger.LogInformation("Confirmation email skipped: account email is already confirmed.");
        else
            await SendConfirmation(user, request.Email);
        return Ok(new { message = "If an account needs confirmation, a confirmation email has been submitted." });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null && await users.IsEmailConfirmedAsync(user))
            await emailSender.SendPasswordResetCodeAsync(user, request.Email,
                Encode(await users.GeneratePasswordResetTokenAsync(user)));
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.IsEmailConfirmedAsync(user) || !TryDecode(request.ResetCode, out var token))
            return IdentityErrors(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid reset request." }));
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        return result.Succeeded ? Ok() : IdentityErrors(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        Response.Headers.CacheControl = "no-store";
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(new
        {
            user.Id, user.UserName, user.Email, user.EmailConfirmed, user.PhoneNumber,
            user.PhoneNumberConfirmed, user.TwoFactorEnabled,
            displayName = (await users.GetClaimsAsync(user)).FirstOrDefault(claim => claim.Type == "rail:display-name")?.Value
                ?? user.UserName,
            roles = await users.GetRolesAsync(user)
        });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? Ok() : IdentityErrors(result);
    }

    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var code = Encode(await users.GenerateChangeEmailTokenAsync(user, request.NewEmail));
        var link = ConfirmationLink(user.Id, code, request.NewEmail);
        await emailSender.SendConfirmationLinkAsync(user, request.NewEmail, link);
        return Ok();
    }

    [HttpPost("2fa")]
    public async Task<IActionResult> TwoFactor(TwoFactorRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (request.Enable == true)
        {
            if (request.ResetSharedKey || string.IsNullOrWhiteSpace(request.TwoFactorCode) ||
                !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode))
                return BadRequest(new { message = "A valid authenticator code is required." });
            var enabled = await users.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded) return IdentityErrors(enabled);
        }
        else if (request.Enable == false || request.ResetSharedKey)
        {
            var disabled = await users.SetTwoFactorEnabledAsync(user, false);
            if (!disabled.Succeeded) return IdentityErrors(disabled);
        }
        if (request.ResetSharedKey || await users.GetAuthenticatorKeyAsync(user) is null)
        {
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) return IdentityErrors(reset);
        }
        IEnumerable<string>? recoveryCodes = null;
        if (request.ResetRecoveryCodes || (request.Enable == true && await users.CountRecoveryCodesAsync(user) == 0))
            recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Ok(new
        {
            sharedKey = await users.GetAuthenticatorKeyAsync(user),
            recoveryCodes, recoveryCodesLeft = await users.CountRecoveryCodesAsync(user),
            isTwoFactorEnabled = await users.GetTwoFactorEnabledAsync(user)
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await users.UpdateSecurityStampAsync(user);
        // Invalidates all refresh tokens. Existing access tokens expire naturally.
        return result.Succeeded ? Ok() : IdentityErrors(result);
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request)
    {
        // TODO : Before enabling application-data writes, coordinate account deletion with restricted user foreign keys, retention and cached content.
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!await users.CheckPasswordAsync(user, request.Password)) return Unauthorized();
        var result = await users.DeleteAsync(user);
        return result.Succeeded ? NoContent() : IdentityErrors(result);
    }

    private async Task SendConfirmation(ApplicationUser user, string email)
    {
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        await emailSender.SendConfirmationLinkAsync(user, email, ConfirmationLink(user.Id, code));
    }

    private string ConfirmationLink(string userId, string code, string? changedEmail = null) =>
        Url.ActionLink(nameof(ConfirmEmail), values: new { userId, code, changedEmail })
        ?? throw new InvalidOperationException("Could not generate confirmation URL.");

    private IActionResult IdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static bool TryDecode(string code, out string token)
    {
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); return true; }
        catch (FormatException) { token = ""; return false; }
    }
}

public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public sealed record ChangeEmailRequest([Required, EmailAddress] string NewEmail);
public sealed record DeleteAccountRequest([Required] string Password);

