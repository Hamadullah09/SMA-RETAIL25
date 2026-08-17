using System.Globalization;
using Retail25.Domain.Common;

namespace Retail25.Domain.Shoppers;

/// <summary>
/// A member of the public who shops with their own phone, and the account they sign in to.
/// <para>
/// Deliberately <b>not</b> an <c>ApplicationUser</c>. Staff identity carries an access level, a role
/// and a permission set, and every one of those is a thing a shopper must never be able to hold — the
/// authorisation behaviour asks <c>ICurrentUser.Permissions</c> and nothing else, so the only durable
/// way to guarantee a customer can never ring up a discount is for customers to live outside the
/// table that grants permissions at all. A shopper principal resolves to the empty permission set,
/// which means every <c>[RequiresPermission]</c> command in the system refuses it structurally rather
/// than by a check somebody has to remember to write.
/// </para>
/// <para>
/// Equally deliberately not a <see cref="Customers.Customer"/>. That record is the store's
/// accounts-receivable and loyalty view of a person, created and edited by staff, and most shoppers
/// will never have one. A shopper may be <em>linked</em> to a customer so loyalty accrues, and the
/// link is optional in both directions.
/// </para>
/// </summary>
public sealed class Shopper : AggregateRoot, IAuditable
{
    public static readonly Error NameRequired =
        new("shopper.name_required", "A first and last name are both required.");

    public static readonly Error EmailInvalid =
        new("shopper.email_invalid", "That does not look like an email address.");

    public static readonly Error PhoneInvalid =
        new("shopper.phone_invalid", "A phone number must be 7–20 digits, optionally starting with '+'.");

    public static readonly Error EmailTaken =
        new("shopper.email_taken", "An account already exists for that email address.");

    public static readonly Error CredentialsInvalid =
        new("shopper.credentials_invalid", "That email and password do not match an account.");

    public static readonly Error Deactivated =
        new("shopper.deactivated", "This account has been deactivated.");

    private Shopper()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    /// <summary>Stored E.164-ish: a leading '+' and digits, with formatting punctuation stripped.</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Stored as entered, for display and for sending receipts.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Upper-cased email, and the column the unique index and every lookup actually use.
    /// <para>
    /// A separate column rather than a case-insensitive collation because the collation is a property
    /// of the database a future operator can change underneath us, and "two accounts now exist for
    /// the same person" is not a failure we want to depend on a server setting to prevent.
    /// </para>
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool EmailConfirmed { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Set once the shopper is matched to the store's own customer record, if ever.</summary>
    public long? CustomerId { get; private set; }

    public DateTimeOffset? LastSignedInAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public string FullName => string.Create(
        CultureInfo.InvariantCulture,
        $"{FirstName} {LastName}");

    /// <summary>
    /// The normalisation every lookup must go through. Public because the query side needs to
    /// normalise the address a caller typed before comparing it, and doing that inline in two places
    /// is how the two places end up disagreeing.
    /// </summary>
    public static string Normalize(string? email)
        => (email ?? string.Empty).Trim().ToUpperInvariant();

    public static Result<Shopper> Create(
        string? firstName,
        string? lastName,
        string? phone,
        string? email,
        string passwordHash)
    {
        var first = (firstName ?? string.Empty).Trim();
        var last = (lastName ?? string.Empty).Trim();

        if (first.Length == 0 || last.Length == 0)
        {
            return Result.Failure<Shopper>(NameRequired);
        }

        var cleanEmail = (email ?? string.Empty).Trim();

        if (!LooksLikeEmail(cleanEmail))
        {
            return Result.Failure<Shopper>(EmailInvalid.With("value", email));
        }

        var cleanPhone = NormalizePhone(phone);

        if (!LooksLikePhone(cleanPhone))
        {
            return Result.Failure<Shopper>(PhoneInvalid.With("value", phone));
        }

        return Result.Success(new Shopper
        {
            FirstName = first,
            LastName = last,
            Phone = cleanPhone,
            Email = cleanEmail,
            NormalizedEmail = Normalize(cleanEmail),
            PasswordHash = passwordHash,
        });
    }

    public void RecordSignIn(DateTimeOffset now) => LastSignedInAt = now;

    public void ConfirmEmail() => EmailConfirmed = true;

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void LinkToCustomer(long customerId) => CustomerId = customerId;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Punctuation humans type — spaces, dashes, brackets — removed, a leading '+' kept.
    /// The stored form is what gets compared, so "+1 (415) 555-0148" and "+14155550148" are one number.
    /// </summary>
    private static string NormalizePhone(string? phone)
    {
        var raw = (phone ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var plus = raw[0] == '+';
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());

        return plus ? "+" + digits : digits;
    }

    private static bool LooksLikePhone(string normalized)
    {
        var digits = normalized.AsSpan();

        if (digits.Length > 0 && digits[0] == '+')
        {
            digits = digits[1..];
        }

        return digits.Length is >= 7 and <= 20;
    }

    /// <summary>
    /// Shape only. Whether an address can actually receive mail is settled by sending to it, not by a
    /// regular expression — so this rejects what is obviously not an address and leaves the rest to
    /// the confirmation email.
    /// </summary>
    private static bool LooksLikeEmail(string email)
    {
        if (email.Length is 0 or > 200)
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var domain = email.AsSpan(at + 1);

        return domain.Length >= 3
            && domain.Contains('.')
            && domain[0] != '.'
            && domain[^1] != '.'
            && !email.Contains(' ', StringComparison.Ordinal);
    }
}
