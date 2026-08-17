using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Retail25.Infrastructure.Identity.Shoppers;

/// <summary>
/// Finds the key that signs the phone app's tokens, and — where nothing has been configured — makes
/// one and keeps it.
/// <para>
/// <see cref="ShopperTokenOptions.SigningKey"/> has no default on purpose: a key shipped in source is
/// a key every deployment shares, and anyone holding it can mint a token for any customer. That rule
/// is right and is not being relaxed here. What it left behind was a deployment that cannot work and
/// says so only at the worst moment — the API starts, the till serves normally, and the first shopper
/// to press Create account gets a 500 from an issuer that refused to construct. Shared hosting has no
/// shell and no environment editor a developer can reach, so "set it on the host" is advice nobody
/// can take on the plan this runs on.
/// </para>
/// <para>
/// So: configuration still wins whenever it is present, and an unconfigured deployment generates 32
/// random bytes once and writes them to <c>App_Data</c>. That directory is chosen for two properties
/// the deployment already depends on. It is the one place the IIS pool identity can reliably write —
/// the database backups live there for the same reason — and the deploy workflow skips it, so a key
/// written on Monday is still there after Friday's deploy. A key that changed on every deploy would
/// sign out every phone in the shop without explanation, which is the exact failure the OpenIddict
/// certificate check upstream exists to prevent.
/// </para>
/// <para>
/// The file is per-host and never in source control, so two deployments never share a key — the
/// property that mattered about refusing a default in the first place.
/// </para>
/// </summary>
internal static class ShopperSigningKey
{
    /// <summary>Sits beside the backups, for the write-permission and survives-deploy reasons above.</summary>
    private const string DirectoryName = "App_Data";

    private const string FileName = "shopper-signing.key";

    /// <summary>
    /// Matches the minimum <see cref="ShopperTokenIssuer"/> enforces. Written base64, so 32 bytes of
    /// entropy become 44 ASCII characters — comfortably past the check, which counts UTF-8 bytes.
    /// </summary>
    private const int KeyBytes = 32;

    /// <summary>
    /// The configured key, the stored key, or a newly stored one — in that order.
    /// <para>
    /// Returns an empty string if the key could not be read or written, which leaves the phone app
    /// disabled rather than half-working. That is the pre-existing contract for an unconfigured
    /// deployment and the caller already handles it.
    /// </para>
    /// </summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration[$"{ShopperTokenOptions.Section}:SigningKey"] ?? string.Empty;

        // An operator who has set a key means it. Never silently prefer a file over that.
        if (Encoding.UTF8.GetByteCount(configured) >= KeyBytes)
        {
            return configured;
        }

        try
        {
            var directory = Path.Combine(environment.ContentRootPath, DirectoryName);
            var path = Path.Combine(directory, FileName);

            if (File.Exists(path))
            {
                var stored = File.ReadAllText(path).Trim();

                if (Encoding.UTF8.GetByteCount(stored) >= KeyBytes)
                {
                    return stored;
                }
            }

            Directory.CreateDirectory(directory);

            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

            // CreateNew rather than a plain write, because two worker processes can start at once —
            // IIS overlaps them on a recycle. The loser of that race must adopt the winner's key, not
            // overwrite it: two keys in rotation would fail validation for whichever half of the
            // phones held the other one.
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(file);
                writer.Write(generated);
            }
            catch (IOException) when (File.Exists(path))
            {
                var winner = File.ReadAllText(path).Trim();

                return Encoding.UTF8.GetByteCount(winner) >= KeyBytes ? winner : string.Empty;
            }

            return generated;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Read-only content root, or a pool identity without write access. The phone app stays
            // off; everything else about the deployment is unaffected.
            return string.Empty;
        }
    }
}
