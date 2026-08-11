using System.Globalization;
using Retail25.Domain.Catalog;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Rfid.Import;

/// <summary>One physical tag, as one row of the export describes it.</summary>
public sealed record EpcCatalogRow(
    int LineNumber,
    string Epc,
    string StockCode,
    string ProductName,
    ProductType Type,
    decimal RegularPrice,
    SerializedUnitState State,
    DateTimeOffset? ReceivedOn);

/// <summary>
/// Something the file said that the importer could not take at face value.
/// <para>
/// <paramref name="RowDropped"/> is the part that matters operationally: a mangled state column
/// costs a default, a mangled EPC costs the tag. Both are reported, and the caller can tell them
/// apart without reading the message.
/// </para>
/// </summary>
public sealed record EpcCatalogProblem(int LineNumber, string Value, string Reason, string Message, bool RowDropped);

public sealed record EpcCatalogParse(
    IReadOnlyList<EpcCatalogRow> Rows,
    IReadOnlyList<EpcCatalogProblem> Problems,
    int DataRows);

/// <summary>
/// Reads the tag export: one row per physical tag, with the tag's own columns and the columns of
/// the product it hangs on flattened side by side.
/// <para>
/// The shape is awkward in three specific ways, and all three are load-bearing here.
/// </para>
/// <para>
/// <b>The header repeats itself.</b> Both halves carry <c>id</c>, <c>location_id</c>,
/// <c>created_at</c>, <c>created_by</c>, <c>modified_at</c> and <c>modified_by</c>, so a column
/// name only means something inside one half. The second <c>id</c> is the seam: everything before
/// it belongs to the tag, everything from it belongs to the product. Resolving each column within
/// its own half is what keeps that true as the export gains or loses columns — the alternative,
/// fixed positions, breaks the moment somebody deletes one of the columns the annotation row calls
/// a repetition.
/// </para>
/// <para>
/// <b>The second row is prose.</b> It carries the annotations somebody wrote on the export —
/// "CHANGE TO PRODUCT NAME", "JUST DATE NO TIME", "REPITATION" — and reads as a data row to
/// anything that only skips one header line. It has no id, which is what identifies it.
/// </para>
/// <para>
/// <b>Column B was overwritten by hand.</b> It is headed <c>product_id</c> but the annotation asks
/// for the product name, and about a third of the file has been edited that way; the rest still
/// holds the old system's GUIDs. So: use it when it is not a GUID, fall back to the product half's
/// <c>name</c> when it is. That is not a guess — it is the two states the file is actually in.
/// </para>
/// </summary>
public static class EpcCatalogCsv
{
    /// <summary>
    /// The product a tag hangs on is identified by stock code, because that is the only column in
    /// this file that means anything in a fresh database — the id columns are the old system's
    /// GUIDs and the location column points at a location that no longer exists.
    /// </summary>
    public static EpcCatalogParse Parse(string text)
    {
        var rows = new List<EpcCatalogRow>();
        var problems = new List<EpcCatalogProblem>();

        var records = ReadRecords(text);
        if (records.Count == 0)
        {
            return new EpcCatalogParse(rows, problems, 0);
        }

        var header = Columns.From(records[0].Fields);
        if (header.Epc < 0)
        {
            problems.Add(new EpcCatalogProblem(
                records[0].LineNumber,
                string.Empty,
                "header.no_epc_column",
                "The file has no EPC column, so there is nothing here to import.",
                RowDropped: true));

            return new EpcCatalogParse(rows, problems, 0);
        }

        var dataRows = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records.Skip(1))
        {
            var fields = record.Fields;

            if (fields.Count == 0)
            {
                continue;
            }

            // The annotation row, and any trailing blank line. Neither carries an id, and neither is
            // an error worth reporting — the file is simply built that way.
            //
            // Only where there is an id column to test, though. Requiring an integer in column one
            // unconditionally meant a file that simply lists a tag and a stock code — the shape the
            // import screen describes, and the shape anybody hand-builds — had every row silently
            // dropped and came back "the file held no rows this importer could use". Where no id
            // column exists, a row is data if it says which tag it is about.
            var skip = header.HasLeadingId
                ? !int.TryParse(Field(fields, 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                : Field(fields, header.Epc).Trim().Length == 0;

            if (skip)
            {
                continue;
            }

            dataRows++;

            var rawEpc = Field(fields, header.Epc);

            // Half the file writes the EPC as space-separated hex pairs, the way a reader's console
            // prints it. Same tag, different transcription.
            var epc = new string(rawEpc.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

            var validated = Epc.Create(epc);
            if (validated.IsFailure)
            {
                problems.Add(new EpcCatalogProblem(
                    record.LineNumber,
                    rawEpc,
                    validated.Error.Code,
                    validated.Error.Message,
                    RowDropped: true));

                continue;
            }

            epc = validated.Value.Value;

            if (!seen.Add(epc))
            {
                problems.Add(new EpcCatalogProblem(
                    record.LineNumber,
                    epc,
                    "epc.duplicate_in_file",
                    "This tag appears earlier in the same file. The first occurrence was used.",
                    RowDropped: true));

                continue;
            }

            var stockCode = Field(fields, header.StockCode).Trim();
            if (stockCode.Length == 0)
            {
                problems.Add(new EpcCatalogProblem(
                    record.LineNumber,
                    epc,
                    "row.no_stock_code",
                    "The row has no stock code, so there is no item to hang the tag on.",
                    RowDropped: true));

                continue;
            }

            var name = ProductName(fields, header);
            if (name.Length == 0)
            {
                name = stockCode;
            }

            var state = ReadState(fields, header, record.LineNumber, problems);

            rows.Add(new EpcCatalogRow(
                record.LineNumber,
                epc,
                Truncate(stockCode, 30),
                Truncate(name, 200),
                ReadType(fields, header),
                ReadDecimal(fields, header.RegularPrice),
                state,
                ReadTimestamp(fields, header.ReceivedOn)));
        }

        return new EpcCatalogParse(rows, problems, dataRows);
    }

    private static string ProductName(IReadOnlyList<string> fields, Columns header)
    {
        var overwritten = Field(fields, header.ProductNameOverride).Trim();

        if (overwritten.Length > 0 && !Guid.TryParse(overwritten, out _))
        {
            return overwritten;
        }

        return Field(fields, header.Name).Trim();
    }

    /// <summary>
    /// The export writes the state as a name; the annotation asks for a 0/1 flag. Both are read,
    /// because a file edited to follow the annotation must not stop importing.
    /// </summary>
    private static SerializedUnitState ReadState(
        IReadOnlyList<string> fields,
        Columns header,
        int lineNumber,
        List<EpcCatalogProblem> problems)
    {
        var raw = Field(fields, header.State).Trim();

        if (raw.Length == 0 || raw == "0")
        {
            return SerializedUnitState.InStock;
        }

        if (raw == "1")
        {
            return SerializedUnitState.Sold;
        }

        if (Enum.TryParse<SerializedUnitState>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        // Not fatal: the tag is still a tag. Recorded so the count of "in stock" afterwards can be
        // explained rather than merely trusted.
        problems.Add(new EpcCatalogProblem(
            lineNumber,
            raw,
            "state.unrecognised",
            "The state column was not a state this system knows. The tag was taken as in stock.",
            RowDropped: false));

        return SerializedUnitState.InStock;
    }

    /// <summary>
    /// The type column already holds this system's <see cref="ProductType"/> values. Where it is
    /// blank the answer is still <see cref="ProductType.Serialized"/> — every row in this file has
    /// an EPC, and an EPC is one physical unit.
    /// </summary>
    private static ProductType ReadType(IReadOnlyList<string> fields, Columns header)
    {
        var raw = Field(fields, header.Type).Trim();

        if (raw.Length > 0
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            && Enum.IsDefined(typeof(ProductType), numeric))
        {
            return (ProductType)numeric;
        }

        return Enum.TryParse<ProductType>(raw, ignoreCase: true, out var named)
            ? named
            : ProductType.Serialized;
    }

    private static decimal ReadDecimal(IReadOnlyList<string> fields, int index)
        => decimal.TryParse(Field(fields, index), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    /// <summary>
    /// Returned in UTC, always.
    /// <para>
    /// The file writes its timestamps in the exporting machine's local offset — <c>+0500</c> — and
    /// The instant is the same either way; only its spelling changes. Npgsql rejected a non-zero
    /// offset outright for <c>timestamptz</c> and SQL Server accepts one, so this is now about
    /// storing what the rest of the system stores rather than about being allowed to store it. The
    /// unchanged; only its spelling is. Left alone, every row in this file throws on save.
    /// </para>
    /// </summary>
    private static DateTimeOffset? ReadTimestamp(IReadOnlyList<string> fields, int index)
        => DateTimeOffset.TryParse(
            Field(fields, index),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var value)
            ? value.ToUniversalTime()
            : null;

    private static string Field(IReadOnlyList<string> fields, int index)
        => index >= 0 && index < fields.Count ? fields[index] : string.Empty;

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value[..length];

    /// <summary>Where each column the importer needs actually sits, once the join is split.</summary>
    private readonly record struct Columns(
        int ProductNameOverride,
        int Epc,
        int State,
        int ReceivedOn,
        int StockCode,
        int Name,
        int Type,
        int RegularPrice,
        bool HasLeadingId)
    {
        public static Columns From(IReadOnlyList<string> header)
        {
            var names = header.Select(Normalise).ToList();

            // The seam. Without a second `id` the file is not the joined export.
            var seam = SecondIndexOf(names, "id");
            var tagHalf = seam < 0 ? names.Count : seam;

            // Where there is no seam there are no repeated names either, so there is no half a
            // column could be misread as belonging to — the whole row is searched for the product's
            // columns instead of a range that is empty by construction. Restricting them to the
            // product half regardless meant a two-column file could never find its stock code, and
            // every row was rejected as having none.
            var productFrom = seam < 0 ? 0 : tagHalf;

            return new Columns(
                ProductNameOverride: Find(names, 0, tagHalf, "product_id"),
                Epc: Find(names, 0, tagHalf, "epc"),
                State: Find(names, 0, tagHalf, "state"),
                ReceivedOn: Find(names, 0, tagHalf, "received_on"),
                StockCode: Find(names, productFrom, names.Count, "stock_code"),
                Name: Find(names, productFrom, names.Count, "name"),
                Type: Find(names, productFrom, names.Count, "type"),
                RegularPrice: Find(names, productFrom, names.Count, "regular_price"),
                HasLeadingId: names.Count > 0 && names[0] == "id");
        }

        /// <summary>
        /// Prefix matching, because two headers carry an inline note: <c>product_id PRODUCT NAME</c>
        /// and <c>state FLAG</c>. The note is what the column is for; the prefix is what it is.
        /// </summary>
        private static int Find(IReadOnlyList<string> names, int from, int to, string prefix)
        {
            for (var i = from; i < to; i++)
            {
                if (names[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int SecondIndexOf(IReadOnlyList<string> names, string name)
        {
            var found = 0;
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i] == name && ++found == 2)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Normalise(string header)
        {
            var collapsed = new System.Text.StringBuilder(header.Length);
            var lastWasSpace = false;

            foreach (var c in header.Trim().ToLowerInvariant())
            {
                // To an underscore, not a space. The columns are looked up by their snake_case names,
                // so a header written the way a person writes one — "Stock Code" — matched nothing
                // and the column came back missing. A spreadsheet exports the same column under both
                // spellings depending on who made the file, and they mean the same thing.
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && collapsed.Length > 0)
                    {
                        collapsed.Append('_');
                    }

                    lastWasSpace = true;
                    continue;
                }

                lastWasSpace = false;
                collapsed.Append(c);
            }

            return collapsed.ToString();
        }
    }

    private readonly record struct Record(int LineNumber, IReadOnlyList<string> Fields);

    /// <summary>
    /// RFC 4180: quoted fields, doubled quotes inside them, and newlines inside quotes. The
    /// annotation row needs the first two — <c>"ALREADY APPEARS IN COLUMN ""G"""</c> splits into
    /// three fields under a naive comma split, which shifts every column after it.
    /// </summary>
    private static List<Record> ReadRecords(string text)
    {
        var records = new List<Record>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();

        var quoted = false;
        var lineNumber = 1;
        var recordLine = 1;
        var started = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c != '"')
                {
                    if (c == '\n')
                    {
                        lineNumber++;
                    }

                    field.Append(c);
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    started = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    started = true;
                    break;

                case '\r':
                    break;

                case '\n':
                    if (started || field.Length > 0 || fields.Count > 0)
                    {
                        fields.Add(field.ToString());
                        records.Add(new Record(recordLine, fields));
                        fields = [];
                    }

                    field.Clear();
                    started = false;
                    lineNumber++;
                    recordLine = lineNumber;
                    break;

                default:
                    field.Append(c);
                    started = true;
                    break;
            }
        }

        if (started || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new Record(recordLine, fields));
        }

        return records;
    }
}
