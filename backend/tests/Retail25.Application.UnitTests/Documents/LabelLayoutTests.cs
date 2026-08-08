using FluentAssertions;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// Grid arithmetic for label stock. A layout that overruns the page prints the last column onto the
/// paper edge and wastes the sheet, and nobody sees it until it comes out of the printer.
/// </summary>
public sealed class LabelLayoutTests
{
    [Theory]
    [InlineData(LabelStock.Avery5160, 30)]
    [InlineData(LabelStock.Avery8160, 30)]
    [InlineData(LabelStock.Avery8163, 10)]
    [InlineData(LabelStock.S644N, 6)]
    public void Each_stock_holds_the_number_of_labels_on_the_box(LabelStock stock, int expected)
        => AveryLayouts.For(stock).PerSheet.Should().Be(expected);

    [Fact]
    public void Every_stock_resolves_to_its_own_layout()
    {
        foreach (var stock in Enum.GetValues<LabelStock>())
        {
            AveryLayouts.For(stock).Stock.Should().Be(stock);
        }
    }

    /// <summary>
    /// Left margin + labels + gaps must fit the sheet with the mirrored right margin still to spare.
    /// </summary>
    [Fact]
    public void No_layout_runs_off_the_right_edge()
    {
        foreach (var layout in AveryLayouts.All)
        {
            var used = layout.MarginLeft
                + (layout.Columns * layout.LabelWidth)
                + ((layout.Columns - 1) * layout.GapHorizontal);

            used.Should().BeLessThanOrEqualTo(layout.PageWidth - layout.MarginLeft,
                because: $"{layout.DisplayName} has to fit 8.5\" with an equal right margin");
        }
    }

    [Fact]
    public void No_layout_runs_off_the_bottom_edge()
    {
        foreach (var layout in AveryLayouts.All)
        {
            var used = layout.MarginTop
                + (layout.Rows * layout.LabelHeight)
                + ((layout.Rows - 1) * layout.GapVertical);

            used.Should().BeLessThanOrEqualTo(layout.PageHeight - layout.MarginTop,
                because: $"{layout.DisplayName} has to fit 11\" with an equal bottom margin");
        }
    }

    /// <summary>8160 is the inkjet twin of 5160 — the geometry is the same sheet.</summary>
    [Fact]
    public void The_inkjet_twin_shares_the_laser_geometry()
    {
        var laser = AveryLayouts.Avery5160;
        var inkjet = AveryLayouts.Avery8160;

        (inkjet.Columns, inkjet.Rows, inkjet.LabelWidth, inkjet.LabelHeight)
            .Should().Be((laser.Columns, laser.Rows, laser.LabelWidth, laser.LabelHeight));

        inkjet.DisplayName.Should().NotBe(laser.DisplayName);
    }

    [Fact]
    public void Every_stock_has_a_name_for_the_picker()
    {
        foreach (var stock in Enum.GetValues<LabelStock>())
        {
            LabelStockNames.Describe(stock).Should().NotBeNullOrWhiteSpace().And.NotBe(stock.ToString());
        }
    }
}
