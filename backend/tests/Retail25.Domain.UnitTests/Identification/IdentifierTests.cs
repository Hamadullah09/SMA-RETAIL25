using FluentAssertions;
using Retail25.Domain.Identification;
using Xunit;

namespace Retail25.Domain.UnitTests.Identification;

/// <summary>
/// Type 2 barcodes and the identifier classifier (guide p.98, doc 04 §5).
/// </summary>
public sealed class RandomWeightBarcodeParserTests
{
    [Fact]
    public void Parses_the_stock_code_and_embedded_price()
    {
        // 2 | 01234 | 0 | 0512 | 8  →  stock code 01234, price 5.12
        var parsed = RandomWeightBarcodeParser.Parse("201234005128");

        parsed.Should().NotBeNull();
        parsed!.StockCode.Should().Be("01234");
        parsed.EmbeddedPrice.Should().Be(5.12m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678901")]      // eleven digits
    [InlineData("1234567890123")]    // thirteen digits
    [InlineData("301234005128")]     // wrong number-system character
    [InlineData("20123400512X")]     // not all digits
    public void Returns_null_for_anything_that_is_not_a_type_2_barcode(string candidate)
        => RandomWeightBarcodeParser.Parse(candidate).Should().BeNull();

    /// <summary>
    /// A bad check digit is reported but never rejects the scan: scales in the field print barcodes
    /// that fail it, and refusing a sale over a check digit is not acceptable at a queue.
    /// </summary>
    [Fact]
    public void A_failing_check_digit_is_reported_but_still_parses()
    {
        var parsed = RandomWeightBarcodeParser.Parse("201234005120");

        parsed.Should().NotBeNull();
        parsed!.EmbeddedPrice.Should().Be(5.12m);
        parsed.CheckDigitValid.Should().BeFalse();
    }
}

public sealed class IdentifierClassifierTests
{
    private const string SgtinEpc = "3034257BF400B7800004CB2F";

    [Fact]
    public void An_epc_is_recognised_before_anything_else()
    {
        var classified = IdentifierClassifier.Classify(SgtinEpc, scanRandomWeightBarcodes: true);

        classified.Kind.Should().Be(IdentifierKind.Epc);
        classified.Value.Should().Be(SgtinEpc);
    }

    [Fact]
    public void An_epc_is_normalised_to_upper_case()
        => IdentifierClassifier.Classify(SgtinEpc.ToLowerInvariant(), false).Value.Should().Be(SgtinEpc);

    [Fact]
    public void A_weighed_barcode_is_recognised_when_the_station_is_configured_for_it()
    {
        var classified = IdentifierClassifier.Classify("201234005128", scanRandomWeightBarcodes: true);

        classified.Kind.Should().Be(IdentifierKind.RandomWeight);
        classified.StockCode.Should().Be("01234");
        classified.EmbeddedPrice.Should().Be(5.12m);
    }

    /// <summary>
    /// Without the station setting, the same digits are an ordinary code. A store with no scales must
    /// still be able to sell a product whose UPC happens to begin with a 2 (guide p.98).
    /// </summary>
    [Fact]
    public void The_same_digits_are_an_ordinary_code_when_weighed_scanning_is_off()
    {
        var classified = IdentifierClassifier.Classify("201234005128", scanRandomWeightBarcodes: false);

        classified.Kind.Should().Be(IdentifierKind.Code);
        classified.EmbeddedPrice.Should().BeNull();
    }

    [Fact]
    public void An_empty_identifier_classifies_as_empty_rather_than_throwing()
        => IdentifierClassifier.Classify("   ", true).Kind.Should().Be(IdentifierKind.Empty);
}
