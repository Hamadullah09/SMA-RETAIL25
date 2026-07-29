using FluentAssertions;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Peripherals;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Peripherals;

/// <summary>
/// Escape sequences are administrator-editable strings (guide p.80). Getting the parse wrong means a
/// drawer that never opens or a receipt that never cuts, so the exact byte values are asserted.
/// </summary>
public sealed class EscapeSequenceTests
{
    [Fact]
    public void The_epson_drawer_kick_parses_to_its_documented_bytes()
        => EscapeSequence.Parse("27,112,0,50,250").Should().Equal(27, 112, 0, 50, 250);

    [Fact]
    public void The_star_cutter_parses_to_its_documented_bytes()
        => EscapeSequence.Parse("27,100,48").Should().Equal(27, 100, 48);

    [Theory]
    [InlineData("27, 112, 0")]
    [InlineData("27 112 0")]
    [InlineData("27;112;0")]
    public void Spacing_and_separators_are_tolerated(string sequence)
        => EscapeSequence.Parse(sequence).Should().Equal(27, 112, 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_sequence_is_simply_empty(string? sequence)
        => EscapeSequence.Parse(sequence).Should().BeEmpty();

    /// <summary>
    /// A malformed value is skipped rather than throwing. A mistyped cutter code should mean "the
    /// paper does not cut", not "the receipt does not print" — the sale is already saved by then.
    /// </summary>
    [Fact]
    public void A_malformed_value_is_skipped_rather_than_failing_the_print()
        => EscapeSequence.Parse("27,oops,112,999,0").Should().Equal(27, 112, 0);

    [Fact]
    public void Formatting_round_trips_the_stored_notation()
        => EscapeSequence.Format(EscapeSequence.Parse("27,112,0,50,250")).Should().Be("27,112,0,50,250");
}

/// <summary>
/// Receipt rendering (guide p.78–80). The plain-text form is asserted rather than the byte stream:
/// a layout regression should be visible in a diff, and the escape codes around it are covered
/// separately.
/// </summary>
public sealed class EscPosRendererTests
{
    [Fact]
    public void A_forty_column_slip_shows_the_figures_and_the_total()
    {
        var slip = EscPosRenderer.RenderText(Document(), 40);

        slip.Should().Contain("Columbia polo");
        slip.Should().Contain("49.99");
        slip.Should().Contain("GST");
        slip.Should().Contain("TOTAL");
        slip.Should().Contain("$55.99");

        slip.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').Length <= 40);
    }

    /// <summary>
    /// A 20-column roll cannot fit description, quantity, price and extension on one line, so the
    /// description gets its own line. Anything else wraps into mush.
    /// </summary>
    [Fact]
    public void A_twenty_column_slip_never_exceeds_its_width()
    {
        var slip = EscPosRenderer.RenderText(Document(), 20);

        slip.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').Length <= 20);
        slip.Should().Contain("49.99");
    }

    [Fact]
    public void A_packing_slip_carries_no_money_at_all()
    {
        var slip = EscPosRenderer.RenderText(Document() with { Format = ReceiptFormat.PackingSlip }, 40);

        slip.Should().Contain("PACKING SLIP");
        slip.Should().Contain("Columbia polo");
        slip.Should().NotContain("TOTAL");
        slip.Should().NotContain("55.99");
    }

    [Fact]
    public void A_reprint_says_so_on_its_face()
        => EscPosRenderer.RenderText(Document() with { IsReprint = true }, 40).Should().Contain("*** REPRINT ***");

    [Fact]
    public void A_voided_sale_says_so_on_its_face()
        => EscPosRenderer.RenderText(Document() with { IsVoided = true }, 40).Should().Contain("*** VOIDED ***");

    /// <summary>The rounding penny is printed, never absorbed silently (guide p.84).</summary>
    [Fact]
    public void The_cash_rounding_adjustment_is_printed()
    {
        var slip = EscPosRenderer.RenderText(Document() with { RoundingAdjustment = 0.01m }, 40);

        slip.Should().Contain("Rounding");
    }

    [Fact]
    public void A_card_sale_prints_the_signature_line_when_the_store_asks_for_one()
    {
        var slip = EscPosRenderer.RenderText(Document() with { PrintSignatureLine = true }, 40);

        slip.Should().Contain("Signature");
    }

    [Fact]
    public void Rendering_to_bytes_wraps_the_slip_in_the_configured_setup_and_cutter_codes()
    {
        var profile = Printer() with { SetupCommand = "27,64", CutterCommand = "27,105" };

        var bytes = EscPosRenderer.Render(Document(), profile);

        bytes.Take(2).Should().Equal(27, 64);
        bytes.TakeLast(2).Should().Equal(27, 105);
    }

    private static PrinterProfileContract Printer() => new(
        Guid.NewGuid(),
        "Default",
        Port: null,
        SetupCommand: null,
        CutterCommand: null,
        RedCommand: null,
        BlackCommand: null,
        DefaultCopies: 1,
        PageEject: false,
        ExtraCopyOnCard: false,
        InitializeSerial: false,
        Output: "Slip40",
        Columns: 40,
        DrawerTrigger: "27,112,0,50,250",
        DrawerRepeat: 1,
        OpenDrawerOnPrint: false);

    private static ReceiptDocument Document() => new(
        Guid.NewGuid(),
        1042,
        ReceiptFormat.Slip40,
        "Test Store",
        ["1 High Street", "Anytown"],
        "GST123456",
        "001",
        "Sarah K.",
        null,
        null,
        new DateTimeOffset(2026, 7, 29, 14, 30, 0, TimeSpan.Zero),
        [new ReceiptLine("POLO01", "Columbia polo", 1m, 49.99m, 49.99m, null, null, true, true, false)],
        [],
        49.99m,
        0m,
        "GST",
        2.50m,
        "PST",
        3.50m,
        "Service",
        0m,
        0m,
        55.99m,
        [new ReceiptTender("Cash", 55.99m, 60.00m, 4.01m, null)],
        4.01m,
        0,
        0,
        "$",
        null,
        IsReprint: false,
        IsVoided: false,
        PrintSignatureLine: false);
}

/// <summary>
/// Scales answer in several dialects, so the number is extracted rather than the format assumed
/// (guide p.81).
/// </summary>
public sealed class ScaleResponseTests
{
    [Theory]
    [InlineData("1.245", 1.245, true)]
    [InlineData("  2.50 kg  ", 2.50, true)]
    [InlineData("ST,GS,   0.755kg", 0.755, true)]
    [InlineData("-0.010", -0.010, true)]
    public void A_stable_reading_yields_its_number(string response, double expected, bool stable)
    {
        PeripheralCoordinator.TryParseWeight(response, out var value, out var isStable).Should().BeTrue();

        value.Should().Be((decimal)expected);
        isStable.Should().Be(stable);
    }

    /// <summary>
    /// An unstable reading is reported as unstable rather than discarded: the cashier watching the
    /// platter is better placed than the agent to decide whether to wait for it to settle.
    /// </summary>
    [Theory]
    [InlineData("US, 1.245")]
    [InlineData("?1.245")]
    public void An_unsettled_reading_is_flagged_but_still_returned(string response)
    {
        PeripheralCoordinator.TryParseWeight(response, out var value, out var stable).Should().BeTrue();

        value.Should().Be(1.245m);
        stable.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ERR")]
    public void An_unreadable_answer_yields_no_weight(string? response)
        => PeripheralCoordinator.TryParseWeight(response, out _, out _).Should().BeFalse();
}
