using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RetailPlus.Cutover;

/// <summary>
/// The ids rows get in the new database. Retail25 keys its rows with bigint identities, not GUIDs, so each row's id is
/// made from its table and key: the same Retail25 row gets the same id on every run, which is what makes a second run
/// update rows instead of adding them again. The ids are name-based UUIDs (version 8, from SHA-256), so they cannot
/// collide with the version 7 ids the program makes for new rows.
/// </summary>
public static class StableIds
{
    /// <summary>The id of the row <paramref name="key"/> of <paramref name="kind"/> (a Retail25 table, or a kind of row made from one).</summary>
    public static Guid For(string kind, long key) => For(kind, key.ToString(CultureInfo.InvariantCulture));

    public static Guid For(string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes("retail25/" + kind + "/" + key), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);   // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);   // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>The cutover was not started: the source or target is not what it should be.</summary>
public sealed class CutoverRefusedException : Exception
{
    public CutoverRefusedException() { }
    public CutoverRefusedException(string message) : base(message) { }
    public CutoverRefusedException(string message, Exception innerException) : base(message, innerException) { }
}
