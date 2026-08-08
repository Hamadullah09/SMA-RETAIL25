using System.Buffers.Binary;
using System.Text;
using Retail25.Application.Migration;
using Retail25.Domain.Common;

namespace Retail25.Infrastructure.LegacyData;

/// <summary>
/// Reads the legacy formats into rows and profiles them (doc 09 §3, analyze + stage).
/// <para>
/// A DBF is recognised by its own header rather than by its extension, so a file renamed on the way
/// off an old machine still reads. Everything else is treated as the positional, headerless CSV the
/// guide documents.
/// </para>
/// </summary>
public sealed class LegacySourceReader : ILegacySourceReader
{
    public static readonly Error CannotRead = new(
        "migration.cannot_read",
        "That file could not be read.");

    /// <summary>Values kept per column when profiling, so a huge file does not become a huge report.</summary>
    private const int SampleSize = 5;

    public IReadOnlyList<LegacySourceKind> Kinds { get; } = LegacyLayouts.All
        .Select(layout => new LegacySourceKind(
            layout.Entity.ToString(),
            layout.Name,
            layout.GuideReference,
            layout.Columns,

            // Any of these can arrive as a DBF, which cannot survive a text field. The browser sends
            // everything base64 rather than deciding per file what is binary.
            RequiresBase64: true))
        .ToList();

    public bool Knows(string entity) => Enum.TryParse<LegacyEntity>(entity, ignoreCase: true, out _);

    public Result<LegacySource> Read(string entity, string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!Enum.TryParse<LegacyEntity>(entity, ignoreCase: true, out var kind))
        {
            return Result.Failure<LegacySource>(MigrationHandlers.UnknownEntity.With("entity", entity));
        }

        var layout = LegacyLayouts.For(kind);

        try
        {
            return LooksLikeDbf(content)
                ? Result.Success(ReadDbf(content, fileName, layout))
                : Result.Success(ReadCsv(content, fileName, layout));
        }
        catch (InvalidDataException exception)
        {
            return Result.Failure<LegacySource>(CannotRead.With("reason", exception.Message));
        }
        catch (IOException exception)
        {
            return Result.Failure<LegacySource>(CannotRead.With("reason", exception.Message));
        }
    }

    /// <summary>
    /// Whether this is a DBF, decided from the header rather than the extension — a file arriving as
    /// <c>INVENTORY.TXT</c> off a twenty-year-old backup should still read correctly.
    /// <para>
    /// The version byte alone is not enough, and assuming it was is a real trap: <c>0x43</c> is a
    /// dBase IV variant and also the letter <c>C</c>, so a CSV whose first row began "Columbia polo"
    /// was being read as a table. The structural checks below are what actually distinguish the two
    /// — a text file will not have a header length that is 33 plus a whole number of 32-byte field
    /// descriptors and still fits inside the file.
    /// </para>
    /// </summary>
    public static bool LooksLikeDbf(byte[] content)
    {
        if (content.Length < 33)
        {
            return false;
        }

        // The versions the guide's files actually use: dBase III+, FoxBASE and Visual FoxPro.
        if (content[0] is not (0x02 or 0x03 or 0x30 or 0x31 or 0x32 or 0x83 or 0x8B or 0x8E or 0xF5))
        {
            return false;
        }

        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(8, 2));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(10, 2));

        if (headerLength < 33 || recordLength < 1 || headerLength > content.Length)
        {
            return false;
        }

        // 32 bytes of file header, then whole 32-byte field descriptors, then the 0x0D terminator.
        return (headerLength - 33) % 32 == 0 && content[headerLength - 1] == 0x0D;
    }

    private static LegacySource ReadDbf(byte[] content, string fileName, LegacyLayout layout)
    {
        using var table = new MemoryStream(content, writable: false);
        using var reader = DbfReader.Create(table, memo: null);

        var columns = reader.Header.Fields.Select(f => f.Name).ToList();
        var rows = new List<SourceRow>();
        var rowNumber = 0;

        foreach (var record in reader.ReadRecords())
        {
            rowNumber++;

            rows.Add(new SourceRow(
                rowNumber,
                record.Fields,
                record.IsDeleted,
                KeyOf(record.Fields, columns, layout)));
        }

        var notes = new List<string>
        {
            $"Table format: {reader.Header.Describe()}.",
            $"Header declares {reader.Header.RecordCount} record(s); {rows.Count} were readable.",
        };

        if (reader.Header.HasMemoFields)
        {
            // Said out loud because a client file's purchase-history memo is exactly the sort of
            // thing someone assumes came across.
            notes.Add(
                "This table has memo columns. Upload the matching .FPT alongside it to bring their contents across; "
                + "without it the memo columns import empty.");
        }

        if (rows.Count < reader.Header.RecordCount)
        {
            notes.Add("The file is shorter than its header says. It may have been truncated in transit.");
        }

        return new LegacySource(
            Profile(fileName, $"DBF — {reader.Header.Describe()}", layout, columns, rows, notes),
            rows);
    }

    private static LegacySource ReadCsv(byte[] content, string fileName, LegacyLayout layout)
    {
        using var stream = new MemoryStream(content, writable: false);

        var raw = LegacyCsvReader.Read(stream, DbfReader.Cp1252());
        var columns = layout.Columns;
        var rows = new List<SourceRow>();
        var notes = new List<string>();

        var widths = raw.Select(r => r.Values.Count).Distinct().OrderBy(w => w).ToList();

        if (widths.Count > 1)
        {
            notes.Add($"Rows have differing column counts ({string.Join(", ", widths)}). Short rows import with blanks.");
        }

        if (widths.Count > 0 && !widths.Contains(layout.ColumnCount))
        {
            var guess = LegacyLayouts.GuessByColumnCount(widths[0]);

            notes.Add(
                $"This file has {widths[0]} column(s) but {layout.Name} has {layout.ColumnCount} ({layout.GuideReference})."
                + (guess is null ? " Check the file type." : $" It looks more like {guess.Name}."));
        }

        foreach (var row in raw)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < columns.Count; index++)
            {
                values[columns[index]] = row[index];
            }

            // Anything past the documented layout is kept rather than dropped, so the analysis shows
            // it and nobody has to wonder where a column went.
            for (var index = columns.Count; index < row.Values.Count; index++)
            {
                values[$"Extra{index - columns.Count + 1}"] = row[index];
            }

            rows.Add(new SourceRow(row.LineNumber, values, IsDeletedInSource: false, KeyOf(values, columns, layout)));
        }

        var allColumns = rows.SelectMany(r => r.Values.Keys).Distinct().ToList();

        return new LegacySource(Profile(fileName, "CSV / .DTA (positional)", layout, allColumns, rows, notes), rows);
    }

    /// <summary>
    /// The legacy key a row claims — the thing that makes it the same row on a re-import. Stock code
    /// for an item, customer number for a client, and so on.
    /// </summary>
    private static string? KeyOf(
        IReadOnlyDictionary<string, string?> values, IReadOnlyList<string> columns, LegacyLayout layout)
    {
        var candidates = layout.Entity switch
        {
            LegacyEntity.Inventory or LegacyEntity.RegisterSales or LegacyEntity.StockCount
                => new[] { "StockCode", "STOCKCODE", "STOCK_NO", "ITEMNO", "SKU" },
            LegacyEntity.Client => ["CustomerNumber", "CUSTNO", "CLIENTNO", "ACCOUNT"],
            LegacyEntity.Supplier => ["SupplierNumber", "SUPPNO", "VENDORNO"],
            LegacyEntity.Invoice => ["InvoiceNumber", "INVNO", "INVOICE"],
            _ => [],
        };

        foreach (var candidate in candidates)
        {
            if (values.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().ToUpperInvariant();
            }
        }

        // Falls back to the first column, which is where a positional export puts its key often
        // enough to be worth trying before giving up.
        return columns.Count > 0 && values.TryGetValue(columns[0], out var first) && !string.IsNullOrWhiteSpace(first)
            ? first.Trim().ToUpperInvariant()
            : null;
    }

    private static AnalysisReport Profile(
        string fileName,
        string format,
        LegacyLayout layout,
        IReadOnlyList<string> columns,
        IReadOnlyList<SourceRow> rows,
        IReadOnlyList<string> notes)
    {
        var profiles = columns.Select(column =>
        {
            var values = rows
                .Select(r => r.Values.GetValueOrDefault(column))
                .ToList();

            var populated = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

            return new ColumnProfile(
                column,
                populated.Count,
                values.Count - populated.Count,
                populated.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                populated.Count == 0 ? null : populated.MinBy(v => v!.Length),
                populated.Count == 0 ? null : populated.MaxBy(v => v!.Length),
                populated.Take(SampleSize).Select(v => v!).ToList());
        }).ToList();

        return new AnalysisReport(
            fileName,
            format,
            layout.Name,
            layout.GuideReference,
            rows.Count,
            rows.Count(r => r.IsDeletedInSource),
            columns.Count,
            profiles,
            notes);
    }

    /// <summary>Exposed so the CLI and the tests can use the same encoding defaults.</summary>
    public static Encoding DefaultEncoding => DbfReader.Cp1252();
}
