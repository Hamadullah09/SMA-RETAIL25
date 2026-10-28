using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RetailPlus.Sync;

/// <summary>Rows as JSON objects keyed by the CLR property names (shadow columns by their names).</summary>
internal static class RowJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>One row as a JSON object, its values in the order of <paramref name="columns"/>.</summary>
    public static void WriteRow(Utf8JsonWriter writer, IReadOnlyList<IProperty> columns, object?[] row)
    {
        writer.WriteStartObject();
        for (int i = 0; i < columns.Count; i++)
        {
            writer.WritePropertyName(columns[i].Name);
            JsonSerializer.Serialize(writer, row[i], columns[i].ClrType, Options);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// The values of one row, converted to the columns' CLR types. Columns the row does not have (a package from an older
    /// program) are left out.
    /// </summary>
    public static Dictionary<string, object?> Read(JsonElement row, IReadOnlyList<IProperty> columns)
    {
        var values = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
        foreach (var column in columns)
            if (row.TryGetProperty(column.Name, out var value))
                values[column.Name] = value.ValueKind == JsonValueKind.Null ? null : value.Deserialize(column.ClrType, Options);
        return values;
    }
}
