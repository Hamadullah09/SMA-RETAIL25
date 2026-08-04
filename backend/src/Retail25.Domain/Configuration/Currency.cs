using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Configuration;

/// <summary>How a value is rounded. Mirrors <see cref="MidpointRounding"/> as stored data.</summary>
public enum RoundingMode
{
    /// <summary>0.5 rounds away from zero. The retail convention and the seeded default.</summary>
    AwayFromZero = 0,

    /// <summary>0.5 rounds to the nearest even value ("banker's rounding").</summary>
    ToEven = 1,

    /// <summary>Always toward zero.</summary>
    Down = 2,

    /// <summary>Always away from zero.</summary>
    Up = 3,
}

/// <summary>
/// A currency the business transacts in. The legacy system allowed exchange rates for up to five
/// currencies at the till (user guide p.9, p.17); here the number is unbounded and every rounding
/// rule that affects money is a property of this row rather than a constant in the pricing engine.
/// </summary>
public sealed class Currency : AggregateRoot, IAuditable
{
    public static readonly Error CodeInvalid = new("currency.code_invalid", "A currency code must be three letters.");
    public static readonly Error ScaleInvalid = new("currency.scale_invalid", "A currency scale must be between 0 and 4.");
    public static readonly Error MinimumTenderInvalid = new("currency.minimum_tender_invalid", "The minimum tender must be greater than zero.");

    private Currency()
    {
    }

    /// <summary>ISO 4217 alphabetic code, e.g. <c>CAD</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Symbol { get; private set; } = string.Empty;

    /// <summary>Decimal places shown and tendered. 2 for most currencies, 0 for JPY, 3 for KWD.</summary>
    public int Scale { get; private set; } = 2;

    public RoundingMode Rounding { get; private set; } = RoundingMode.AwayFromZero;

    /// <summary>
    /// Smallest coin in circulation — the legacy "Minimum Tender" setting (user guide p.84).
    /// Cash tenders and change round to a multiple of this; electronic tenders stay exact.
    /// </summary>
    public decimal MinimumTender { get; private set; } = 0.01m;

    /// <summary>True for the location's base currency; ledgers are kept in the base currency.</summary>
    public bool IsBaseCurrency { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Units of this currency per one unit of the base currency. Only meaningful when
    /// <see cref="IsBaseCurrency"/> is false.
    /// </summary>
    public decimal ExchangeRate { get; private set; } = 1m;

    public DateTimeOffset? ExchangeRateUpdatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public MidpointRounding RoundingMidpoint => Rounding switch
    {
        RoundingMode.ToEven => MidpointRounding.ToEven,
        RoundingMode.Down => MidpointRounding.ToZero,
        RoundingMode.Up => MidpointRounding.AwayFromZero,
        _ => MidpointRounding.AwayFromZero,
    };

    public static Result<Currency> Create(
        string code,
        string name,
        string symbol,
        int scale,
        RoundingMode rounding,
        decimal minimumTender,
        bool isBaseCurrency)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsLetter))
        {
            return Result.Failure<Currency>(CodeInvalid.With("value", code));
        }

        if (scale is < 0 or > Money.StorageScale)
        {
            return Result.Failure<Currency>(ScaleInvalid.With("value", scale));
        }

        if (minimumTender <= 0m)
        {
            return Result.Failure<Currency>(MinimumTenderInvalid.With("value", minimumTender));
        }

        return Result.Success(new Currency
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Symbol = symbol,
            Scale = scale,
            Rounding = rounding,
            MinimumTender = minimumTender,
            IsBaseCurrency = isBaseCurrency,
            ExchangeRate = 1m,
        });
    }

    public Result SetExchangeRate(decimal rate, DateTimeOffset asAt)
    {
        if (IsBaseCurrency)
        {
            return rate == 1m
                ? Result.Success()
                : Result.Failure(new Error("currency.base_rate_fixed", "The base currency always has an exchange rate of 1."));
        }

        if (rate <= 0m)
        {
            return Result.Failure(new Error("currency.rate_invalid", "An exchange rate must be greater than zero."));
        }

        ExchangeRate = rate;
        ExchangeRateUpdatedAt = asAt;
        return Result.Success();
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Rounds an amount to this currency's presentation scale and rounding mode.</summary>
    public Money Round(Money amount) => amount.RoundTo(Scale, RoundingMidpoint);

    /// <summary>Rounds a cash amount to the smallest coin in circulation.</summary>
    public Money RoundCash(Money amount) => amount.RoundToNearest(MinimumTender, RoundingMidpoint);

    /// <summary>Converts an amount expressed in this currency into the base currency.</summary>
    public Money ToBase(Money amountInThisCurrency, string baseCurrencyCode)
        => new(amountInThisCurrency.Amount / ExchangeRate, baseCurrencyCode);

    /// <summary>Converts a base-currency amount into this currency.</summary>
    public Money FromBase(Money amountInBase) => new(amountInBase.Amount * ExchangeRate, Code);
}
