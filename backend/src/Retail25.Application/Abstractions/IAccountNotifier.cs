namespace Retail25.Application.Abstractions;

/// <summary>
/// Delivers the one-time links account recovery depends on.
/// <para>
/// An abstraction rather than an SMTP client because how a shop sends mail is a deployment decision,
/// not an application one — a chain runs a relay, a single store uses whatever its ISP gave it, and
/// a hosted deployment uses an API. The application only needs to know the message left the building.
/// </para>
/// </summary>
public interface IAccountNotifier
{
    /// <summary>
    /// Sends a password-reset link.
    /// </summary>
    /// <param name="email">The address the user asked us to send to.</param>
    /// <param name="displayName">Who the message is addressed to.</param>
    /// <param name="resetLink">A complete, single-use URL. Never log this — it is a credential.</param>
    Task SendPasswordResetAsync(string email, string displayName, string resetLink, CancellationToken ct = default);

    /// <summary>Confirms an account was created, so an unexpected sign-up is visible to its owner.</summary>
    Task SendWelcomeAsync(string email, string displayName, CancellationToken ct = default);
}
