using System.Buffers.Binary;

namespace RetailPlus.Migration.Dbf;

/// <summary>
/// Read-only access to one DBF table and its FPT memo file, following the rules in docs/DATA_FORMATS.md. Never writes
/// to the files and needs no index files. The source folder is expected to be a copy taken while Retail Plus was
/// closed, but files are opened with sharing so a locked original can still be read.
/// </summary>
public sealed class DbfTable : IDisposable
{
    private readonly FileStream _stream;
    private readonly FptMemoFile? _memo;
    private readonly Dictionary<string, DbfField> _fieldsByName;

    private DbfTable(string path)
    {
        FilePath = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        try
        {
            Header = ReadHeader();
            Fields = ReadFields();
            _fieldsByName = Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
            long available = Header.RecordLength > 0 ? Math.Max(0, (_stream.Length - Header.HeaderLength) / Header.RecordLength) : 0;
            RecordsPresent = (int)Math.Min(Header.RecordCount, available);
            int fieldBytes = Fields.Sum(f => f.Length) + 1;
            if (fieldBytes != Header.RecordLength)
                Warnings.Add($"field lengths add up to {fieldBytes} bytes, but the header says {Header.RecordLength}");
            if (IsTruncated)
                Warnings.Add($"header says {Header.RecordCount} records, but the file only holds {RecordsPresent}");
            MemoFilePath = FindMemoFile(path);
            if (MemoFilePath is not null)
                _memo = new FptMemoFile(MemoFilePath);
            else if (Fields.Any(f => f.Type == 'M'))
                Warnings.Add("table has memo fields but no FPT file");
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public static DbfTable Open(string path) => new(Path.GetFullPath(path));

    public string FilePath { get; }
    public DbfHeader Header { get; }
    public IReadOnlyList<DbfField> Fields { get; }
    /// <summary>Records actually in the file: the header count, unless the file is shorter.</summary>
    public int RecordsPresent { get; }
    public bool IsTruncated => RecordsPresent < Header.RecordCount;
    public string? MemoFilePath { get; }
    public int? MemoBlockSize => _memo?.BlockSize;
    /// <summary>Structural oddities that don't stop reading (for the import report).</summary>
    public List<string> Warnings { get; } = [];

    public DbfField? FindField(string name) => _fieldsByName.GetValueOrDefault(name);

    public DbfField GetField(string name) =>
        FindField(name) ?? throw new DbfFormatException(FilePath, 0, name, "no such field");

    /// <summary>Reads all present records in order, including deleted ones (check <see cref="DbfRecord.IsDeleted"/>).</summary>
    public IEnumerable<DbfRecord> ReadRecords()
    {
        for (int n = 1; n <= RecordsPresent; n++)
            yield return ReadRecord(n);
    }

    public DbfRecord ReadRecord(int recordNumber)
    {
        if (recordNumber < 1 || recordNumber > RecordsPresent)
            throw new ArgumentOutOfRangeException(nameof(recordNumber), recordNumber, $"1..{RecordsPresent}");
        var bytes = new byte[Header.RecordLength];
        _stream.Position = Header.HeaderLength + (long)(recordNumber - 1) * Header.RecordLength;
        _stream.ReadExactly(bytes);
        return new DbfRecord(this, recordNumber, bytes);
    }

    internal (DbfMemo? Memo, string? Error) ReadMemo(long block) =>
        _memo is null ? (null, "no memo file") : _memo.Read(block);

    private DbfHeader ReadHeader()
    {
        Span<byte> h = stackalloc byte[32];
        if (_stream.ReadAtLeast(h, 32, throwOnEndOfStream: false) < 32)
            throw new DbfFormatException(FilePath, 0, null, "file is shorter than a DBF header");
        var header = new DbfHeader(
            Version: h[0],
            LastUpdateYear: h[1],
            LastUpdateMonth: h[2],
            LastUpdateDay: h[3],
            RecordCount: checked((int)BinaryPrimitives.ReadUInt32LittleEndian(h[4..8])),
            HeaderLength: BinaryPrimitives.ReadUInt16LittleEndian(h[8..10]),
            RecordLength: BinaryPrimitives.ReadUInt16LittleEndian(h[10..12]),
            TableFlags: h[28],
            CodePage: h[29]);
        if (header.HeaderLength < 33 || header.HeaderLength > _stream.Length)
            throw new DbfFormatException(FilePath, 0, null, $"implausible header length {header.HeaderLength}");
        return header;
    }

    private List<DbfField> ReadFields()
    {
        var descriptorBytes = new byte[Header.HeaderLength - 32];
        _stream.Position = 32;
        _stream.ReadExactly(descriptorBytes);
        var fields = new List<DbfField>();
        int offset = 1;
        for (int at = 0; at + 32 <= descriptorBytes.Length && descriptorBytes[at] != 0x0D; at += 32)
        {
            var d = descriptorBytes.AsSpan(at, 32);
            int nameEnd = d[..11].IndexOf((byte)0);
            string name = System.Text.Encoding.ASCII.GetString(d[..(nameEnd < 0 ? 11 : nameEnd)]);
            char type = (char)d[11];
            int length = d[16], decimals = d[17];
            if (type == 'C' && decimals > 0)
            {
                length += decimals * 256;   // Clipper/VO long character field
                decimals = 0;
            }
            fields.Add(new DbfField(name, type, length, decimals, offset));
            offset += length;
        }
        return fields;
    }

    private static string? FindMemoFile(string tablePath)
    {
        string directory = Path.GetDirectoryName(tablePath)!;
        string baseName = Path.GetFileNameWithoutExtension(tablePath);
        // Matches POSINV.FPT for POSINV.DBF and PO1001.FPT for PO1001.POS, whatever the case on disk.
        return Directory.EnumerateFiles(directory, baseName + ".*")
            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(Path.GetExtension(f), ".fpt", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _memo?.Dispose();
        _stream.Dispose();
    }
}
