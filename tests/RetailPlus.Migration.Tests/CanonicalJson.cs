using System.Globalization;
using System.Text.Json.Nodes;
using RetailPlus.Migration.Dbf;

namespace RetailPlus.Migration.Tests;

/// <summary>
/// Writes what the C# reader sees in a table in the canonical form of tools/dbf/dbf_to_json.py, so the two can be
/// compared node by node.
/// </summary>
internal static class CanonicalJson
{
    public static JsonObject Export(string path)
    {
        using var table = DbfTable.Open(path);
        var h = table.Header;
        var records = new JsonArray();
        foreach (var record in table.ReadRecords())
        {
            var values = new JsonObject();
            foreach (var field in table.Fields)
                values[field.Name] = Value(record, field);
            records.Add(new JsonObject { ["n"] = record.RecordNumber, ["deleted"] = record.IsDeleted, ["values"] = values });
        }

        return new JsonObject
        {
            ["file"] = Path.GetFileName(path),
            ["header"] = new JsonObject
            {
                ["version"] = h.Version,
                ["last_update"] = new JsonArray(h.LastUpdateYear, h.LastUpdateMonth, h.LastUpdateDay),
                ["record_count"] = h.RecordCount,
                ["header_length"] = h.HeaderLength,
                ["record_length"] = h.RecordLength,
                ["table_flags"] = h.TableFlags,
                ["code_page"] = h.CodePage,
            },
            ["fields"] = new JsonArray(table.Fields.Select(f => (JsonNode)new JsonObject
            {
                ["name"] = f.Name, ["type"] = f.Type.ToString(), ["length"] = f.Length, ["decimals"] = f.Decimals,
            }).ToArray()),
            ["memo_block_size"] = table.MemoBlockSize,
            ["records_present"] = table.RecordsPresent,
            ["truncated"] = table.IsTruncated,
            ["records"] = records,
        };
    }

    private static JsonNode? Value(DbfRecord record, DbfField field)
    {
        try
        {
            return field.Type switch
            {
                'C' => record.GetString(field),
                'N' or 'F' => record.GetDecimal(field) is { } d ? Minimal(d) : null,
                'D' => record.GetDate(field)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                'L' => record.GetLogical(field),
                'M' => record.GetMemo(field) is { } m ? new JsonObject { ["type"] = m.Type, ["text"] = m.Text } : null,
                _ => new JsonObject { ["unsupported"] = field.Type.ToString() },
            };
        }
        catch (DbfFormatException ex)
        {
            return new JsonObject { ["invalid"] = ex.Detail };
        }
    }

    /// <summary>Same as the oracle: no trailing zeros, "0" for zero.</summary>
    private static string Minimal(decimal value) =>
        value == 0 ? "0" : value.ToString("0.############################", CultureInfo.InvariantCulture);
}
