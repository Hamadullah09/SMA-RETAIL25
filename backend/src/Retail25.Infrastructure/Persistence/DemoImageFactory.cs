using System.IO.Compression;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Makes a small PNG for a demo item.
/// <para>
/// The till's product grid draws tiles when the catalogue has pictures and rows when it does not, and
/// a demo catalogue with no pictures at all can only ever show one of those. Rather than ship binary
/// assets in the repository — which would have to be licensed, reviewed and kept — the seeder draws
/// its own: a coloured square with a diagonal band, keyed off the stock code so the same item is the
/// same picture on every machine.
/// </para>
/// <para>
/// Written by hand because the alternative is a drawing library, and pulling one in to produce a
/// two-tone square is a dependency, a native asset and a CVE feed for something a page of arithmetic
/// does exactly.
/// </para>
/// </summary>
internal static class DemoImageFactory
{
    private const int Size = 96;

    public const string ContentType = "image/png";

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Create(string seed)
    {
        var (r, g, b) = ColourFor(seed);
        return Encode(Draw(r, g, b));
    }

    /// <summary>
    /// Scanlines in PNG's truecolour layout: one filter byte per row, then RGB triplets.
    /// <para>
    /// Filter 0 (none) throughout. Filtering exists to help the compressor find patterns, and a flat
    /// square with one diagonal has no patterns worth finding.
    /// </para>
    /// </summary>
    private static byte[] Draw(byte r, byte g, byte b)
    {
        var raw = new byte[Size * (1 + (Size * 3))];
        var offset = 0;

        for (var y = 0; y < Size; y++)
        {
            raw[offset++] = 0;

            for (var x = 0; x < Size; x++)
            {
                // A band across the diagonal, lightened rather than recoloured, so the tile still
                // reads as one colour at the size a grid actually shows it.
                var onBand = Math.Abs(x - y) < 10 || Math.Abs((Size - 1 - x) - y) < 4;

                raw[offset++] = onBand ? Lighten(r) : r;
                raw[offset++] = onBand ? Lighten(g) : g;
                raw[offset++] = onBand ? Lighten(b) : b;
            }
        }

        return raw;
    }

    private static byte Lighten(byte channel) => (byte)(channel + ((255 - channel) * 45 / 100));

    /// <summary>
    /// A mid-saturation colour from the seed. Kept away from both ends of the lightness range: a tile
    /// has white text on it, and a pastel demo catalogue would prove the layout works and the contrast
    /// does not.
    /// </summary>
    private static (byte R, byte G, byte B) ColourFor(string seed)
    {
        // FNV-1a: short, stable, and well spread across the wheel.
        var hash = 0x811c9dc5u;
        foreach (var character in seed)
        {
            hash ^= character;
            hash *= 0x01000193;
        }

        var hue = hash % 360u / 360.0;
        return FromHsl(hue, saturation: 0.45, lightness: 0.42);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = c * (1 - Math.Abs((h * 6 % 2) - 1));
        var m = lightness - (c / 2);

        var (r, g, b) = (h * 6) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return (Channel(r + m), Channel(g + m), Channel(b + m));
    }

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static byte[] Encode(byte[] raw)
    {
        using var png = new MemoryStream();
        png.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        WriteBigEndian(header[..4], Size);
        WriteBigEndian(header[4..8], Size);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method: adaptive
        header[12] = 0; // interlace: none

        WriteChunk(png, "IHDR", header.ToArray());
        WriteChunk(png, "IDAT", Deflate(raw));
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>zlib, not raw deflate — PNG's IDAT carries the zlib header and Adler-32 trailer.</summary>
    private static byte[] Deflate(byte[] raw)
    {
        using var compressed = new MemoryStream();

        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, data.Length);
        target.Write(length);

        var typeAndData = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
        {
            typeAndData[i] = (byte)type[i];
        }

        data.CopyTo(typeAndData, 4);
        target.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        WriteBigEndian(crc, unchecked((int)Crc32(typeAndData)));
        target.Write(crc);
    }

    private static void WriteBigEndian(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] bytes)
    {
        var c = 0xFFFFFFFFu;

        foreach (var b in bytes)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
