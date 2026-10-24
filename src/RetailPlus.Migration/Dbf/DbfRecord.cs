using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RetailPlus.Migration.Dbf;

/// <summary>
/// One record of a <see cref="DbfTable"/>. The typed getters follow the canonical rules of tools/dbf/dbf_to_json.py:
/// blank values are null, and anything that can't be read as its declared type throws a
/// <see cref="DbfFormatException"/> naming the file, record and field.
/// </summary>
public sealed partial class DbfRecord
{
    private readonly DbfTable _table;
    private readonly byte[] _bytes;

    internal DbfRecord(DbfTable table, int recordNumber, byte[] bytes)
    {
        _table = table;
        RecordNumber = recordNumber;
        _bytes = bytes;
    }

    public int RecordNumber { get; }
    public bool IsDeleted => _bytes[0] == (byte)'*';
    /// <summary>The whole record as stored, deletion flag first.</summary>
    public ReadOnlySpan<byte> RawRecord => _bytes;

    public ReadOnlySpan<byte> GetBytes(DbfField field) => _bytes.AsSpan(field.Offset, field.Length);

    /// <summary>Character field text, untrimmed, decoded as CP1252.</summary>
    public string GetString(DbfField field) => Cp1252.Decode(GetBytes(field));
    public string GetString(string field) => GetString(_table.GetField(field));

    /// <summary>Character field text with trailing spaces removed (DBF pads with spaces).</summary>
    public string GetTrimmed(string field) => GetString(field).TrimEnd(' ');

    /// <summary>N or F field: null when blank; accepts an optional sign, digits and an optional decimal point.</summary>
    public decimal? GetDecimal(DbfField field)
    {
        var raw = GetString(field);
        var text = raw.Trim(' ', '\0');
        if (text.Length == 0) return null;
        if (!NumberPattern().IsMatch(text)) throw Invalid(field, raw);
        return decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
    public decimal? GetDecimal(string field) => GetDecimal(_table.GetField(field));

    /// <summary>D field: null when blank or all zeros.</summary>
    public DateOnly? GetDate(DbfField field)
    {
        var text = GetString(field);
        if (text.Trim(' ', '\0').Length == 0 || text == "00000000") return null;
        if (DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            text.All(char.IsAsciiDigit))
            return date;
        throw Invalid(field, text);
    }
    public DateOnly? GetDate(string field) => GetDate(_table.GetField(field));

    /// <summary>L field: T/t/Y/y true, F/f/N/n false, space or ? null.</summary>
    public bool? GetLogical(DbfField field)
    {
        var text = GetString(field);
        return text switch
        {
            "T" or "t" or "Y" or "y" => true,
            "F" or "f" or "N" or "n" => false,
            " " or "?" or "\0" => null,
            _ => throw Invalid(field, text),
        };
    }
    public bool? GetLogical(string field) => GetLogical(_table.GetField(field));

    /// <summary>M field: null when the block pointer is blank or 0.</summary>
    public DbfMemo? GetMemo(DbfField field)
    {
        long block;
        var raw = GetBytes(field);
        if (field.Length == 4)
        {
            block = BinaryPrimitives.ReadUInt32LittleEndian(raw);    // Visual FoxPro style pointer
        }
        else
        {
            var pointer = Cp1252.Decode(raw).Trim(' ', '\0');
            if (pointer.Length == 0) return null;
            if (!pointer.All(char.IsAsciiDigit)) throw Invalid(field, "bad block pointer " + pointer);
            block = long.Parse(pointer, CultureInfo.InvariantCulture);
        }
        if (block == 0) return null;
        var (memo, error) = _table.ReadMemo(block);
        return memo ?? throw Invalid(field, error!);
    }
    public DbfMemo? GetMemo(string field) => GetMemo(_table.GetField(field));
    public string? GetMemoText(string field) => GetMemo(field)?.Text;

    private DbfFormatException Invalid(DbfField field, string detail) =>
        new(_table.FilePath, RecordNumber, field.Name, detail);

    // ASCII digits only, exactly as the oracle: \d would also accept other Unicode digits.
    [GeneratedRegex(@"^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)$")]
    private static partial Regex NumberPattern();
}
