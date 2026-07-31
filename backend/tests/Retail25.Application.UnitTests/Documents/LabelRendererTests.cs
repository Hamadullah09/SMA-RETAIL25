using FluentAssertions;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// Cell placement and the rendered PDF. The cell tests carry the weight — where a label lands on the
/// sheet is the part that can be wrong in a way the eye will not catch until a batch has printed.
/// </summary>
public sealed class LabelRendererTests
{
    private static readonly LabelLayout Sheet30 = AveryLayouts.Avery5160;

    private static PriceTag Tag(string code = "A-1")
        => new(code, "Widget", 9.99m, Barcode: code, BinLocation: "B12");

    private static LabelSheetRequest Request(IReadOnlyList<LabelLine> lines, int skip = 0)
        => new(LabelStock.Avery5160, lines, ShowBarcode: true, SkipLabels: skip);

    [Fact]
    public void Copies_become_that_many_cells()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 4)]), Sheet30);

        cells.Count(c => c is not null).Should().Be(4);
    }

    /// <summary>
    /// The grid only holds if every sheet is full, so a short run is padded out with blanks rather
    /// than left ragged.
    /// </summary>
    [Fact]
    public void A_short_run_is_padded_out_to_a_whole_sheet()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 4)]), Sheet30);

        cells.Should().HaveCount(Sheet30.PerSheet);
        cells.Skip(4).Should().OnlyContain(c => c == null);
    }

    [Fact]
    public void An_exactly_full_sheet_is_not_padded_to_a_second_one()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 30)]), Sheet30);

        cells.Should().HaveCount(30).And.OnlyContain(c => c != null);
    }

    [Fact]
    public void A_run_that_overflows_fills_a_second_sheet()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 31)]), Sheet30);

        cells.Should().HaveCount(60);
        cells.Take(31).Should().OnlyContain(c => c != null);
        cells.Skip(31).Should().OnlyContain(c => c == null);
    }

    /// <summary>
    /// Re-using a part-used sheet is the common case at a till counter. The already-peeled positions
    /// have to be skipped, or the print lands on bare backing and the rest of the sheet is wasted.
    /// </summary>
    [Fact]
    public void Skipped_labels_are_left_blank_at_the_front()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 2)], skip: 7), Sheet30);

        cells.Take(7).Should().OnlyContain(c => c == null);
        cells[7].Should().NotBeNull();
        cells[8].Should().NotBeNull();
        cells.Should().HaveCount(Sheet30.PerSheet);
    }

    [Fact]
    public void Skipping_past_a_sheet_boundary_still_prints_every_label()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 5)], skip: 28), Sheet30);

        cells.Should().HaveCount(60);
        cells.Count(c => c is not null).Should().Be(5);
    }

    [Fact]
    public void A_negative_skip_is_treated_as_a_fresh_sheet()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 1)], skip: -5), Sheet30);

        cells[0].Should().NotBeNull();
    }

    [Fact]
    public void Zero_copies_still_prints_one_label()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(Request([new LabelLine(Tag(), 0)]), Sheet30);

        cells.Count(c => c is not null).Should().Be(1);
    }

    [Fact]
    public void Lines_keep_their_order_on_the_sheet()
    {
        var cells = QuestPdfLabelRenderer.BuildCells(
            Request([new LabelLine(Tag("FIRST"), 2), new LabelLine(Tag("SECOND"), 1)]),
            Sheet30);

        cells[0]!.StockCode.Should().Be("FIRST");
        cells[1]!.StockCode.Should().Be("FIRST");
        cells[2]!.StockCode.Should().Be("SECOND");
    }

    [Theory]
    [InlineData(LabelStock.Avery5160)]
    [InlineData(LabelStock.Avery8160)]
    [InlineData(LabelStock.Avery8163)]
    [InlineData(LabelStock.S644N)]
    public void Every_stock_renders_a_pdf(LabelStock stock)
    {
        var pdf = new QuestPdfLabelRenderer().RenderPriceTags(
            new LabelSheetRequest(stock, [new LabelLine(Tag(), 3)]));

        BeAPdf(pdf);
    }

    [Fact]
    public void The_barcode_first_layout_also_renders()
        => BeAPdf(new QuestPdfLabelRenderer().RenderBarcodeLabels(Request([new LabelLine(Tag(), 2)])));

    /// <summary>
    /// A code Code 39 cannot carry must still produce a tag — without a barcode rather than with an
    /// unscannable one.
    /// </summary>
    [Fact]
    public void An_unencodable_code_still_renders_the_tag()
    {
        var pdf = new QuestPdfLabelRenderer().RenderPriceTags(
            Request([new LabelLine(new PriceTag("W#1", "Widget", 4.50m, Barcode: "W#1"), 1)]));

        BeAPdf(pdf);
    }

    [Fact]
    public void Turning_the_barcode_off_still_renders()
    {
        var pdf = new QuestPdfLabelRenderer().RenderPriceTags(
            new LabelSheetRequest(LabelStock.Avery5160, [new LabelLine(Tag(), 1)], ShowBarcode: false));

        BeAPdf(pdf);
    }

    private static void BeAPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
