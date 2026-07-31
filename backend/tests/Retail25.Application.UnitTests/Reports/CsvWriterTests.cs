using System.Globalization;
using FluentAssertions;
using Retail25.Application.Common;
using Xunit;

namespace Retail25.Application.UnitTests.Reports;

/// <summary>
/// The escaping rules matter more than they look: every report export runs through this, and a
/// product name with a comma in it silently shifts every column after it if the quoting is wrong.
/// </summary>
public sealed class CsvWriterTests
{
    [Fact]
    public void A_plain_row_is_written_unquoted()
    {
        var csv = new CsvWriter().Header("Code", "Name").Row("A-1", "Widget").ToString();

        csv.Should().Be("Code,Name" + Environment.NewLine + "A-1,Widget" + Environment.NewLine);
    }

    [Fact]
    public void A_value_containing_a_comma_is_quoted()
    {
        var csv = new CsvWriter().Row("Widget, large").ToString();

        csv.Should().Be("\"Widget, large\"" + Environment.NewLine);
    }

    [Fact]
    public void A_quote_inside_a_value_is_doubled_and_the_value_quoted()
    {
        var csv = new CsvWriter().Row("The \"big\" one").ToString();

        csv.Should().Be("\"The \"\"big\"\" one\"" + Environment.NewLine);
    }

    [Fact]
    public void A_newline_inside_a_value_is_quoted_so_the_row_does_not_split()
    {
        var csv = new CsvWriter().Row("Line one\nLine two").ToString();

        csv.Should().Be("\"Line one\nLine two\"" + Environment.NewLine);
    }

    [Fact]
    public void A_null_is_written_as_an_empty_cell()
    {
        var csv = new CsvWriter().Row("A", null, "B").ToString();

        csv.Should().Be("A,,B" + Environment.NewLine);
    }

    /// <summary>
    /// The regression that matters for a bookkeeper: on a machine whose locale writes 1,50 for one
    /// and a half, an un-invariant decimal turns one column into two.
    /// </summary>
    [Fact]
    public void Decimals_are_written_invariant_regardless_of_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var csv = new CsvWriter().Row(1.5m, 1234.56m).ToString();

            csv.Should().Be("1.5,1234.56" + Environment.NewLine);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Dates_are_written_in_a_round_trippable_shape()
    {
        var csv = new CsvWriter()
            .Row(new DateOnly(2026, 7, 31), new DateTimeOffset(2026, 7, 31, 9, 30, 0, TimeSpan.Zero))
            .ToString();

        csv.Should().StartWith("2026-07-31,2026-07-31T09:30:00.0000000+00:00");
    }
}
