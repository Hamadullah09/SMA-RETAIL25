using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace RetailPlus.Cutover.Source;

/// <summary>
/// One row of a Retail25 table, read by column name (Retail25's snake_case names). A column the table does not have
/// reads as empty, so an older or newer Retail25 schema still maps as far as it goes.
/// </summary>
internal sealed class SourceRow(string table, Dictionary<string, object?> values)
{
    public string Table { get; } = table;

    /// <summary>The row's key: every Retail25 business table has a bigint <c>id</c>.</summary>
    public long Id => Long("id");

    public bool Has(string column) => values.ContainsKey(column);

    public object? Value(string column) => values.TryGetValue(column, out var v) && v is not DBNull ? v : null;

    /// <summary>Text, trimmed; null when empty.</summary>
    public string? Text(string column)
    {
        var text = Value(column) switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var v => v.ToString(),
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public long Long(string column) => LongOrNull(column) ?? 0;

    public long? LongOrNull(string column) => Value(column) is { } v ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;

    public int Int(string column) => Value(column) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

    public decimal Dec(string column) => DecOrNull(column) ?? 0;

    public decimal? DecOrNull(string column) => Value(column) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;

    public bool Bool(string column) => Value(column) is { } v && Convert.ToBoolean(v, CultureInfo.InvariantCulture);

    public DateOnly? Date(string column) => Value(column) switch
    {
        DateOnly d => d,
        DateTime d => DateOnly.FromDateTime(d),
        DateTimeOffset o => DateOnly.FromDateTime(o.Date),
        _ => null,
    };

    /// <summary>A point in time. Retail25 keeps them as datetimeoffset in UTC; a plain datetime is read as UTC.</summary>
    public DateTimeOffset? Instant(string column) => Value(column) switch
    {
        DateTimeOffset o => o,
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        _ => null,
    };

    public TimeSpan? TimeOfDay(string column) => Value(column) switch
    {
        TimeSpan t => t,
        TimeOnly t => t.ToTimeSpan(),
        _ => null,
    };

    public byte[]? Bytes(string column) => Value(column) as byte[];

    /// <summary>The row as JSON (binary columns and the named secret columns left out), for the import trace.</summary>
    public string ToJson(params string[] exclude)
    {
        var shown = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (value is byte[] || Array.IndexOf(exclude, name) >= 0) continue;
            shown[name] = value is DBNull ? null : value;
        }
        return JsonSerializer.Serialize(shown);
    }
}

/// <summary>The rows of one Retail25 table, with lookups by id.</summary>
internal sealed class SourceTable(string name, bool exists, List<SourceRow> rows)
{
    private Dictionary<long, SourceRow>? _byId;

    public string Name { get; } = name;

    /// <summary>False when the source database has no such table (it then reads as empty).</summary>
    public bool Exists { get; } = exists;

    public IReadOnlyList<SourceRow> Rows { get; } = rows;

    public int Count => Rows.Count;

    public SourceRow? Find(long? id)
    {
        if (id is null) return null;
        _byId ??= Rows.Where(r => r.Has("id")).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        return _byId.GetValueOrDefault(id.Value);
    }
}

/// <summary>
/// The Retail25 database, read with plain ADO.NET and never written: the tables the cutover maps, plus the row count of
/// every table so that what is not carried over can be listed.
/// </summary>
internal sealed class Retail25Source
{
    private readonly Dictionary<string, SourceTable> _tables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Server and database, for the report.</summary>
    public string Description { get; private set; } = "";

    /// <summary>Rows in every table of the dbo schema, by table name.</summary>
    public IReadOnlyDictionary<string, long> RowCounts { get; private set; } = new Dictionary<string, long>();

    public SourceTable this[string table] => _tables.TryGetValue(table, out var t) ? t : new SourceTable(table, false, []);

    public static async Task<Retail25Source> ReadAsync(string connectionString, IEnumerable<string> tables, Action<string>? progress, CancellationToken ct)
    {
        // Read only: say so to the server too (it matters on an availability group, and documents the intent elsewhere).
        var builder = new SqlConnectionStringBuilder(connectionString) { ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        var source = new Retail25Source { Description = $"{connection.DataSource}/{connection.Database}" };

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT t.name, SUM(p.rows) FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id AND s.name = 'dbo'
                JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
                GROUP BY t.name
                """;
            command.CommandTimeout = 600;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) counts[reader.GetString(0)] = reader.GetInt64(1);
        }
        source.RowCounts = counts;
        if (!counts.ContainsKey("products") || !counts.ContainsKey("locations"))
            throw new CutoverRefusedException($"{source.Description} does not look like a Retail25 database (no products or locations table).");

        foreach (var table in tables.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!counts.ContainsKey(table))
            {
                source._tables[table] = new SourceTable(table, false, []);
                continue;
            }
            progress?.Invoke($"Reading {table}");
            var rows = new List<SourceRow>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM [dbo].[{table.Replace("]", "]]", StringComparison.Ordinal)}]";
            command.CommandTimeout = 600;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++) values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(new SourceRow(table, values));
            }
            source._tables[table] = new SourceTable(table, true, rows);
        }
        return source;
    }
}
