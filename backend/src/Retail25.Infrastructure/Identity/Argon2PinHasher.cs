using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Argon2id for staff PINs (doc 07 §POS fast user switching).
/// <para>
/// A PIN is short and low-entropy by nature — a cashier cannot type a passphrase between customers.
/// The hash therefore has to carry the cost the secret does not: four iterations over 64 MB makes an
/// offline attack on a stolen table expensive even though the search space is only ten thousand.
/// </para>
/// <para>
/// The parameters are stored in the hash string, so raising them later does not invalidate existing
/// PINs — old ones keep verifying at their original cost and are re-hashed the next time they are set.
/// </para>
/// </summary>
public sealed class Argon2PinHasher : IPinHasher
{
    private const string Prefix = "argon2id";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DegreeOfParallelism = 2;
    private const int Iterations = 4;
    private const int MemoryKb = 64 * 1024;

    /// <summary>
    /// Derivations are serialised, for two independent reasons.
    /// <para>
    /// The underlying Argon2 implementation returns wrong digests when instances run concurrently —
    /// verified by a test that passes in isolation and fails under parallel execution. At a till that
    /// would mean two cashiers entering PINs at the same moment could each get the other's answer:
    /// a correct PIN refused, or worse, a wrong one accepted.
    /// </para>
    /// <para>
    /// Independently, each derivation reserves 64 MB. Twenty concurrent PIN checks would reserve
    /// 1.3 GB, which turns an unauthenticated endpoint into a memory-exhaustion lever. Serialising
    /// bounds that at one buffer, and costs nothing real: PIN checks happen a handful of times a
    /// minute per till, and Argon2 is deliberately slow anyway.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim DerivationGate = new(1, 1);

    public string Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, Iterations, MemoryKb, DegreeOfParallelism);

        return string.Join('$',
            Prefix,
            Iterations.ToString(CultureInfo.InvariantCulture),
            MemoryKb.ToString(CultureInfo.InvariantCulture),
            DegreeOfParallelism.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string pin, string hash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var parts = hash.Split('$');

        if (parts.Length != 6
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memoryKb)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallelism))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            expected = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(pin, salt, iterations, memoryKb, parallelism);

        // Constant time: a comparison that returns on the first wrong byte leaks how much matched,
        // which over enough attempts is a working oracle.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations, int memoryKb, int parallelism)
    {
        DerivationGate.Wait();

        try
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(pin))
            {
                Salt = salt,
                Iterations = iterations,
                MemorySize = memoryKb,
                DegreeOfParallelism = parallelism,
            };

            return argon.GetBytes(HashBytes);
        }
        finally
        {
            DerivationGate.Release();
        }
    }
}
