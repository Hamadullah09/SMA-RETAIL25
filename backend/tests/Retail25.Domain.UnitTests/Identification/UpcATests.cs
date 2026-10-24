using FluentAssertions;
using Retail25.Domain.Identification;
using Xunit;

namespace Retail25.Domain.UnitTests.Identification;

/// <summary>
/// The UPC-A arithmetic. A wrong check digit prints a barcode every scanner rejects, and an in-store
/// number in the wrong range collides with the scale labels the till already decodes — both found
/// only at the counter, so both are pinned here.
/// </summary>
public sealed class UpcATests
{
    [Theory]
    [InlineData("036000291452")] // the textbook example
    [InlineData("012345678905")]
    [InlineData("400000000015")]
    public void A_correct_upc_a_is_valid(string upc) => UpcA.IsValid(upc).Should().BeTrue();

    [Theory]
    [InlineData("036000291453")] // last digit wrong
    [InlineData("03600029145")] // eleven digits
    [InlineData("0036000291452")] // thirteen
    [InlineData("03600029145A")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string? candidate) => UpcA.IsValid(candidate).Should().BeFalse();

    [Fact]
    public void The_check_digit_is_three_times_the_odd_positions_plus_the_even_ones()
    {
        // 0+6+0+2+1+5 = 14, times 3 = 42; 3+0+0+9+4 = 16; 58, so 2 to reach 60.
        UpcA.CheckDigit("03600029145").Should().Be(2);
    }

    [Fact]
    public void An_in_store_number_is_in_gs1s_retailer_range_and_valid()
    {
        var upc = UpcA.InStore(UpcA.InStoreKind.Product, 1);

        upc.Should().Be("400000000015");
        upc.Should().StartWith("4", "number system 4 is GS1's range for a retailer's own numbering");
        UpcA.IsValid(upc).Should().BeTrue();
    }

    [Fact]
    public void A_product_and_a_variant_with_the_same_id_get_different_numbers()
    {
        UpcA.InStore(UpcA.InStoreKind.Product, 42)
            .Should().NotBe(UpcA.InStore(UpcA.InStoreKind.Variant, 42));
    }

    [Fact]
    public void An_in_store_number_never_starts_with_2_which_the_till_reads_as_a_scale_label()
    {
        foreach (var id in new long[] { 1, 2, 99, 123_456_789, UpcA.MaxInStoreId })
        {
            UpcA.InStore(UpcA.InStoreKind.Product, id)[0].Should().NotBe('2');
            RandomWeightBarcodeParser.Parse(UpcA.InStore(UpcA.InStoreKind.Product, id)).Should().BeNull();
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1_000_000_000L)]
    public void An_id_that_does_not_fit_nine_digits_is_refused(long id)
    {
        var issuing = () => UpcA.InStore(UpcA.InStoreKind.Product, id);

        issuing.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_scanner_sending_the_ean_13_form_and_one_sending_upc_a_find_each_other()
    {
        UpcA.AlternateForm("036000291452").Should().Be("0036000291452");
        UpcA.AlternateForm("0036000291452").Should().Be("036000291452");
    }

    [Theory]
    [InlineData("036000291453")] // not a valid UPC-A
    [InlineData("1036000291452")] // thirteen digits, but not a zero-led EAN of a UPC-A
    [InlineData("SHIRT01")]
    [InlineData("12345")]
    [InlineData(null)]
    public void A_code_that_is_neither_form_has_no_alternate(string? code)
        => UpcA.AlternateForm(code).Should().BeNull();
}
