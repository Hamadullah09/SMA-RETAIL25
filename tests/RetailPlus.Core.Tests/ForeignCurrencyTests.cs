using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

public class ForeignCurrencyTests
{
    [Fact]
    public void A_rate_is_units_of_the_currency_per_unit_of_home_currency()
    {
        Assert.Equal(27_777.78m, ForeignCurrency.ToHome(100m, 0.0036m));
        Assert.Equal(72m, ForeignCurrency.ToForeign(20_000m, 0.0036m));
    }

    [Fact]
    public void The_amount_due_in_a_currency_is_rounded_up_so_it_is_always_enough()
    {
        decimal due = ForeignCurrency.ToForeign(1_000m, 0.0036123m);
        Assert.Equal(3.62m, due);
        Assert.True(ForeignCurrency.ToHome(due, 0.0036123m) >= 1_000m);
    }

    [Fact]
    public void Change_for_a_foreign_payment_is_in_home_currency()
    {
        var tender = ForeignCurrency.Pay("US Dollars", TenderKind.Cash, "USD", 0.0036m, 100m, 20_000m);
        Assert.Equal((20_000m, 27_777.78m, 7_777.78m), (tender.Amount, tender.Tendered, tender.Change));
        Assert.Equal(("USD", 100m, 0.0036m), (tender.Currency, tender.ForeignAmount, tender.ExchangeRate));
    }

    [Fact]
    public void A_foreign_payment_below_the_amount_due_pays_part_of_it()
    {
        var tender = ForeignCurrency.Pay("Euros", TenderKind.Cash, "EUR", 0.5m, 10m, 50m);
        Assert.Equal((20m, 20m, 0m), (tender.Amount, tender.Tendered, tender.Change));
    }

    [Fact]
    public void A_rate_must_be_above_zero() => Assert.Throws<ArgumentOutOfRangeException>(() => ForeignCurrency.ToHome(1m, 0m));
}
