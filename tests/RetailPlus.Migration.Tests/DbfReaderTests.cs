using RetailPlus.Migration.Dbf;

namespace RetailPlus.Migration.Tests;

/// <summary>Specific reader behaviour, on top of the oracle comparison.</summary>
public sealed class DbfReaderTests
{
    private static string Fixture(string name) => Path.Combine(TestPaths.FixtureFolder, name);

    [Fact]
    public void Long_character_fields_use_the_decimals_byte_like_Clipper_and_VO()
    {
        using var table = DbfTable.Open(Fixture("memo64.dbf"));
        var weekly = table.GetField("WEEKLY");
        Assert.Equal(600, weekly.Length);
        Assert.Equal(0, weekly.Decimals);
        Assert.Equal(54, table.ReadRecord(1).GetTrimmed("WEEKLY").Split(',').Length);
        Assert.Empty(table.Warnings);
    }

    [Fact]
    public void Typed_getters_return_values_and_null_for_blanks()
    {
        using var table = DbfTable.Open(Fixture("scalars.dbf"));
        var first = table.ReadRecord(1);
        Assert.Equal(12.50m, first.GetDecimal("PRICE"));
        Assert.Equal(0.125m, first.GetDecimal("RATE"));
        Assert.Equal(new DateOnly(2026, 10, 24), first.GetDate("SOLD"));
        Assert.True(first.GetLogical("TAXABLE"));
        Assert.Equal("Plain item", first.GetTrimmed("NAME"));

        var blank = table.ReadRecord(3);
        Assert.Null(blank.GetDecimal("PRICE"));
        Assert.Null(blank.GetDate("SOLD"));
        Assert.Null(blank.GetLogical("TAXABLE"));
        Assert.Equal("", blank.GetTrimmed("NAME"));
    }

    [Fact]
    public void Field_names_are_found_regardless_of_case()
    {
        using var table = DbfTable.Open(Fixture("scalars.dbf"));
        Assert.Equal(-4.05m, table.ReadRecord(2).GetDecimal("price"));
    }

    [Fact]
    public void Bad_values_raise_errors_that_name_file_record_and_field()
    {
        using var table = DbfTable.Open(Fixture("scalars.dbf"));
        var record = table.ReadRecord(5);
        var ex = Assert.Throws<DbfFormatException>(() => record.GetDate("SOLD"));
        Assert.Equal(5, ex.RecordNumber);
        Assert.Equal("SOLD", ex.FieldName);
        Assert.Equal("20261340", ex.Detail);
        Assert.Contains("scalars.dbf record 5 field SOLD", ex.Message, StringComparison.Ordinal);
        Assert.Throws<DbfFormatException>(() => record.GetDecimal("PRICE"));
        Assert.Throws<DbfFormatException>(() => record.GetLogical("TAXABLE"));
    }

    [Fact]
    public void Text_is_CP1252_and_undefined_bytes_survive_as_C1_characters()
    {
        using var table = DbfTable.Open(Fixture("scalars.dbf"));
        Assert.Equal("Café €“q”", table.ReadRecord(2).GetTrimmed("NAME"));
        Assert.Equal("Und\u0081ef\u009d", table.ReadRecord(6).GetTrimmed("NAME"));
        Assert.Equal("Nul\0inside", table.ReadRecord(8).GetTrimmed("NAME"));
    }

    [Fact]
    public void Deleted_records_are_returned_and_marked()
    {
        using var table = DbfTable.Open(Fixture("scalars.dbf"));
        var deleted = table.ReadRecords().Where(r => r.IsDeleted).Select(r => r.RecordNumber).ToList();
        Assert.Equal([7], deleted);
    }

    [Fact]
    public void Memos_are_read_with_either_block_size()
    {
        using (var table = DbfTable.Open(Fixture("memo64.dbf")))
        {
            Assert.Equal(64, table.MemoBlockSize);
            Assert.Equal("Soft cotton shirt.\r\nSecond line.\r\n", table.ReadRecord(1).GetMemoText("NOTES"));
            Assert.Equal(352, table.ReadRecord(2).GetMemoText("NOTES")!.Length);   // spans several blocks
            Assert.Null(table.ReadRecord(2).GetMemo("MATRIX"));
            Assert.Equal("", table.ReadRecord(6).GetMemoText("NOTES"));
        }
        using (var table = DbfTable.Open(Fixture("memo32.dbf")))
        {
            Assert.Equal(32, table.MemoBlockSize);
            Assert.StartsWith("|ACME OUTDOOR SUPPLY,AX100", table.ReadRecord(1).GetMemoText("SUPLIST"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Broken_memo_pointers_and_missing_memo_files_are_reported()
    {
        using (var table = DbfTable.Open(Fixture("memo64.dbf")))
        {
            var record = table.ReadRecord(4);
            Assert.Equal("block 9999 past end of memo file", Assert.Throws<DbfFormatException>(() => record.GetMemo("NOTES")).Detail);
            Assert.Equal("bad block pointer 12x", Assert.Throws<DbfFormatException>(() => record.GetMemo("MATRIX")).Detail);
        }
        using (var table = DbfTable.Open(Fixture("nofpt.dbf")))
        {
            Assert.Null(table.MemoFilePath);
            Assert.Contains("table has memo fields but no FPT file", table.Warnings);
            Assert.Equal("no memo file", Assert.Throws<DbfFormatException>(() => table.ReadRecord(1).GetMemo("NOTES")).Detail);
            Assert.Null(table.ReadRecord(2).GetMemo("NOTES"));
        }
    }

    [Fact]
    public void A_truncated_file_yields_its_complete_records_and_a_warning()
    {
        using var table = DbfTable.Open(Fixture("truncated.dbf"));
        Assert.Equal(5, table.Header.RecordCount);
        Assert.Equal(2, table.RecordsPresent);
        Assert.True(table.IsTruncated);
        Assert.Equal(2, table.ReadRecords().Count());
        Assert.Contains(table.Warnings, w => w.Contains("only holds 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Purchase_order_files_find_their_memo_file_despite_the_location_extension()
    {
        // Retail Plus names a PO table PO1001.POS with PO1001.FPT beside it.
        var folder = Directory.CreateTempSubdirectory("rp-po-");
        try
        {
            File.Copy(Fixture("memo64.dbf"), Path.Combine(folder.FullName, "PO1001.POS"));
            File.Copy(Fixture("memo64.fpt"), Path.Combine(folder.FullName, "po1001.fpt"));
            using var table = DbfTable.Open(Path.Combine(folder.FullName, "PO1001.POS"));
            Assert.NotNull(table.MemoFilePath);
            Assert.StartsWith("Colour,Size,Fit", table.ReadRecord(1).GetMemoText("MATRIX"), StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Header_fields_are_exposed_raw()
    {
        using var table = DbfTable.Open(Fixture("memo32.dbf"));
        Assert.Equal(0xF5, table.Header.Version);
        Assert.Equal((4, 10, 30), (table.Header.LastUpdateYear, table.Header.LastUpdateMonth, table.Header.LastUpdateDay));
        Assert.Equal(0x03, table.Header.CodePage);
        using var empty = DbfTable.Open(Fixture("empty.dbf"));
        Assert.Equal(0, empty.RecordsPresent);
        Assert.Empty(empty.ReadRecords());
    }
}
