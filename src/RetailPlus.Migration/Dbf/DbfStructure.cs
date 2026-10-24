namespace RetailPlus.Migration.Dbf;

/// <summary>The fixed 32-byte DBF file header.</summary>
/// <param name="Version">0x03 for dBASE III without memo, 0xF5 for FoxPro 2.x with FPT memo.</param>
/// <param name="LastUpdateYear">Raw byte. Retail Plus writes a two-digit year (2026 = 26).</param>
/// <param name="RecordCount">Record count as stored in the header; see <see cref="DbfTable.RecordsPresent"/>.</param>
/// <param name="TableFlags">Bit 0 set when a production CDX index belongs to the table.</param>
/// <param name="CodePage">0x03 = Windows ANSI 1252.</param>
public sealed record DbfHeader(
    byte Version,
    byte LastUpdateYear,
    byte LastUpdateMonth,
    byte LastUpdateDay,
    int RecordCount,
    int HeaderLength,
    int RecordLength,
    byte TableFlags,
    byte CodePage);

/// <summary>A field descriptor.</summary>
/// <param name="Type">C, N, F, D, L, M (others are reported as unsupported).</param>
/// <param name="Length">Real length; character fields longer than 255 use the decimals byte (Clipper/VO).</param>
/// <param name="Offset">Position in the record, after the deletion flag byte.</param>
public sealed record DbfField(string Name, char Type, int Length, int Decimals, int Offset);

/// <summary>A memo value: FPT block type (1 = text, 0 = picture/binary) and its text.</summary>
public sealed record DbfMemo(int Type, string Text);
