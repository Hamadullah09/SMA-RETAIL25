using System.Security.Cryptography;
using System.Text;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// The station's server key in station.json, encrypted with Windows DPAPI for the Windows user the till runs as: a copy
/// of the file on another computer or account is of no use.
/// </summary>
public static class StationKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SMA RETAIL station key");

    public static string Protect(string key) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The key, or null when there is none or it cannot be read here.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
