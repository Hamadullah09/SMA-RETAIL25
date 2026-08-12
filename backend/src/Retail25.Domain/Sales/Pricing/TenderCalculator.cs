using Retail25.Domain.Common;
using Retail25.Domain.Configuration;

namespace Retail25.Domain.Sales.Pricing;

/// <summary>
/// One leg of an N-way split payment as the cashier entered it (guide p.8–9).
/// </summary>
/// <param name="Amount">How much of the balance this tender settles, in the base currency.</param>
/// <param name="AmountTendered">What the customer physically handed over. Only meaningful for cash.</param>
/// <param name="ExchangeRate">Units of the tender's currency per unit of the base currency (guide p.9).</param>
public sealed record TenderInputLine(
    long TenderTypeId,
    TenderBehaviour Behaviour,
    bool RoundsToMinimumTender,
    bool AllowsOverTender,
    decimal Amount,
    decimal AmountTendered = 0m,
    decimal ExchangeRate = 1m,
    long? CurrencyId = null,
    string? Reference = null,
    string? AuthCode = null,
    string? CardLast4 = null);

public sealed record SettledTender(
    long TenderTypeId,
    TenderBehaviour Behaviour,
    decimal Amount,
    decimal AmountTendered,
    decimal ChangeGiven,
    decimal ExchangeRate,
    long? CurrencyId,
    string? Reference,
    string? AuthCode,
    string? CardLast4);

/// <summary>
/// The outcome of settling a sale. <see cref="RoundingAdjustment"/> is the penny the store gives up
/// or gains when the cash portion is rounded to the smallest coin — it is reported, never hidden.
/// </summary>
public sealed record TenderSettlement(
    IReadOnlyList<SettledTender> Tenders,
    decimal AmountDue,
    decimal CashPortionDue,
    decimal AmountApplied,
    decimal ChangeDue,
    decimal RoundingAdjustment,
    decimal OutstandingBalance)
{
    public bool IsSettled => OutstandingBalance == 0m;
}

/// <summary>
/// Splits a grand total across tenders, rounds the cash leg to the smallest coin in circulation and
/// works out the change (doc 04 §4 steps 8–9, decision P4).
/// <para>
/// Cash rounds; card, gift and on-account settle to the exact cent. That asymmetry is deliberate and
/// is the reason the rounding adjustment is surfaced as its own figure.
/// </para>
/// </summary>
public static class TenderCalculator
{
    public static readonly Error Mismatch = new("tender.mismatch", "The tenders do not add up to the amount due.");
    public static readonly Error OverTenderNotAllowed = new("tender.over_tender_not_allowed", "This tender type does not accept more than the amount due.");

    /// <summary>
    /// A tender pointing the opposite way to the bill: money coming in against a refund, or going
    /// out against a sale. Always malformed, whichever direction the transaction runs.
    /// </summary>
    public static readonly Error WrongDirection = new(
        "tender.wrong_direction",
        "That payment runs the opposite way to the transaction it settles.");

    /// <summary>
    /// The largest single tender this will settle. Not a business limit — a guard against a value
    /// that only arrives through a malformed or hostile request, where the alternative is a decimal
    /// overflow part-way through a transaction that has already written rows.
    /// </summary>
    private const decimal MaxTender = 100_000_000m;

    public static readonly Error AmountTooLarge = new(
        "tender.amount_too_large",
        "That amount is larger than this till will settle.");

    public static Result<TenderSettlement> Settle(
        decimal grandTotal,
        IReadOnlyList<TenderInputLine> tenders,
        MoneyRounding rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);
        tenders ??= [];

        // Validate before any arithmetic.
        //
        // Everything below used to accept whatever it was handed and fall back to a sensible-looking
        // number when the input made no sense — `AmountTendered > 0 ? AmountTendered : Amount` in
        // particular, which turns "nothing was handed over" into "exactly the right money was handed
        // over". That is the shape of the defect that let a cashier type `abc` into the cash field
        // and ring a fully-settled sale with an empty drawer: a falsy value quietly became the
        // amount due, on both sides of the wire.
        //
        // The rule is direction, not sign. A return is a sale run backwards: its grand total is
        // negative and so is every tender on it, because the money leaves the drawer. So "negative
        // is wrong" would be wrong — an earlier revision said exactly that and broke every refund,
        // which is how this comment came to be here. What is always malformed is a tender pointing
        // the opposite way to the bill it settles.
        var isRefund = grandTotal < 0m;

        foreach (var tender in tenders)
        {
            if (isRefund ? tender.Amount > 0m : tender.Amount < 0m)
            {
                return Result.Failure<TenderSettlement>(WrongDirection
                    .With("tenderTypeId", tender.TenderTypeId)
                    .With("amount", tender.Amount)
                    .With("grandTotal", grandTotal));
            }

            if (isRefund ? tender.AmountTendered > 0m : tender.AmountTendered < 0m)
            {
                return Result.Failure<TenderSettlement>(WrongDirection
                    .With("tenderTypeId", tender.TenderTypeId)
                    .With("amountTendered", tender.AmountTendered)
                    .With("grandTotal", grandTotal));
            }

            if (Math.Abs(tender.Amount) > MaxTender || Math.Abs(tender.AmountTendered) > MaxTender)
            {
                return Result.Failure<TenderSettlement>(AmountTooLarge.With("tenderTypeId", tender.TenderTypeId));
            }
        }

        var amountDue = rounding.Round(grandTotal);

        var nonCash = tenders.Where(t => !t.RoundsToMinimumTender).ToList();
        var cash = tenders.Where(t => t.RoundsToMinimumTender).ToList();

        var nonCashApplied = rounding.Round(nonCash.Sum(t => t.Amount));

        foreach (var tender in nonCash.Where(t => !t.AllowsOverTender && t.Amount > amountDue))
        {
            return Result.Failure<TenderSettlement>(OverTenderNotAllowed.With("tenderTypeId", tender.TenderTypeId));
        }

        var settled = new List<SettledTender>(tenders.Count);
        foreach (var tender in nonCash)
        {
            settled.Add(new SettledTender(
                tender.TenderTypeId,
                tender.Behaviour,
                rounding.Round(tender.Amount),
                rounding.Round(tender.Amount),
                0m,
                tender.ExchangeRate,
                tender.CurrencyId,
                tender.Reference,
                tender.AuthCode,
                tender.CardLast4));
        }

        // Whatever the electronic tenders leave behind is the cash portion, and only that portion
        // is rounded to the smallest coin (guide p.84).
        var remaining = rounding.Round(amountDue - nonCashApplied);
        var cashDue = cash.Count > 0 ? rounding.RoundCash(remaining) : remaining;
        var roundingAdjustment = cash.Count > 0 ? rounding.Round(cashDue - remaining) : 0m;

        // Zero means "the exact money was handed over", which is a real thing a till says and is the
        // convention every existing caller uses for a card leg, where "tendered" has no physical
        // meaning. It is a convention, not a fallback: any *stated* amount is now honoured as
        // stated, and a stated amount that does not cover the cash due is refused below rather than
        // being quietly rounded up into a settled sale.
        var cashTendered = rounding.Round(cash.Sum(t => t.AmountTendered > 0m ? t.AmountTendered : t.Amount));

        // A short payment is deliberately *not* an error here. It comes back as a successful
        // settlement carrying IsSettled = false and the outstanding balance, which is what lets a
        // caller offer to part-pay rather than being told only that something was wrong.
        // CompleteSaleHandler refuses the sale on that flag, so a shortfall still cannot complete —
        // it is refused one layer up, by the code that knows whether part-payment is on the table.
        var changeDue = cash.Count > 0 ? rounding.RoundCash(Math.Max(0m, cashTendered - cashDue)) : 0m;

        // Change comes out of the last cash leg so a receipt shows it against the money that was
        // physically handed over.
        for (var i = 0; i < cash.Count; i++)
        {
            var tender = cash[i];
            var tendered = tender.AmountTendered > 0m ? tender.AmountTendered : tender.Amount;
            var isLast = i == cash.Count - 1;

            settled.Add(new SettledTender(
                tender.TenderTypeId,
                tender.Behaviour,
                rounding.Round(isLast ? tendered - changeDue : tendered),
                rounding.Round(tendered),
                isLast ? changeDue : 0m,
                tender.ExchangeRate,
                tender.CurrencyId,
                tender.Reference,
                tender.AuthCode,
                tender.CardLast4));
        }

        var applied = rounding.Round(settled.Sum(t => t.Amount));
        var outstanding = rounding.Round(amountDue + roundingAdjustment - applied);

        return Result.Success(new TenderSettlement(
            settled,
            amountDue,
            cashDue,
            applied,
            changeDue,
            roundingAdjustment,
            outstanding));
    }
}
