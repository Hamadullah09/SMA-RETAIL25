namespace RetailPlus.Migration.Dbf;

/// <summary>A value or structure in a DBF/FPT file that can't be read as its declared type.</summary>
public sealed class DbfFormatException : Exception
{
    public DbfFormatException(string filePath, int recordNumber, string? fieldName, string detail)
        : base(Describe(filePath, recordNumber, fieldName, detail))
    {
        FilePath = filePath;
        RecordNumber = recordNumber;
        FieldName = fieldName;
        Detail = detail;
    }

    public DbfFormatException() : this("", 0, null, "") { }

    public DbfFormatException(string message) : this("", 0, null, message) { }

    public DbfFormatException(string message, Exception innerException) : base(message, innerException)
    {
        FilePath = "";
        Detail = message;
    }

    public string FilePath { get; }
    /// <summary>1-based record number, or 0 for a problem with the file as a whole.</summary>
    public int RecordNumber { get; }
    public string? FieldName { get; }
    /// <summary>
    /// The offending raw text (numbers, dates, logicals) or the reason (memos). Matches the "invalid" value the
    /// Python oracle writes for the same field.
    /// </summary>
    public string Detail { get; }

    private static string Describe(string filePath, int recordNumber, string? fieldName, string detail) =>
        $"{Path.GetFileName(filePath)}" +
        (recordNumber > 0 ? $" record {recordNumber}" : "") +
        (fieldName is null ? "" : $" field {fieldName}") +
        $": {detail}";
}
