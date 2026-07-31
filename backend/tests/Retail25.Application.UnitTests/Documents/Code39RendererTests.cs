using FluentAssertions;
using Retail25.Infrastructure.Documents;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// A barcode that prints but will not scan is worse than no barcode, because nobody finds out until
/// there is a queue. These tests pin the encode down to the module pattern rather than trusting that
/// a PDF came out the other end.
/// </summary>
public sealed class Code39RendererTests
{
    [Fact]
    public void A_stock_code_encodes()
    {
        Code39Renderer.TryEncode("A1234", out var pattern).Should().BeTrue();

        pattern.Text.Should().Be("A1234");
        pattern.Width.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A barcode with no quiet zone is the classic reason a printed one will not read: the scanner
    /// cannot tell where the symbol starts. The pattern must carry white margins at both ends, with
    /// the actual bars — bracketed by Code 39's <c>*</c> guards — in between.
    /// </summary>
    [Fact]
    public void The_pattern_carries_a_quiet_zone_at_both_ends()
    {
        Code39Renderer.TryEncode("WIDGET1", out var pattern).Should().BeTrue();

        pattern.Modules[0].Should().BeFalse();
        pattern.Modules[^1].Should().BeFalse();

        var firstBar = pattern.Modules.ToList().IndexOf(true);
        var lastBar = pattern.Modules.ToList().LastIndexOf(true);

        firstBar.Should().BeGreaterThan(0);
        lastBar.Should().BeLessThan(pattern.Width - 1);
        (lastBar - firstBar).Should().BeGreaterThan(0);
    }

    /// <summary>Code 39 has no lower case. Upper-casing is right; refusing the label would not be.</summary>
    [Fact]
    public void Lower_case_is_upper_cased_rather_than_refused()
    {
        Code39Renderer.TryEncode("widget-1", out var lower).Should().BeTrue();
        Code39Renderer.TryEncode("WIDGET-1", out var upper).Should().BeTrue();

        lower.Text.Should().Be("WIDGET-1");
        lower.Modules.Should().Equal(upper.Modules);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_before_encoding()
    {
        Code39Renderer.TryEncode("  A1  ", out var padded).Should().BeTrue();
        Code39Renderer.TryEncode("A1", out var plain).Should().BeTrue();

        padded.Modules.Should().Equal(plain.Modules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_encode_is_reported_rather_than_thrown(string? value)
    {
        Code39Renderer.TryEncode(value, out var pattern).Should().BeFalse();

        pattern.Width.Should().Be(0);
    }

    /// <summary>
    /// The Code 39 alphabet is 43 characters. Anything outside it must come back false so the caller
    /// prints a tag with no barcode rather than one that only reads on a Full ASCII scanner — ZXing
    /// will quietly fall back to extended Code 39 if it is allowed to.
    /// </summary>
    [Theory]
    [InlineData("WIDGET#1")]
    [InlineData("WIDGET_1")]
    [InlineData("WIDGÉT")]
    [InlineData("A,1")]
    public void A_character_outside_the_alphabet_is_refused(string value)
    {
        Code39Renderer.TryEncode(value, out var pattern).Should().BeFalse();

        pattern.Width.Should().Be(0);
    }

    /// <summary>The punctuation Code 39 does carry has to keep working — stock codes are full of it.</summary>
    [Theory]
    [InlineData("A-1")]
    [InlineData("A.1")]
    [InlineData("A 1")]
    [InlineData("A/1")]
    [InlineData("A+1")]
    [InlineData("A$1")]
    [InlineData("A%1")]
    public void The_punctuation_in_the_alphabet_is_accepted(string value)
        => Code39Renderer.TryEncode(value, out _).Should().BeTrue();

    [Fact]
    public void A_longer_code_produces_a_wider_pattern()
    {
        Code39Renderer.TryEncode("A1", out var shortCode).Should().BeTrue();
        Code39Renderer.TryEncode("A1B2C3", out var longCode).Should().BeTrue();

        longCode.Width.Should().BeGreaterThan(shortCode.Width);
    }
}
