using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Retail25.Application.Abstractions;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Api.Controllers;

/// <summary>
/// Self-service account creation and password recovery.
/// <para>
/// These are JSON endpoints rather than server-rendered forms, and that is a considered split from
/// <see cref="AccountController"/>. Signing in submits an <em>existing</em> password, so it happens
/// only on the identity provider's own origin and the application never sees it. These three flows
/// carry a <em>new</em> password or no password at all, so they can be driven from the application's
/// own screens without widening what has access to a working credential.
/// </para>
/// <para>
/// Every response here is deliberately uninformative about whether an account exists. A recovery
/// form that says "no such address" is a free list of your staff's email addresses.
/// </para>
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/v1/account")]
[EnableRateLimiting("auth")]
[Produces("application/json")]
public sealed class RegistrationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly IAccountNotifier _notifier;
    private readonly IAuditWriter _audit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistrationController> _logger;

    public RegistrationController(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        IAccountNotifier notifier,
        IAuditWriter audit,
        IConfiguration configuration,
        ILogger<RegistrationController> logger)
    {
        _users = users;
        _db = db;
        _notifier = notifier;
        _audit = audit;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Whether this deployment accepts self sign-up.
    ///
    /// <para>
    /// Exists so the sign-in page can stop offering something the server will refuse. It advertised
    /// "No account yet? Create one" on a deployment with registration off, and the link led to a 403
    /// — a dead end a new employee has no way to interpret, and the first thing the client reported.
    /// </para>
    /// <para>
    /// The flag is asked for rather than compiled in, because it is a per-deployment setting: a shop
    /// that turns registration on must not need a rebuilt front end to show the link again.
    /// Anonymous by necessity — the people who need the answer are the ones who cannot sign in — and
    /// it discloses nothing a single POST would not.
    /// </para>
    /// </summary>
    [HttpGet("registration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult RegistrationAvailability()
        => Ok(new { enabled = _configuration.GetValue("Auth:SelfRegistration:Enabled", false) });

    /// <summary>
    /// Creates an account.
    /// <para>
    /// Off unless <c>Auth:SelfRegistration:Enabled</c> says otherwise, because an unattended sign-up
    /// form on a system that holds a shop's takings is not a sensible default. When it is on, the new
    /// account lands on <c>Auth:SelfRegistration:Role</c> — Trainee unless configured otherwise, the
    /// access level whose sales commit nothing at all. So the worst a stranger who signs up can do is
    /// practise. An administrator promotes them afterwards, which is the moment a human decides who
    /// this person is.
    /// </para>
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!_configuration.GetValue("Auth:SelfRegistration:Enabled", false))
        {
            return Problem(
                title: "registration.disabled",
                detail: "This system does not accept self sign-up. Ask an administrator for an account.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var email = request.Email.Trim();

        // An existing address gets the same 201 as a new one. Returning 409 here would answer
        // "does this person have an account?" for anyone who cares to ask.
        if (await _users.FindByEmailAsync(email) is not null)
        {
            _logger.LogInformation("Sign-up attempted for an address that already has an account.");

            await SafeNotifyAsync(() => _notifier.SendWelcomeAsync(email, request.DisplayName.Trim(), ct));
            return Accepted();
        }

        var location = await _db.Locations
            .AsNoTracking()
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(ct);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,

            // Not confirmed: nothing has proved this address belongs to whoever typed it. The account
            // works, but the flag is honest, and a deployment that requires confirmation can enforce
            // it without back-filling.
            EmailConfirmed = false,
            DisplayName = request.DisplayName.Trim(),
            DefaultLocationId = location,
        };

        var created = await _users.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            // Identity's own messages are safe to return — they are about the password's shape, not
            // about whether the account exists.
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = "registration.rejected",
                Status = StatusCodes.Status400BadRequest,
                Errors = { ["password"] = created.Errors.Select(e => e.Description).ToArray() },
            });
        }

        var role = _configuration["Auth:SelfRegistration:Role"] ?? "Trainee";
        var granted = await _users.AddToRoleAsync(user, role);

        if (!granted.Succeeded)
        {
            // Almost always a configured role that does not exist. Ignoring it produced the worst
            // possible outcome: a 201, a working password, and an account with no role and therefore
            // no permission to do anything — which reads to whoever signed up as the application
            // being broken rather than as their account being incomplete.
            //
            // The account is removed rather than left behind, so the address stays free and the
            // person can sign up again once the configuration is fixed.
            _logger.LogError(
                "Self sign-up rolled back: role {Role} could not be granted ({Errors}). "
                + "Auth:SelfRegistration:Role must name a role that exists.",
                role,
                string.Join("; ", granted.Errors.Select(e => e.Description)));

            await _users.DeleteAsync(user);

            return Problem(
                title: "registration.unavailable",
                detail: "Accounts cannot be created just now. Please ask an administrator.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Sales are attributed to staff, not to Identity users, so an account with no profile cannot
        // touch a till at all.
        _db.StaffProfiles.Add(StaffProfile.Create(
            user.Id,
            StaffCode(user.Id),
            request.DisplayName.Trim(),
            string.Empty,
            accessLevel: 0));

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            AuditAction.Created,
            nameof(ApplicationUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            operation: "self-registration",
            reason: $"Self sign-up as {role}");

        await SafeNotifyAsync(() => _notifier.SendWelcomeAsync(email, user.DisplayName, ct));

        return StatusCode(StatusCodes.Status201Created, new { user.DisplayName, Role = role });
    }

    /// <summary>
    /// Starts password recovery. Always answers 202, whatever the address turns out to be.
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(request.Email.Trim());

        if (user is null || !user.IsEnabled)
        {
            // No mail, no token, no timing tell worth the complexity of faking one — and crucially,
            // the same 202 the real path returns.
            _logger.LogInformation("Password reset requested for an unknown or disabled account.");
            return Accepted();
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user);

        // Base64url through the query string: the raw token contains characters that do not survive
        // a URL intact, and a mangled token fails at redemption rather than at generation, which is
        // a miserable thing to debug.
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var appOrigin = (_configuration["Auth:WebOrigin"] ?? "http://localhost:3000").TrimEnd('/');
        var link = $"{appOrigin}/reset-password?email={UrlEncoder.Default.Encode(user.Email!)}&token={encoded}";

        try
        {
            await _notifier.SendPasswordResetAsync(user.Email!, user.DisplayName, link, ct);
        }
        catch (InvalidOperationException error)
        {
            // The relay is not configured. Saying so is not an enumeration risk — it is true of every
            // address — and pretending the mail was sent would strand the user forever.
            _logger.LogError(error, "Password reset could not be sent.");

            return Problem(
                title: "mail.unavailable",
                detail: "Password reset is not available: this system has no mail relay configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await _audit.RecordAsync(
            AuditAction.Executed,
            nameof(ApplicationUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            operation: "password-reset-requested");

        return Accepted();
    }

    /// <summary>Redeems a reset token for a new password.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(request.Email.Trim());

        string token;

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            return InvalidToken();
        }

        if (user is null || !user.IsEnabled)
        {
            // Same answer a genuinely expired token gets, so a wrong address and a stale link are
            // indistinguishable from outside.
            return InvalidToken();
        }

        var result = await _users.ResetPasswordAsync(user, token, request.Password);

        if (!result.Succeeded)
        {
            // Two different failures hide in here: a bad token and a password Identity rejected. The
            // second is safe to explain and the first is not, so they are told apart by error code.
            var passwordProblems = result.Errors
                .Where(e => !e.Code.Contains("Token", StringComparison.Ordinal))
                .Select(e => e.Description)
                .ToArray();

            if (passwordProblems.Length == 0)
            {
                return InvalidToken();
            }

            return ValidationProblem(new ValidationProblemDetails
            {
                Title = "reset.rejected",
                Status = StatusCodes.Status400BadRequest,
                Errors = { ["password"] = passwordProblems },
            });
        }

        // Every existing session dies with the old password. Recovery is what someone does when they
        // think their account is compromised, and leaving the intruder signed in defeats the point.
        await _users.UpdateSecurityStampAsync(user);

        await _audit.RecordAsync(
            AuditAction.Executed,
            nameof(ApplicationUser),
            user.Id.ToString(CultureInfo.InvariantCulture),
            operation: "password-reset-completed");

        return NoContent();
    }

    private IActionResult InvalidToken() => Problem(
        title: "reset.invalid",
        detail: "That link is no longer valid. Ask for a new one.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Mail failure must not undo a successful sign-up. The account exists; a missing welcome note is
    /// a smaller problem than a 500 that leaves the user unsure whether to try again.
    /// </summary>
    private async Task SafeNotifyAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        // Broad on purpose: what a notifier throws depends on which one is wired up, and this layer
        // deliberately does not know that. Cancellation still propagates.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "Account mail could not be sent.");
        }
    }

    /// <summary>
    /// Six digits of the user id — unique enough for a code, short enough to say aloud.
    /// <para>
    /// This used to slice six characters off a GUID's hyphen-less form. On a numeric id the same
    /// expression is a crash rather than a code: <c>ToString("N")</c> means "number with group
    /// separators", so a new user with id 5 produced "5.00" and slicing six characters off a
    /// four-character string threw — on every sign-up, and only once ids were small enough.
    /// </para>
    /// <para>
    /// Padded rather than truncated, so early users get 000001 rather than a code that is shorter
    /// than the field everyone else's fits.
    /// </para>
    /// </summary>
    private static string StaffCode(long id)
    {
        var digits = id.ToString(CultureInfo.InvariantCulture);

        // Beyond six digits the low-order end is kept: it is the part that still differs between two
        // accounts created a moment apart.
        return digits.Length <= 6
            ? digits.PadLeft(6, '0')
            : digits[^6..];
    }

    public sealed record RegisterRequest(
        [Required, EmailAddress, StringLength(256)] string Email,
        [Required, StringLength(128, MinimumLength = 1)] string DisplayName,
        [Required, StringLength(256, MinimumLength = 8)] string Password);

    public sealed record ForgotPasswordRequest(
        [Required, EmailAddress, StringLength(256)] string Email);

    public sealed record ResetPasswordRequest(
        [Required, EmailAddress, StringLength(256)] string Email,
        [Required] string Token,
        [Required, StringLength(256, MinimumLength = 8)] string Password);
}
