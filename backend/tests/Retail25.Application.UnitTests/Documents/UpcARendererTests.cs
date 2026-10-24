using FluentAssertions;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// UPC-A on labels, and which items get it.
/// <para>
/// A shop asked for UPC-A barcodes. Most of its items have no UPC, so the run defaults to UPC-A
/// where an item has one and the old Code 39 of the stock code where it does not — every label still
/// gets a barcode that scans — while a run set to UPC-A only leaves an item without one bare rather
/// than quietly printing another kind.
/// </para>
/// </summary>
public sealed class UpcARendererTests
{
    /// <summary>Guard bars, six digits, centre guard, six digits, guard bars: 95 modules.</summary>
    private const int UpcAModules = 95;

    [Fact]
    public void A_valid_upc_a_encodes_with_quiet_zones()
    {
        UpcARenderer.TryEncode("036000291452", out var pattern).Should().BeTrue();

        pattern.Text.Should().Be("036000291452");

        var firstBar = pattern.Modules.ToList().IndexOf(true);
        var lastBar = pattern.Modules.ToList().LastIndexOf(true);

        firstBar.Should().BeGreaterThan(0, "a UPC-A without a quiet zone does not scan");
        lastBar.Should().BeLessThan(pattern.Width - 1);
        (lastBar - firstBar + 1).Should().Be(UpcAModules);
    }

    /// <summary>
    /// The real test of a barcode: a reader gets the number back. Three pixels a module, which is
    /// coarser than any label prints at, so this is no kinder than the shelf.
    /// </summary>
    [Theory]
    [InlineData("400000001371")] // an in-store number, as the shop's own goods are given
    [InlineData("036000291452")]
    public void The_bars_read_back_as_the_same_number(string upc)
    {
        UpcARenderer.TryEncode(upc, out var pattern).Should().BeTrue();

        const int pixelsPerModule = 3;
        var row = new ZXing.Common.BitArray(pattern.Width * pixelsPerModule);

        for (var module = 0; module < pattern.Width; module++)
        {
            if (!pattern.Modules[module]) continue;

            for (var pixel = 0; pixel < pixelsPerModule; pixel++)
            {
                row[(module * pixelsPerModule) + pixel] = true;
            }
        }

        var read = new ZXing.OneD.UPCAReader().decodeRow(0, row, new Dictionary<ZXing.DecodeHintType, object>());

        read.Should().NotBeNull();
        read!.Text.Should().Be(upc);
    }

    [Theory]
    [InlineData("036000291453")] // wrong check digit: ZXing would print bars for a different number
    [InlineData("03600029145")] // eleven digits: ZXing would invent the twelfth
    [InlineData("SHIRT01")]
    [InlineData(null)]
    public void Anything_but_a_complete_correct_upc_a_is_refused(string? value)
        => UpcARenderer.TryEncode(value, out _).Should().BeFalse();

    [Fact]
    public void By_default_an_item_with_a_upc_a_prints_it()
    {
        var bars = LabelBarcodes.For(new PriceTag("SHIRT01", "Shirt", 10m, Barcode: "036000291452"), LabelSymbology.Auto);

        bars.Should().NotBeNull();
        bars!.Text.Should().Be("036000291452");
        Encodes(bars, "036000291452").Should().BeTrue();
    }

    [Fact]
    public void By_default_an_item_without_one_still_gets_a_barcode_that_scans()
    {
        var bars = LabelBarcodes.For(new PriceTag("SHIRT01", "Shirt", 10m), LabelSymbology.Auto);

        bars.Should().NotBeNull();
        bars!.Text.Should().Be("SHIRT01", "it falls back to Code 39 of the stock code, which the till also reads");
    }

    [Fact]
    public void Upc_a_only_leaves_an_item_without_one_bare()
        => LabelBarcodes.For(new PriceTag("SHIRT01", "Shirt", 10m), LabelSymbology.UpcA).Should().BeNull();

    [Fact]
    public void Code_39_still_prints_the_legacy_label_for_an_item_that_has_a_upc()
    {
        var bars = LabelBarcodes.For(new PriceTag("SHIRT01", "Shirt", 10m, Barcode: "036000291452"), LabelSymbology.Code39);

        bars.Should().NotBeNull();
        Encodes(bars!, "036000291452").Should().BeFalse("Code 39 of the same digits is a different pattern");
    }

    private static bool Encodes(BarcodePattern bars, string upc)
        => UpcARenderer.TryEncode(upc, out var expected) && expected.Modules.SequenceEqual(bars.Modules);
}
