using System.Text;

namespace RetailPlus.Migration.Dbf;

/// <summary>
/// Decodes Retail Plus text (Windows code page 1252). The five bytes CP1252 leaves undefined (0x81, 0x8D, 0x8F,
/// 0x90, 0x9D) become the C1 control characters U+0081 etc., so no byte is ever lost or replaced. The Python oracle
/// (tools/dbf/dbf_to_json.py) uses the same rule.
/// </summary>
public static class Cp1252
{
    private static readonly char[] Table = BuildTable();

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return "";
        return string.Create(bytes.Length, bytes.ToArray(), static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++) chars[i] = Table[source[i]];
        });
    }

    private static char[] BuildTable()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var strict = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var table = new char[256];
        for (int b = 0; b < 256; b++)
        {
            try
            {
                table[b] = strict.GetString([(byte)b])[0];
            }
            catch (DecoderFallbackException)
            {
                table[b] = (char)b;
            }
        }
        return table;
    }
}
