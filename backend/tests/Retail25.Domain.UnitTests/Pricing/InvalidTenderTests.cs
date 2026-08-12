using FluentAssertions;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// What the till must refuse to settle.
/// <para>
/// These exist because a cashier could type <c>abc</c> into the cash field and complete a sale. The
/// browser did <c>Number(tendered) || due</c> — <c>NaN</c> is falsy, so the nonsense became the exact
/// amount owed and the sale settled with an empty drawer. The server had the same shape of bug one
/// layer down: <c>AmountTendered &gt; 0 ? AmountTendered : Amount</c> turned "nothing was handed
/// over" into "exactly the right money was handed over".
/// </para>
/// <para>
/// A string never reaches this class — <c>decimal</c> sees to that, and malformed JSON is a 400
/// before any of this runs. What reaches it is the numeric residue of those bugs, and of a client
/// that has been tampered with. That is what is pinned here.
/// </para>
/// </summary>
public sealed class InvalidTenderTests
{
    private static readonly long CashTenderId = TestIds.Next();
    private static readonly long CardTenderId = TestIds.Next();
    private static readonly MoneyRounding Penny = MoneyRounding.Retail;

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Money_leaving_the_drawer_against_a_sale_is_refused(decimal amount)
    {
        var result = TenderCalculator.Settle(100m, [Cash(amount, amount)], Penny);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TenderCalculator.WrongDirection.Code);
    }

    [Fact]
    public void A_negative_amount_handed_over_against_a_sale_is_refused()
    {
        var result = TenderCalculator.Settle(100m, [Cash(100m, -50m)], Penny);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TenderCalculator.WrongDirection.Code);
    }

    /// <summary>
    /// The other direction, and the reason the rule is about direction rather than sign. A refund's
    /// grand total is negative and so is its tender, because the money leaves the drawer. An
    /// earlier revision of this guard rejected every negative amount and broke returns outright.
    /// </summary>
    [Fact]
    public void A_refund_settles_with_negative_tenders()
    {
        var settlement = TenderCalculator.Settle(-22.40m, [Cash(-22.40m, -22.40m)], Penny);

        settlement.IsSuccess.Should().BeTrue();
        settlement.Value.IsSettled.Should().BeTrue();
        settlement.Value.AmountDue.Should().Be(-22.40m);
    }

    [Fact]
    public void Money_coming_in_against_a_refund_is_refused()
    {
        var result = TenderCalculator.Settle(-22.40m, [Cash(22.40m, 22.40m)], Penny);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TenderCalculator.WrongDirection.Code);
    }

    /// <summary>
    /// Not a business limit. A value this size only arrives from a malformed or hostile request, and
    /// the alternative to refusing it is a decimal overflow part-way through a transaction that has
    /// already written rows.
    /// </summary>
    [Fact]
    public void An_absurdly_large_tender_is_refused()
    {
        var result = TenderCalculator.Settle(100m, [Cash(100m, 999_999_999_999m)], Penny);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TenderCalculator.AmountTooLarge.Code);
    }

    /// <summary>
    /// The heart of it. A stated cash amount that does not cover the bill must not come back
    /// settled, however the rest of the request is dressed up to say it does.
    /// <para>
    /// It is reported rather than refused, deliberately: the settlement succeeds and carries
    /// <c>IsSettled = false</c> with the outstanding balance, which is what lets a caller offer to
    /// part-pay. <c>CompleteSaleHandler</c> checks that flag, so the sale still cannot complete.
    /// </para>
    /// </summary>
    [Fact]
    public void Cash_short_of_the_amount_due_does_not_settle()
    {
        var settlement = TenderCalculator.Settle(100m, [Cash(100m, 40m)], Penny);

        settlement.IsSuccess.Should().BeTrue();
        settlement.Value.IsSettled.Should().BeFalse();
        settlement.Value.OutstandingBalance.Should().Be(60m);
    }

    /// <summary>
    /// The exact request the broken browser sent: the amount claimed is right, but nothing was
    /// actually handed over. Zero still means "the exact money" — that is the long-standing
    /// convention for a card leg — so this settles, and the drawer figure is the amount due rather
    /// than a fiction.
    /// </summary>
    [Fact]
    public void Zero_handed_over_still_means_the_exact_money()
    {
        var settlement = TenderCalculator.Settle(100m, [Cash(100m, 0m)], Penny);

        settlement.IsSuccess.Should().BeTrue();
        settlement.Value.IsSettled.Should().BeTrue();
        settlement.Value.ChangeDue.Should().Be(0m);
        settlement.Value.Tenders[0].AmountTendered.Should().Be(100m);
    }

    [Fact]
    public void Exact_payment_settles_with_no_change()
    {
        var settlement = TenderCalculator.Settle(2500m, [Cash(2500m, 2500m)], Penny).Value;

        settlement.IsSettled.Should().BeTrue();
        settlement.ChangeDue.Should().Be(0m);
    }

    [Fact]
    public void Overpayment_returns_the_difference_as_change()
    {
        var settlement = TenderCalculator.Settle(2500m, [Cash(2500m, 5000m)], Penny).Value;

        settlement.IsSettled.Should().BeTrue();
        settlement.ChangeDue.Should().Be(2500m);
    }

    [Fact]
    public void Change_is_never_negative()
    {
        var settlement = TenderCalculator.Settle(100m, [Cash(100m, 100m)], Penny).Value;

        settlement.ChangeDue.Should().BeGreaterThanOrEqualTo(0m);
    }

    /// <summary>A split where the card leg covers part and the cash leg is short still does not settle.</summary>
    [Fact]
    public void A_short_cash_leg_in_a_split_does_not_settle()
    {
        var settlement = TenderCalculator.Settle(100m, [Card(60m), Cash(40m, 10m)], Penny);

        settlement.IsSuccess.Should().BeTrue();
        settlement.Value.IsSettled.Should().BeFalse();
    }

    [Fact]
    public void A_split_that_covers_the_bill_settles()
    {
        var settlement = TenderCalculator.Settle(100m, [Card(60m), Cash(40m, 40m)], Penny);

        settlement.IsSuccess.Should().BeTrue();
        settlement.Value.IsSettled.Should().BeTrue();
    }

    /// <summary>
    /// Two decimal places is the currency's smallest unit; a third is either a tampered request or a
    /// rounding error upstream. It settles, and it settles to the rounded figure rather than
    /// carrying a fraction of a paisa into the ledger.
    /// </summary>
    [Fact]
    public void Excess_decimal_precision_is_rounded_not_carried()
    {
        var settlement = TenderCalculator.Settle(10.005m, [Cash(10.005m, 20m)], Penny).Value;

        settlement.AmountDue.Should().Be(10.01m);
        settlement.Tenders.Sum(t => t.Amount).Should().Be(10.01m);
    }

    [Fact]
    public void Nothing_tendered_against_a_bill_does_not_settle()
    {
        var result = TenderCalculator.Settle(100m, [], Penny);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSettled.Should().BeFalse();
        result.Value.OutstandingBalance.Should().Be(100m);
    }

    private static TenderInputLine Cash(decimal amount, decimal tendered)
        => new(CashTenderId, TenderBehaviour.Cash, RoundsToMinimumTender: true, AllowsOverTender: true, amount, tendered);

    private static TenderInputLine Card(decimal amount)
        => new(CardTenderId, TenderBehaviour.Card, RoundsToMinimumTender: false, AllowsOverTender: false, amount, amount);
}
