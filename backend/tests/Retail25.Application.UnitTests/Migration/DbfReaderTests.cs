using System.Text;
using FluentAssertions;
using Retail25.Infrastructure.LegacyData;
using Xunit;

namespace Retail25.Application.UnitTests.Migration;

/// <summary>
/// The DBF reader, against tables built byte for byte to the real format.
/// <para>
/// This is the piece a cutover rests on: if it reads a column one byte out, an entire shop's
/// catalogue arrives subtly wrong and nothing downstream can tell.
/// </para>
/// </summary>
public sealed class DbfReaderTests
{
    private static DbfReader Open(byte[] table) => DbfReader.Create(new MemoryStream(table), memo: null);

    [Fact]
    public void The_header_reports_the_columns_it_declares()
    {
        using var reader = Open(DbfFixture.Inventory([["Widget", "A-1", "Hardware", "", "", "1", "4.00", "9.99", "10", "", ""]]));

        reader.Header.Fields.Should().HaveCount(11);
        reader.Header.RecordCount.Should().Be(1);
        reader.Header.Fields[0].Name.Should().Be("ITEMNAME");
        reader.Header.Fields[1].Name.Should().Be("STOCKCODE");
        reader.Header.Describe().Should().Contain("dBase III+");
    }

    [Fact]
    public void Fields_come_back_trimmed_of_their_padding()
    {
        using var reader = Open(DbfFixture.Inventory(
            [["Columbia polo", "POLO01", "Clothing", "Shirts", "L", "1", "18.500", "49.99", "12", "Acme", "AC-99"]]));

        var record = reader.ReadRecords().Single();

        record.Fields["ITEMNAME"].Should().Be("Columbia polo");
        record.Fields["STOCKCODE"].Should().Be("POLO01");
        record.Fields["PRICE"].Should().Be("49.99");
        record.Fields["ONHAND"].Should().Be("12");
        record.IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// A legacy table that was never packed is full of these. They are yielded with their flag
    /// rather than skipped, because whether they matter is the importer's decision.
    /// </summary>
    [Fact]
    public void A_deleted_record_is_flagged_rather_than_hidden()
    {
        var table = DbfFixture.Inventory(
            [
                ["Kept", "A-1", "", "", "", "", "", "", "", "", ""],
                ["Gone", "B-2", "", "", "", "", "", "", "", "", ""],
            ],
            deletedRows: [1]);

        using var reader = Open(table);
        var records = reader.ReadRecords().ToList();

        records.Should().HaveCount(2);
        records[0].IsDeleted.Should().BeFalse();
        records[1].IsDeleted.Should().BeTrue();
        records[1].Fields["ITEMNAME"].Should().Be("Gone");
    }

    [Fact]
    public void An_empty_field_comes_back_as_null_rather_than_whitespace()
    {
        using var reader = Open(DbfFixture.Inventory([["Widget", "A-1", "", "", "", "", "", "", "", "", ""]]));

        reader.ReadRecords().Single().Fields["DEPARTMENT"].Should().BeNull();
    }

    /// <summary>
    /// Numerics right-align in the file. A reader that assumed left-alignment would return "4.00   "
    /// and every downstream parse would still work — which is exactly why this is worth pinning.
    /// </summary>
    [Fact]
    public void A_right_aligned_numeric_reads_correctly()
    {
        using var reader = Open(DbfFixture.Inventory([["Widget", "A-1", "", "", "", "", "  4.000", "     9.99", "        10", "", ""]]));

        var record = reader.ReadRecords().Single();

        record.Fields["COST"].Should().Be("4.000");
        record.Fields["PRICE"].Should().Be("9.99");
    }

    [Fact]
    public void Every_row_is_read()
    {
        var rows = Enumerable.Range(1, 250)
            .Select(i => (IReadOnlyList<string>)[$"Item {i}", $"SKU-{i:0000}", "", "", "", "", "1.00", "2.00", "3", "", ""])
            .ToList();

        using var reader = Open(DbfFixture.Inventory(rows));

        var records = reader.ReadRecords().ToList();

        records.Should().HaveCount(250);
        records[0].Fields["STOCKCODE"].Should().Be("SKU-0001");
        records[^1].Fields["STOCKCODE"].Should().Be("SKU-0250");
    }

    /// <summary>
    /// A copy interrupted or a floppy image cut short. Stopping quietly and reporting the shortfall
    /// is kinder than throwing halfway through an analysis nobody has seen yet.
    /// </summary>
    [Fact]
    public void A_truncated_file_yields_what_it_can_rather_than_throwing()
    {
        var table = DbfFixture.Inventory(
            [
                ["First", "A-1", "", "", "", "", "", "", "", "", ""],
                ["Second", "B-2", "", "", "", "", "", "", "", "", ""],
                ["Third", "C-3", "", "", "", "", "", "", "", "", ""],
            ]);

        var truncated = table[..(table.Length - 60)];

        using var reader = Open(truncated);

        reader.Header.RecordCount.Should().Be(3);
        reader.ReadRecords().Count().Should().BeLessThan(3);
    }

    [Fact]
    public void A_file_that_is_not_a_dbf_is_refused()
    {
        var act = () => DbfReader.Create(new MemoryStream(Encoding.ASCII.GetBytes("this is not a table")));

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_header_declaring_no_columns_is_refused()
    {
        var act = () => DbfReader.Create(new MemoryStream(DbfFixture.Build([], [])));

        act.Should().Throw<InvalidDataException>();
    }

    /* ---------------------------------------------------------------------------------------------
     * Field types
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void A_date_field_is_normalised_to_iso()
    {
        var table = DbfFixture.Build(
            [new FixtureField("CODE", 'C', 6), new FixtureField("SOLDON", 'D', 8)],
            [["A-1", "20250314"]]);

        using var reader = Open(table);

        reader.ReadRecords().Single().Fields["SOLDON"].Should().Be("2025-03-14");
    }

    [Fact]
    public void A_blank_date_comes_back_as_null()
    {
        var table = DbfFixture.Build(
            [new FixtureField("CODE", 'C', 6), new FixtureField("SOLDON", 'D', 8)],
            [["A-1", "        "]]);

        using var reader = Open(table);

        reader.ReadRecords().Single().Fields["SOLDON"].Should().BeNull();
    }

    [Theory]
    [InlineData("T", "true")]
    [InlineData("Y", "true")]
    [InlineData("F", "false")]
    [InlineData("N", "false")]
    [InlineData("?", null)]
    [InlineData(" ", null)]
    public void A_logical_field_is_normalised_to_one_spelling(string raw, string? expected)
        => DbfReader.ParseLogical(raw).Should().Be(expected);

    [Theory]
    [InlineData("20250314", "2025-03-14")]
    [InlineData("19991231", "1999-12-31")]
    [InlineData("00000000", null)]
    [InlineData("2025031", null)]
    [InlineData("        ", null)]
    public void Dbf_dates_parse_or_report_nothing(string raw, string? expected)
        => DbfReader.ParseDbfDate(raw).Should().Be(expected);

    /// <summary>
    /// Memo columns need the companion file. Without it they read as empty rather than as a block
    /// number, which would look like data.
    /// </summary>
    [Fact]
    public void A_memo_column_with_no_memo_file_reads_empty_and_is_reported()
    {
        var table = DbfFixture.Build(
            [new FixtureField("CODE", 'C', 6), new FixtureField("NOTES", 'M', 10)],
            [["A-1", "         3"]]);

        using var reader = Open(table);

        reader.Header.HasMemoFields.Should().BeTrue();
        reader.ReadRecords().Single().Fields["NOTES"].Should().BeNull();
    }
}
