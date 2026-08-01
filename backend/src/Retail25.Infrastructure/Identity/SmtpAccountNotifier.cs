using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Sends account mail over SMTP, or refuses loudly when no relay is configured.
/// <para>
/// The refusal matters more than the sending. A recovery flow that silently drops its mail looks
/// identical to one that works — the user is told "check your inbox" either way — so a deployment
/// can ship with password reset quietly broken and nobody finds out until someone is locked out.
/// With no <c>Mail:Host</c> this throws, the endpoint answers 503, and the operator learns on the
/// first attempt rather than the worst one.
/// </para>
/// <para>
/// In Development, <c>Mail:WriteToLog</c> writes the link to the log instead. That is a real leak of
/// a credential into a log file, which is exactly why it is opt-in and named for what it does.
/// </para>
/// </summary>
public sealed class SmtpAccountNotifier : IAccountNotifier
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpAccountNotifier> _logger;

    public SmtpAccountNotifier(IConfiguration configuration, ILogger<SmtpAccountNotifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(
        string email,
        string displayName,
        string resetLink,
        CancellationToken ct = default)
    {
        var body =
            $"""
             Hello {displayName},

             Someone asked to reset the password for your Retail25 account. Open the link below to
             choose a new one. It can be used once, and it stops working after a short while.

             {resetLink}

             If this was not you, no action is needed — your password has not changed.
             """;

        return SendAsync(email, "Reset your Retail25 password", body, isSensitive: true, ct);
    }

    public Task SendWelcomeAsync(string email, string displayName, CancellationToken ct = default)
    {
        var body =
            $"""
             Hello {displayName},

             A Retail25 account has been created for this address.

             If you did not create it, tell your system administrator — an account you did not ask
             for is worth looking into even when it can do nothing yet.
             """;

        return SendAsync(email, "Your Retail25 account", body, isSensitive: false, ct);
    }

    private async Task SendAsync(string to, string subject, string body, bool isSensitive, CancellationToken ct)
    {
        var host = _configuration["Mail:Host"];

        if (string.IsNullOrWhiteSpace(host))
        {
            if (_configuration.GetValue("Mail:WriteToLog", false))
            {
                // Deliberately at Warning, not Debug: a link in a log is a credential in a log, and
                // it should be visible in the output that it happened.
                _logger.LogWarning(
                    "Mail:Host is not set and Mail:WriteToLog is on, so this message was not sent. "
                    + "To: {To}. Subject: {Subject}.\n{Body}",
                    to,
                    subject,
                    body);

                return;
            }

            throw new InvalidOperationException(
                "No mail relay is configured. Set Mail:Host (and Mail:From) before using account recovery.");
        }

        var from = _configuration["Mail:From"]
            ?? throw new InvalidOperationException("Mail:Host is set but Mail:From is not.");

        using var client = new SmtpClient(host, _configuration.GetValue("Mail:Port", 587))
        {
            EnableSsl = _configuration.GetValue("Mail:UseSsl", true),
        };

        var user = _configuration["Mail:Username"];
        if (!string.IsNullOrWhiteSpace(user))
        {
            client.Credentials = new NetworkCredential(user, _configuration["Mail:Password"]);
        }

        using var message = new MailMessage(from, to, subject, body);

        await client.SendMailAsync(message, ct);

        // The address is logged; the body never is, because for a reset it contains the link.
        _logger.LogInformation(
            "Sent account mail to {To} ({Subject}){Note}",
            to,
            subject,
            isSensitive ? " — contents withheld" : string.Empty);
    }
}
