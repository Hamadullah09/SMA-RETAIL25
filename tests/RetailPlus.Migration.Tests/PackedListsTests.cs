using RetailPlus.Migration.Codecs;
using RetailPlus.Migration.Import;

namespace RetailPlus.Migration.Tests;

/// <summary>The packed-list readers on invented values shaped like docs/DATA_FORMATS.md section 4.</summary>
public class PackedListsTests
{
    private readonly List<string> _warnings = [];

    [Fact]
    public void Matrix_reads_headings_rows_and_stock()
    {
        var m = PackedLists.ParseMatrix("Colour,Size,Fit,|Red,S,Slim,3.00|Blue,M,Wide,4.50", _warnings);
        Assert.Equal(["Colour", "Size", "Fit"], m.Headings);
        Assert.Equal(2, m.Rows.Count);
        Assert.Equal(new MatrixRow("Blue", "M", "Wide", 4.50m), m.Rows[1]);
        Assert.False(m.HasCodeColumn);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Matrix_with_unused_columns_reads_their_dashes_as_null()
    {
        var m = PackedLists.ParseMatrix("Colour,-,-,|Red,-,-,2", _warnings);
        Assert.Equal(["Colour", null, null], m.Headings);
        Assert.Equal(new MatrixRow("Red", null, null, 2m), Assert.Single(m.Rows));
    }

    [Fact]
    public void Matrix_placeholder_of_a_plain_item_is_empty()
    {
        Assert.True(PackedLists.ParseMatrix("-,-,-,", _warnings).IsEmpty);
        Assert.True(PackedLists.ParseMatrix(null, _warnings).IsEmpty);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Matrix_headed_CODE_has_a_code_column()
    {
        var m = PackedLists.ParseMatrix("CODE,Size,,|X100,S,-,1|X101,M,-,2", _warnings);
        Assert.True(m.HasCodeColumn);
        Assert.Equal("X101", m.Rows[1].Value1);
    }

    [Fact]
    public void Matrix_bad_stock_count_reads_as_zero_with_a_warning()
    {
        var m = PackedLists.ParseMatrix("Colour,,,|Red,-,-,lots", _warnings);
        Assert.Equal(0m, Assert.Single(m.Rows).OnHand);
        Assert.Single(_warnings);
    }

    [Fact]
    public void Supplier_list_reads_entries_in_order_with_or_without_leading_bar()
    {
        var list = PackedLists.ParseSupplierList("|ACME TOOLS,AT-1,12.500,6.00|BETA, INC.,B2,3.00,1|", _warnings);
        Assert.Equal(2, list.Count);
        Assert.Equal(new SupplierEntry("ACME TOOLS", "AT-1", 12.5m, 6m), list[0]);
        Assert.Equal(new SupplierEntry("BETA, INC.", "B2", 3m, 1m), list[1]);

        var noBar = PackedLists.ParseSupplierList("GAMMA LTD,,0.410,150|", _warnings);
        Assert.Equal(new SupplierEntry("GAMMA LTD", null, 0.41m, 150m), Assert.Single(noBar));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Kit_reads_components_with_commas_in_the_description()
    {
        var kit = PackedLists.ParseKit("|K1,PART, LARGE,2.00|K2,SMALL PART,1|", _warnings);
        Assert.Equal([new KitEntry("K1", "PART, LARGE", 2m), new KitEntry("K2", "SMALL PART", 1m)], kit);
    }

    [Fact]
    public void Serials_read_US_dates_by_default_and_UK_dates_when_set()
    {
        var us = PackedLists.ParseSerials("|SN-001,IN STOCK,02/28/2012|SN-002,SOLD,03/01/2013|", ukDates: false, _warnings);
        Assert.Equal(new SerialEntry("SN-001", "IN STOCK", new DateOnly(2012, 2, 28)), us[0]);
        Assert.Equal(new DateOnly(2013, 3, 1), us[1].Date);

        var uk = PackedLists.ParseSerials("|SN-9,IN STOCK,28/02/2012|", ukDates: true, _warnings);
        Assert.Equal(new DateOnly(2012, 2, 28), Assert.Single(uk).Date);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Number_lists_read_weekly_values_and_drop_stray_NULs()
    {
        var weekly = PackedLists.ParseNumbers("1.00,2.00,0.5\0\0\00,4.00", "WEEKLY", _warnings);
        Assert.Equal([1m, 2m, 0.5m, 4m], weekly);
        Assert.Single(_warnings);
    }

    [Fact]
    public void Number_lists_read_twelve_months_and_ignore_padding()
    {
        var lastYear = PackedLists.ParseNumbers(string.Join(',', Enumerable.Range(1, 12).Select(i => i + ".00")) + new string(' ', 40), "LASTYR", _warnings);
        Assert.Equal(12, lastYear.Count);
        Assert.Equal(12m, lastYear[11]);
        Assert.Empty(PackedLists.ParseNumbers("      ", "LASTYR", _warnings));
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("TRUE ", true)]
    [InlineData("FALSE", false)]
    [InlineData(".T.", true)]
    [InlineData(null, false)]
    public void Text_logicals_read_as_the_original_writes_them(string? text, bool expected) =>
        Assert.Equal(expected, PackedLists.ParseTextLogical(text));

    [Theory]
    [InlineData(@"C:\Retail Plus 30\PICS\Tent.JPG", "Tent.JPG")]
    [InlineData(@"D:\Photos\Tent.JPG", @"D:\Photos\Tent.JPG")]
    [InlineData(null, null)]
    public void Photo_paths_into_a_PICS_folder_become_relative(string? path, string? expected) =>
        Assert.Equal(expected, DbfImporter.PhotoPath(path));
}
