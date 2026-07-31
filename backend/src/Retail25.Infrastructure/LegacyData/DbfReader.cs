using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Retail25.Infrastructure.LegacyData;

/// <summary>One column as the file's header describes it.</summary>
/// <param name="Type">
/// The dBase type letter. C character, N numeric, F float, D date, L logical, M memo, plus the
/// FoxPro additions I (32-bit int), Y (currency), T (datetime), B (double).
/// </param>
public sealed record DbfField(string Name, char Type, int Length, int DecimalCount, int Offset);

/// <summary>What the header says about a table, before a single row is read.</summary>
public sealed record DbfHeader(
    byte Version,
    DateOnly LastUpdated,
    int RecordCount,
    int HeaderLength,
    int RecordLength,
    IReadOnlyList<DbfField> Fields)
{
    /// <summary>True when a column needs the companion memo file to be readable.</summary>
    public bool HasMemoFields => Fields.Any(f => f.Type is 'M' or 'G' or 'P');

    /// <summary>
    /// The version byte's low nibble is the table kind; 0x03 is dBase III+, 0x30/0x31/0x32 are
    /// Visual FoxPro. Only used for reporting — the record layout is the same for what we read.
    /// </summary>
    public string Describe() => Version switch
    {
        0x02 => "FoxBASE",
        0x03 => "dBase III+ (no memo)",
        0x30 or 0x31 or 0x32 => "Visual FoxPro",
        0x83 => "dBase III+ with memo",
        0x8B or 0x8E => "dBase IV with memo",
        0xF5 => "FoxPro 2.x with memo",
        _ => $"unknown (0x{Version:X2})",
    };
}

/// <summary>One record: its fields by name, and whether it was flagged deleted.</summary>
public sealed record DbfRecord(IReadOnlyDictionary<string, string?> Fields, bool IsDeleted);

/// <summary>
/// A hand-rolled reader for dBase III+ / FoxPro tables and their <c>.FPT</c> memo files
/// (doc 09 §3).
/// <para>
/// Hand-rolled on purpose. The format is small, fixed and thoroughly documented, and the .NET
/// packages that read it are variously abandoned, Windows-only or licensed awkwardly. Depending on
/// one of those for the single most important step of a cutover — the one where a shop's entire
/// history either arrives intact or does not — is a worse trade than two hundred lines of byte
/// offsets that can be read and checked.
/// </para>
/// </summary>
public sealed class DbfReader : IDisposable
{
    /// <summary>Marks a record the legacy system deleted but never packed away.</summary>
    private const byte DeletedFlag = (byte)'*';

    private const byte HeaderTerminator = 0x0D;

    /// <summary>Each field descriptor in the header is exactly 32 bytes.</summary>
    private const int FieldDescriptorLength = 32;

    private readonly Stream _table;
    private readonly Stream? _memo;
    private readonly Encoding _encoding;
    private readonly int _memoBlockSize;
    private readonly bool _leaveOpen;

    private DbfReader(Stream table, Stream? memo, Encoding encoding, DbfHeader header, int memoBlockSize, bool leaveOpen)
    {
        _table = table;
        _memo = memo;
        _encoding = encoding;
        _memoBlockSize = memoBlockSize;
        _leaveOpen = leaveOpen;
        Header = header;
    }

    public DbfHeader Header { get; }

    /// <summary>
    /// Opens a table and, if one sits beside it, its memo file.
    /// <para>
    /// CP1252 by default. The legacy files are DOS-era and often CP437, but the two agree on
    /// everything below 128 — which is all a stock code, a phone number or an English product name
    /// ever contains — and CP1252 is right far more often for the accented characters that do turn
    /// up. Override it when a supplier's name comes back looking wrong.
    /// </para>
    /// </summary>
    public static DbfReader Open(string tablePath, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);

        var table = File.OpenRead(tablePath);
        var memoPath = FindMemoFile(tablePath);
        var memo = memoPath is null ? null : File.OpenRead(memoPath);

        return Create(table, memo, encoding, leaveOpen: false);
    }

    /// <summary>The same, over streams — which is what the tests and the upload path use.</summary>
    public static DbfReader Create(Stream table, Stream? memo = null, Encoding? encoding = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(table);

        var resolved = encoding ?? Cp1252();
        var header = ReadHeader(table, resolved);
        var blockSize = memo is null ? 0 : ReadMemoBlockSize(memo);

        return new DbfReader(table, memo, resolved, header, blockSize, leaveOpen);
    }

    /// <summary>
    /// Every record, streamed. Deleted records are yielded with their flag rather than skipped —
    /// whether they matter is a decision for the importer, and a legacy table that has never been
    /// packed can be half deleted rows.
    /// </summary>
    public IEnumerable<DbfRecord> ReadRecords()
    {
        _table.Seek(Header.HeaderLength, SeekOrigin.Begin);

        var buffer = new byte[Header.RecordLength];

        for (var index = 0; index < Header.RecordCount; index++)
        {
            if (!ReadExactly(_table, buffer))
            {
                // A truncated file is common enough — a copy interrupted, a floppy image cut short.
                // Stopping quietly here and reporting the shortfall from the row count is kinder
                // than throwing halfway through an analysis.
                yield break;
            }

            var fields = new Dictionary<string, string?>(Header.Fields.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var field in Header.Fields)
            {
                fields[field.Name] = ReadField(buffer, field);
            }

            yield return new DbfRecord(fields, buffer[0] == DeletedFlag);
        }
    }

    private string? ReadField(byte[] record, DbfField field)
    {
        var raw = _encoding.GetString(record, field.Offset, field.Length);

        return field.Type switch
        {
            'M' or 'G' or 'P' => ReadMemo(raw),
            'D' => ParseDbfDate(raw),
            'L' => ParseLogical(raw),

            // Numerics are left as the text the file holds. Parsing here would mean deciding what a
            // blank or a row of asterisks means, and that is the importer's decision to make and
            // report on, not the reader's to make silently.
            _ => Blank(raw.Trim()),
        };
    }

    /// <summary>
    /// Pulls a memo out of the <c>.FPT</c>. A memo column holds a block number, either as text or as
    /// a 32-bit integer depending on the dialect — both appear in the wild, so both are handled.
    /// </summary>
    private string? ReadMemo(string raw)
    {
        if (_memo is null || _memoBlockSize == 0)
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('\0');

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var block) || block <= 0)
        {
            return null;
        }

        var offset = (long)block * _memoBlockSize;

        if (offset + 8 > _memo.Length)
        {
            return null;
        }

        _memo.Seek(offset, SeekOrigin.Begin);

        var head = new byte[8];

        if (!ReadExactly(_memo, head))
        {
            return null;
        }

        // FPT block header: big-endian type then big-endian length. Type 1 is text; picture and
        // object blocks are binary and there is nothing useful to put in a text staging column.
        var type = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(4, 4));

        if (type != 1 || length <= 0 || offset + 8 + length > _memo.Length)
        {
            return null;
        }

        var content = new byte[length];

        return ReadExactly(_memo, content) ? Blank(_encoding.GetString(content).TrimEnd('\0').Trim()) : null;
    }

    /// <summary>
    /// dBase dates are eight characters, <c>YYYYMMDD</c>, and blank when unset. Returned in ISO form
    /// so a staging column holds something a later parse can rely on.
    /// </summary>
    public static string? ParseDbfDate(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.Length != 8 || !DateOnly.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A logical is one character: T/Y/t/y true, F/N/f/n false, <c>?</c> or space unset. Returned as
    /// "true"/"false" so staging holds one spelling rather than six.
    /// </summary>
    public static string? ParseLogical(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "T" or "Y" => "true",
        "F" or "N" => "false",
        _ => null,
    };

    private static DbfHeader ReadHeader(Stream table, Encoding encoding)
    {
        table.Seek(0, SeekOrigin.Begin);

        var head = new byte[32];

        if (!ReadExactly(table, head))
        {
            throw new InvalidDataException("The file is too short to be a DBF table.");
        }

        var version = head[0];

        // Bytes 1–3 are the last-update date as year-since-1900, month, day. A year below 70 is
        // read as 20xx, which is the convention every DBF tool settled on.
        var year = head[1] < 70 ? 2000 + head[1] : 1900 + head[1];
        var month = Math.Clamp(head[2], (byte)1, (byte)12);
        var day = Math.Clamp(head[3], (byte)1, (byte)31);

        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4, 4));
        var headerLength = BinaryPrimitives.ReadInt16LittleEndian(head.AsSpan(8, 2));
        var recordLength = BinaryPrimitives.ReadInt16LittleEndian(head.AsSpan(10, 2));

        if (headerLength < 33 || recordLength < 1)
        {
            throw new InvalidDataException(
                $"The header is not a valid DBF: header length {headerLength}, record length {recordLength}.");
        }

        var fields = new List<DbfField>();
        var descriptor = new byte[FieldDescriptorLength];

        // The first byte of every record is the deletion flag, so field data starts at 1.
        var offset = 1;

        while (table.Position < headerLength - 1)
        {
            var first = table.ReadByte();

            // 0x0D ends the descriptor list; -1 is end of stream on a truncated header.
            if (first == HeaderTerminator || first < 0)
            {
                break;
            }

            descriptor[0] = (byte)first;

            if (!ReadExactly(table, descriptor.AsSpan(1)))
            {
                break;
            }

            var name = encoding.GetString(descriptor, 0, 11).TrimEnd('\0', ' ');

            if (name.Length == 0)
            {
                break;
            }

            var type = (char)descriptor[11];
            var length = descriptor[16];
            var decimals = descriptor[17];

            fields.Add(new DbfField(name, type, length, decimals, offset));
            offset += length;
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("The header declares no columns.");
        }

        return new DbfHeader(
            version,
            new DateOnly(year, month, day),
            recordCount,
            headerLength,
            recordLength,
            fields);
    }

    /// <summary>
    /// The FPT header's block size lives at offset 6 as a big-endian 16-bit value. Zero means the
    /// 512-byte default, which is what dBase III wrote.
    /// </summary>
    private static int ReadMemoBlockSize(Stream memo)
    {
        memo.Seek(0, SeekOrigin.Begin);

        var head = new byte[8];

        if (!ReadExactly(memo, head))
        {
            return 512;
        }

        var declared = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(6, 2));

        return declared == 0 ? 512 : declared;
    }

    /// <summary>
    /// The companion memo file, whatever case the filesystem holds it in. A cutover set copied off a
    /// DOS machine is usually all upper case, and Linux will not find <c>.fpt</c> for it.
    /// </summary>
    private static string? FindMemoFile(string tablePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(tablePath))!;
        var stem = Path.GetFileNameWithoutExtension(tablePath);

        foreach (var extension in new[] { ".fpt", ".FPT", ".dbt", ".DBT" })
        {
            var candidate = Path.Combine(directory, stem + extension);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// CP1252, registering the code-page provider first. .NET Core ships only the Unicode encodings
    /// by default, so without this every legacy file would fail on the first accented character.
    /// </summary>
    public static Encoding Cp1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            return Encoding.GetEncoding(1252);
        }
        catch (ArgumentException)
        {
            // A trimmed or unusual runtime without the provider. Latin-1 agrees with CP1252 on
            // everything a stock code contains, so the import still works.
            return Encoding.Latin1;
        }
    }

    /// <summary>CP437, the original DOS code page. Offered for the override the doc calls for.</summary>
    public static Encoding Cp437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            return Encoding.GetEncoding(437);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var got = stream.Read(buffer[read..]);

            if (got == 0)
            {
                return false;
            }

            read += got;
        }

        return true;
    }

    public void Dispose()
    {
        if (_leaveOpen)
        {
            return;
        }

        _table.Dispose();
        _memo?.Dispose();
    }
}
