using FluentAssertions;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Split tender, change and cash rounding (guide p.8–9, p.84, decision P4).
/// <para>
/// The asymmetry under test is the important part: cash rounds to the smallest coin in circulation
/// while card and account settle to the exact cent. Rounding everything would quietly shift money on
/// every electronic sale; rounding nothing would make a store that abolished the penny unable to
/// balance its drawer.
/// </para>
/// </summary>
public sealed class TenderCalculatorTests
{
    private static readonly long CashTenderId = TestIds.Next();
    private static readonly long CardTenderId = TestIds.Next();

    private static readonly MoneyRounding Nickel = new(2, MidpointRounding.AwayFromZero, 0.05m);
    private static readonly MoneyRounding Penny = MoneyRounding.Retail;

    [Fact]
    public void Exact_cash_settles_with_no_change()
    {
        var settlement = TenderCalculator.Settle(
            55.99m,
            [Cash(55.99m, 55.99m)],
            Penny).Value;

        settlement.IsSettled.Should().BeTrue();
        settlement.ChangeDue.Should().Be(0m);
        settlement.RoundingAdjustment.Should().Be(0m);
    }

    [Fact]
    public void Over_tendered_cash_returns_change()
    {
        var settlement = TenderCalculator.Settle(
            55.99m,
            [Cash(55.99m, 60.00m)],
            Penny).Value;

        settlement.IsSettled.Should().BeTrue();
        settlement.ChangeDue.Should().Be(4.01m);
        settlement.Tenders.Single().Amount.Should().Be(55.99m);
        settlement.Tenders.Single().ChangeGiven.Should().Be(4.01m);
    }

    [Fact]
    public void Cash_rounds_to_the_smallest_coin_and_reports_the_adjustment()
    {
        // 55.99 to the nearest nickel is 56.00: the store gains a cent and says so.
        var settlement = TenderCalculator.Settle(
            55.99m,
            [Cash(56.00m, 60.00m)],
            Nickel).Value;

        settlement.CashPortionDue.Should().Be(56.00m);
        settlement.RoundingAdjustment.Should().Be(0.01m);
        settlement.ChangeDue.Should().Be(4.00m);
        settlement.IsSettled.Should().BeTrue();
    }

    [Fact]
    public void A_card_leg_settles_to_the_exact_cent_even_under_nickel_rounding()
    {
        // Only the cash remainder rounds. The card takes 30.00 exactly, leaving 25.99 in cash, which
        // rounds to 26.00 — the card amount is untouched.
        var settlement = TenderCalculator.Settle(
            55.99m,
            [Card(30.00m), Cash(26.00m, 26.00m)],
            Nickel).Value;

        settlement.Tenders.Single(t => t.Behaviour == TenderBehaviour.Card).Amount.Should().Be(30.00m);
        settlement.CashPortionDue.Should().Be(26.00m);
        settlement.RoundingAdjustment.Should().Be(0.01m);
        settlement.IsSettled.Should().BeTrue();
    }

    [Fact]
    public void A_short_payment_is_reported_rather_than_accepted()
    {
        var settlement = TenderCalculator.Settle(
            55.99m,
            [Cash(50.00m, 50.00m)],
            Penny).Value;

        settlement.IsSettled.Should().BeFalse();
        settlement.OutstandingBalance.Should().Be(5.99m);
    }

    [Fact]
    public void A_tender_that_forbids_over_tender_is_rejected_when_it_exceeds_the_total()
    {
        var result = TenderCalculator.Settle(
            20.00m,
            [Card(25.00m)],
            Penny);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tender.over_tender_not_allowed");
    }

    [Fact]
    public void Three_way_split_settles_exactly()
    {
        var settlement = TenderCalculator.Settle(
            100.00m,
            [Card(40.00m), Card(35.00m), Cash(25.00m, 25.00m)],
            Penny).Value;

        settlement.IsSettled.Should().BeTrue();
        settlement.AmountApplied.Should().Be(100.00m);
        settlement.ChangeDue.Should().Be(0m);
    }

    private static TenderInputLine Cash(decimal amount, decimal tendered)
        => new(CashTenderId, TenderBehaviour.Cash, RoundsToMinimumTender: true, AllowsOverTender: true, amount, tendered);

    private static TenderInputLine Card(decimal amount)
        => new(CardTenderId, TenderBehaviour.Card, RoundsToMinimumTender: false, AllowsOverTender: false, amount, amount);
}
