using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RetailPlus.Migration.Tests;

/// <summary>A field of a test table: C, N, L, D or M.</summary>
internal sealed record TestField(string Name, char Type, int Length, int Decimals = 0)
{
    public static TestField C(string name, int length) => new(name, 'C', length);
    public static TestField N(string name, int length, int decimals = 0) => new(name, 'N', length, decimals);
    public static TestField L(string name) => new(name, 'L', 1);
    public static TestField D(string name) => new(name, 'D', 8);
    public static TestField M(string name) => new(name, 'M', 10);
}

/// <summary>A test record: field values as text (numbers as they would be typed, dates as yyyyMMdd).</summary>
internal sealed class TestRecord : Dictionary<string, string>
{
    public TestRecord() : base(StringComparer.OrdinalIgnoreCase) { }
    public bool Deleted { get; init; }
}

/// <summary>
/// Writes small dBASE III / FoxPro 2.x tables with an FPT memo file the way Retail Plus does, so importer tests can run
/// on invented data instead of a real store's files. Long character fields use the Clipper/VO decimals-byte length.
/// </summary>
internal static class TestDbfWriter
{
    private const int MemoBlockSize = 64;

    public static void Write(string path, IReadOnlyList<TestField> fields, IEnumerable<TestRecord> records)
    {
        var rows = records.ToList();
        bool hasMemo = fields.Any(f => f.Type == 'M');
        var memo = hasMemo ? new MemoWriter() : null;
        int recordLength = 1 + fields.Sum(f => f.Length);
        int headerLength = 32 + 32 * fields.Count + 1;

        using var stream = File.Create(path);
        var header = new byte[32];
        header[0] = hasMemo ? (byte)0xF5 : (byte)0x03;
        header[1] = 26;
        header[2] = 10;
        header[3] = 24;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)rows.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)recordLength);
        header[29] = 0x03;
        stream.Write(header);

        foreach (var f in fields)
        {
            var descriptor = new byte[32];
            Encoding.ASCII.GetBytes(f.Name).CopyTo(descriptor, 0);
            descriptor[11] = (byte)f.Type;
            if (f.Type == 'C' && f.Length > 255)
            {
                descriptor[16] = (byte)(f.Length % 256);
                descriptor[17] = (byte)(f.Length / 256);
            }
            else
            {
                descriptor[16] = (byte)f.Length;
                descriptor[17] = (byte)f.Decimals;
            }
            stream.Write(descriptor);
        }
        stream.WriteByte(0x0D);

        foreach (var row in rows)
        {
            stream.WriteByte(row.Deleted ? (byte)'*' : (byte)' ');
            foreach (var f in fields)
            {
                row.TryGetValue(f.Name, out var value);
                string text = f.Type switch
                {
                    'N' => (value ?? "").PadLeft(f.Length),
                    'L' => value ?? " ",
                    'D' => value ?? new string(' ', 8),
                    'M' => value is null ? new string(' ', 10) : memo!.Add(value).ToString(CultureInfo.InvariantCulture).PadLeft(10),
                    _ => (value ?? "").PadRight(f.Length),
                };
                if (text.Length != f.Length) throw new ArgumentException($"{f.Name}: value does not fit in {f.Length} characters");
                stream.Write(Encoding.Latin1.GetBytes(text));
            }
        }
        stream.WriteByte(0x1A);
        memo?.Save(Path.ChangeExtension(path, ".FPT"));
    }

    private sealed class MemoWriter
    {
        private readonly List<byte> _blocks = [];
        private int _nextBlock = 512 / MemoBlockSize;

        public int Add(string text)
        {
            var data = Encoding.Latin1.GetBytes(text);
            int block = _nextBlock;
            var head = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(head, 1);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), (uint)data.Length);
            _blocks.AddRange(head);
            _blocks.AddRange(data);
            int used = 8 + data.Length;
            int padded = (used + MemoBlockSize - 1) / MemoBlockSize * MemoBlockSize;
            _blocks.AddRange(new byte[padded - used]);
            _nextBlock += padded / MemoBlockSize;
            return block;
        }

        public void Save(string path)
        {
            var header = new byte[512];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)_nextBlock);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), MemoBlockSize);
            using var stream = File.Create(path);
            stream.Write(header);
            stream.Write(_blocks.ToArray());
        }
    }
}
