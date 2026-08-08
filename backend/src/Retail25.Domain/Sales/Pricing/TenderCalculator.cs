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

    public static Result<TenderSettlement> Settle(
        decimal grandTotal,
        IReadOnlyList<TenderInputLine> tenders,
        MoneyRounding rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);
        tenders ??= [];

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

        var cashTendered = rounding.Round(cash.Sum(t => t.AmountTendered > 0m ? t.AmountTendered : t.Amount));
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
