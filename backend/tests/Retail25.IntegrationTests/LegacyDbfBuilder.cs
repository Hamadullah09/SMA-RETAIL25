using System.Globalization;
using System.Text;

namespace Retail25.IntegrationTests;

/// <summary>
/// Writes dBase III+ files, so the migration pipeline can be fed something shaped like a real
/// Retail Plus 2.5 export rather than a CSV pretending to be one.
/// <para>
/// The reader this exercises was hand-written against the format specification; a fixture generated
/// by the same understanding would agree with it by construction and prove nothing. So this writes
/// the bytes independently from the published layout — header, field descriptors, the <c>0x0D</c>
/// terminator, fixed-width space-padded records, the deletion flag — and any disagreement between
/// the two is a real disagreement.
/// </para>
/// </summary>
internal sealed class LegacyDbfBuilder
{
    private const byte DbaseIiiWithoutMemo = 0x03;
    private const byte HeaderTerminator = 0x0D;
    private const byte FieldDescriptorLength = 32;

    /// <summary>
    /// Code page 437 — what a DOS-era till actually wrote.
    /// <para>
    /// The provider has to be registered first. .NET Core ships only a handful of encodings in the
    /// box, so <c>GetEncoding(437)</c> without this returns null, the fallback to ASCII kicks in, and
    /// the fixture writes <c>Caf?</c> — then the accent test fails against a perfectly correct
    /// reader. The bug was in the fixture, which is exactly the trap of generating test data.
    /// </para>
    /// </summary>
    private static readonly Encoding Cp437 = RegisterAndGetCp437();

    private static Encoding RegisterAndGetCp437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437);
    }

    private readonly List<(string Name, char Type, byte Width, byte Decimals)> _fields = [];
    private readonly List<(bool Deleted, string?[] Values)> _rows = [];

    public LegacyDbfBuilder Character(string name, byte width)
    {
        _fields.Add((name, 'C', width, 0));
        return this;
    }

    public LegacyDbfBuilder Numeric(string name, byte width, byte decimals)
    {
        _fields.Add((name, 'N', width, decimals));
        return this;
    }

    public LegacyDbfBuilder Row(params string?[] values)
    {
        _rows.Add((false, values));
        return this;
    }

    /// <summary>
    /// A row flagged deleted rather than removed — which is how a DOS-era system deletes.
    /// <para>
    /// These are the rows a naive reader silently imports. A shop that deleted a product in 1998
    /// does not expect it back, and finding out at the first stock count is a bad way to learn the
    /// importer ignored the flag.
    /// </para>
    /// </summary>
    public LegacyDbfBuilder DeletedRow(params string?[] values)
    {
        _rows.Add((true, values));
        return this;
    }

    public byte[] Build()
    {
        var recordLength = 1 + _fields.Sum(f => f.Width);          // deletion flag + fields
        var headerLength = 32 + (_fields.Count * FieldDescriptorLength) + 1;

        var buffer = new byte[headerLength + (_rows.Count * recordLength) + 1];
        var span = buffer.AsSpan();

        // ---- header ----------------------------------------------------------------------
        span[0] = DbaseIiiWithoutMemo;

        // Last update, as YY MM DD. Fixed rather than "now": a fixture whose bytes change daily
        // cannot be compared against a recorded hash when something goes wrong.
        span[1] = 99;   // 1999
        span[2] = 12;
        span[3] = 31;

        BitConverter.TryWriteBytes(span[4..8], _rows.Count);
        BitConverter.TryWriteBytes(span[8..10], (ushort)headerLength);
        BitConverter.TryWriteBytes(span[10..12], (ushort)recordLength);

        // The language driver, byte 29: 0x01 is "DOS USA, code page 437".
        //
        // Without it the file says nothing about its own encoding, and a reader has to guess —
        // reasonably, at CP1252, where the CP437 byte for "é" (0x82) decodes to a low quotation
        // mark instead. The accent test then fails against a reader doing the only sensible thing
        // with the information it was given. A real DOS-era file carries this byte; the fixture
        // omitting it was the fixture's bug.
        span[29] = 0x01;

        var offset = 32;

        foreach (var (name, type, width, decimals) in _fields)
        {
            // Field names are 11 bytes, null-terminated, upper case — and truncated, not rejected,
            // which is why legacy column names are so often eight cryptic characters.
            var nameBytes = Cp437.GetBytes(name.ToUpperInvariant());
            nameBytes.AsSpan(0, Math.Min(10, nameBytes.Length)).CopyTo(span[offset..]);

            span[offset + 11] = (byte)type;
            span[offset + 16] = width;
            span[offset + 17] = decimals;

            offset += FieldDescriptorLength;
        }

        span[offset++] = HeaderTerminator;

        // ---- records ---------------------------------------------------------------------
        foreach (var (deleted, values) in _rows)
        {
            span[offset++] = deleted ? (byte)'*' : (byte)' ';

            for (var i = 0; i < _fields.Count; i++)
            {
                var (_, type, width, _) = _fields[i];
                var raw = i < values.Length ? values[i] ?? string.Empty : string.Empty;

                var encoded = Cp437.GetBytes(raw);
                var field = span.Slice(offset, width);
                field.Fill((byte)' ');

                if (type == 'N')
                {
                    // Numerics are right-aligned in their field. Left-aligning them is a classic
                    // way to write a file that opens fine and totals wrongly.
                    var take = Math.Min(width, encoded.Length);
                    encoded.AsSpan(0, take).CopyTo(field[(width - take)..]);
                }
                else
                {
                    encoded.AsSpan(0, Math.Min(width, encoded.Length)).CopyTo(field);
                }

                offset += width;
            }
        }

        span[offset] = 0x1A;   // end-of-file marker
        return buffer;
    }

    public string ToBase64() => Convert.ToBase64String(Build());

    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A short token, fresh per call, mixed into every generated key.
    /// <para>
    /// Stock codes and customer numbers are unique in the database. Without this a second run of the
    /// suite collides on <c>LEG000001</c> and the import fails on a constraint violation that has
    /// nothing to do with the code under test. It matters because the suite shares a database
    /// whenever the role cannot <c>CREATEDB</c> — and for the same reason the token is handed back,
    /// so assertions can count <em>this</em> run's rows rather than every run's.
    /// </para>
    /// </summary>
    private static string RunToken() => Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

    /// <summary>
    /// An inventory export at the scale and squalor of a real shop's thirty-year-old file.
    /// </summary>
    /// <param name="rows">How many live products to write.</param>
    /// <returns>The file, this run's key token, and the control totals a legacy system would report.</returns>
    public static (byte[] File, string Token, int LiveCount, decimal InventoryValue) Inventory(int rows)
    {
        var token = RunToken();

        var builder = new LegacyDbfBuilder()
            .Character("ItemName", 40)
            .Character("StockCode", 20)
            .Character("Department", 20)
            .Character("Category", 20)
            .Character("Size", 10)
            .Numeric("PackQuantity", 8, 2)
            .Numeric("Cost", 12, 2)
            .Numeric("Price", 12, 2)
            .Numeric("OnHand", 12, 2)
            .Character("Supplier", 30)
            .Character("ReorderNumber", 20);

        // Fixed seed: a fixture that differs between runs turns a failure into a mystery.
        var random = new Random(1999);
        var value = 0m;

        for (var i = 1; i <= rows; i++)
        {
            var cost = Math.Round((decimal)(random.NextDouble() * 40 + 0.5), 2);
            var onHand = random.Next(0, 200);

            var name = (i % 37) switch
            {
                // Accented and box-drawing characters, because CP437 is not ASCII and a reader that
                // assumes it is produces mojibake that reaches a shelf label.
                0 => $"Café crème {i}",
                7 => $"Jalapeño relish {i}",
                _ => $"Legacy item {i}",
            };

            builder.Row(
                name,
                $"LEG{token}{i:D6}",
                $"DEPT{i % 8:D2}",
                $"CAT{i % 17:D2}",
                (i % 5) == 0 ? "LARGE" : null,                       // blank fields are normal
                "1.00",
                cost.ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(cost * 1.6m, 2).ToString("F2", CultureInfo.InvariantCulture),
                onHand.ToString(CultureInfo.InvariantCulture),
                $"SUP{i % 11:D3}",
                (i % 3) == 0 ? $"RO-{i}" : null);

            value += cost * onHand;
        }

        // One deleted row per fifty live ones. These must not be imported, and must not be counted.
        for (var i = 1; i <= Math.Max(1, rows / 50); i++)
        {
            builder.DeletedRow(
                $"Discontinued {i}", $"DEAD{token}{i:D6}", "DEPT00", "CAT00", null,
                "1.00", "9.99", "19.99", "5", "SUP000", null);
        }

        return (builder.Build(), token, rows, decimal.Round(value, 2));
    }

    /// <summary>A client export, with the duplicates and blanks a thirty-year-old list accumulates.</summary>
    public static (byte[] File, string Token, int LiveCount) Clients(int rows)
    {
        var token = RunToken();

        // Customer numbers are numeric in the target schema, so the alphanumeric run token that
        // keeps stock codes unique cannot be used here — it simply fails to parse, every row lands
        // on the same fallback number, and the import dies on a unique-index violation. Uniqueness
        // comes from a random block of the number space instead.
        var block = Random.Shared.Next(100, 9_000) * 100_000L;

        var builder = new LegacyDbfBuilder()
            .Character("CustomerNumber", 12)
            .Character("FirstName", 25)
            .Character("LastName", 25)
            .Character("Company", 40)
            .Character("Address1", 40)
            .Character("Address2", 40)
            .Character("City", 25)
            .Character("Province", 10)
            .Character("PostalCode", 12)
            .Character("Phone", 20)
            .Character("Fax", 20)
            .Character("Email", 40)
            .Character("ClientType", 10)
            .Numeric("CreditLimit", 12, 2);

        for (var i = 1; i <= rows; i++)
        {
            builder.Row(
                (block + i).ToString(CultureInfo.InvariantCulture),
                (i % 11) == 0 ? null : $"First{i}",                  // a company with no contact name
                $"Last{i}",
                (i % 4) == 0 ? $"Company {i} Ltd" : null,
                $"{i} Legacy Street",
                null,
                "Oldtown",
                "ON",
                $"K{i % 10}A {i % 9}B{i % 8}",
                $"555-{i:D4}",
                null,
                (i % 6) == 0 ? null : $"client{i}@example.test",
                (i % 3) == 0 ? "TRADE" : "RETAIL",
                ((i % 7) * 500).ToString(CultureInfo.InvariantCulture));
        }

        return (builder.Build(), token, rows);
    }
}
