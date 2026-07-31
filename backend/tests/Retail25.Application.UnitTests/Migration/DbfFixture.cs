using System.Buffers.Binary;
using System.Text;

namespace Retail25.Application.UnitTests.Migration;

/// <summary>One column of a synthetic table.</summary>
public sealed record FixtureField(string Name, char Type, int Length, int Decimals = 0);

/// <summary>
/// Builds a real dBase III+ table in memory, byte for byte (doc 09 §3).
/// <para>
/// Writing the format rather than checking in a binary blob is deliberate: a fixture whose bytes
/// nobody can read is a fixture nobody can change, and this one doubles as an executable statement
/// of what the reader is expected to cope with. It is also the only honest way to test a reader
/// without a real legacy extract — and the reader being right about the format is exactly what the
/// unavailable extract would otherwise be proving.
/// </para>
/// </summary>
public static class DbfFixture
{
    private const byte DbaseIiiPlus = 0x03;
    private const byte HeaderTerminator = 0x0D;
    private const byte EndOfFile = 0x1A;

    /// <summary>
    /// A table with the given columns and rows. Values are written as the format requires: character
    /// fields left-aligned and space-padded, numerics right-aligned, both truncated to fit.
    /// </summary>
    /// <param name="deletedRows">Zero-based indexes to flag as deleted but leave in the file.</param>
    public static byte[] Build(
        IReadOnlyList<FixtureField> fields,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<int>? deletedRows = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rows);

        var deleted = deletedRows?.ToHashSet() ?? [];
        var encoding = Encoding.Latin1;

        // 32 header + 32 per field + 1 terminator.
        var headerLength = 32 + (fields.Count * 32) + 1;
        var recordLength = 1 + fields.Sum(f => f.Length);

        using var buffer = new MemoryStream();

        var head = new byte[32];
        head[0] = DbaseIiiPlus;
        head[1] = 126;  // 2026
        head[2] = 7;
        head[3] = 31;

        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4, 4), rows.Count);
        BinaryPrimitives.WriteInt16LittleEndian(head.AsSpan(8, 2), (short)headerLength);
        BinaryPrimitives.WriteInt16LittleEndian(head.AsSpan(10, 2), (short)recordLength);

        buffer.Write(head);

        foreach (var field in fields)
        {
            var descriptor = new byte[32];
            var name = encoding.GetBytes(field.Name.ToUpperInvariant());

            Array.Copy(name, descriptor, Math.Min(name.Length, 10));

            descriptor[11] = (byte)field.Type;
            descriptor[16] = (byte)field.Length;
            descriptor[17] = (byte)field.Decimals;

            buffer.Write(descriptor);
        }

        buffer.WriteByte(HeaderTerminator);

        for (var index = 0; index < rows.Count; index++)
        {
            buffer.WriteByte(deleted.Contains(index) ? (byte)'*' : (byte)' ');

            var row = rows[index];

            for (var column = 0; column < fields.Count; column++)
            {
                var field = fields[column];
                var value = column < row.Count ? row[column] : string.Empty;

                var text = value.Length > field.Length
                    ? value[..field.Length]

                    // Numerics right-align, everything else left-aligns. Getting this backwards is
                    // what makes a badly-written fixture pass a badly-written reader.
                    : field.Type is 'N' or 'F'
                        ? value.PadLeft(field.Length)
                        : value.PadRight(field.Length);

                buffer.Write(encoding.GetBytes(text));
            }
        }

        buffer.WriteByte(EndOfFile);

        return buffer.ToArray();
    }

    /// <summary>The eleven-column inventory export as a DBF, in the guide's documented order (p.28).</summary>
    public static byte[] Inventory(IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<int>? deletedRows = null)
        => Build(
            [
                new FixtureField("ITEMNAME", 'C', 30),
                new FixtureField("STOCKCODE", 'C', 15),
                new FixtureField("DEPARTMENT", 'C', 20),
                new FixtureField("CATEGORY", 'C', 20),
                new FixtureField("SIZE", 'C', 10),
                new FixtureField("PACKQTY", 'N', 8, 2),
                new FixtureField("COST", 'N', 10, 3),
                new FixtureField("PRICE", 'N', 10, 2),
                new FixtureField("ONHAND", 'N', 10, 2),
                new FixtureField("SUPPLIER", 'C', 20),
                new FixtureField("REORDERNO", 'C', 15),
            ],
            rows,
            deletedRows);
}
