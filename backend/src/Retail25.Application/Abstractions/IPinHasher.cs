namespace Retail25.Application.Abstractions;

/// <summary>
/// Hashes and verifies staff PINs (doc 07 §POS fast user switching).
/// <para>
/// A PIN is short and low-entropy by nature — a cashier cannot type a passphrase between customers —
/// so the hash has to carry the cost that the secret does not. Argon2id with a per-user salt makes an
/// offline attack on a stolen table expensive even though the search space is ten thousand.
/// </para>
/// <para>
/// Verification is constant-time. A comparison that returns early on the first wrong byte leaks how
/// much of the hash matched, which over enough attempts is a working oracle.
/// </para>
/// </summary>
public interface IPinHasher
{
    string Hash(string pin);

    bool Verify(string pin, string hash);
}

/// <summary>
/// Where the audit trail gets its request context (doc 07 §Audit).
/// <para>
/// The IP address and correlation id live on the HTTP request, which the application layer does not
/// know about. They matter after the fact: "which till" and "which machine" are different questions
/// during a loss-prevention review, and the correlation id is what ties an audit row to the logs and
/// traces for the same request.
/// </para>
/// </summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? CorrelationId { get; }

    string? UserAgent { get; }
}
