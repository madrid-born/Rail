using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;
using Rail.Api.Models;

namespace Rail.Api.Services;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;
}

public sealed class IdentityEmailSender(IOptions<SmtpOptions> options, ILogger<IdentityEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your Rail email", $"Confirm your email: {confirmationLink}");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your Rail password", $"Reset your password: {resetLink}");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "Reset your Rail password", $"Your password reset code is: {resetCode}");

    private async Task SendAsync(string email, string subject, string body)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.From))
            throw new InvalidOperationException("Configure Email:Host and Email:From before sending Identity emails.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = 30_000 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var security = settings.EnableSsl
            ? (settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;
        var stage = "connecting";
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, security, timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.UserName))
            {
                stage = "authenticating";
                if (string.IsNullOrWhiteSpace(settings.Password))
                    throw new InvalidOperationException("Configure Email:Password in user secrets or environment variables.");
                await client.AuthenticateAsync(settings.UserName, settings.Password, timeout.Token);
            }
            stage = "sending";
            await client.SendAsync(message, timeout.Token);
            logger.LogInformation("SMTP server {Host}:{Port} accepted the email. Message ID: {MessageId}",
                settings.Host, settings.Port, message.MessageId);
        }
        catch (Exception exception)
        {
            // Never log SMTP protocol traffic, credentials, recipients, or email tokens.
            logger.LogError("SMTP failure while {Stage} with {Host}:{Port}. Error type: {ErrorType}",
                stage, settings.Host, settings.Port, exception.GetType().Name);
            throw;
        }
        // Delivery already succeeded; a disconnect error must not trigger a duplicate send.
        try { await client.DisconnectAsync(true, timeout.Token); }
        catch (Exception exception) when (exception is IOException or ProtocolException or OperationCanceledException)
        {
            logger.LogWarning("SMTP disconnect failed after delivery. Error type: {ErrorType}", exception.GetType().Name);
        }
    }
}



