using FluentAssertions;
using Retail25.Application.Reports;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Customers;
using Xunit;

namespace Retail25.Application.UnitTests.Reports;

/// <summary>The tax report a filing is built from, and the loyalty activity behind a points query.</summary>
public sealed class FinancialReportTests
{
    private static readonly DateOnly Day1 = new(2026, 7, 1);
    private static readonly DateOnly Whole = new(2026, 7, 31);

    [Fact]
    public async Task Tax_collected_is_bucketed_by_name_and_summed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        await harness.AddSaleAsync(widget, 1m, 100m, 40m, Day1, tax1: 5m, tax2: 7m);
        await harness.AddSaleAsync(widget, 1m, 200m, 80m, Day1, tax1: 10m, tax2: 14m);

        var handlers = new FinancialReportHandlers(harness.Db);
        var result = await handlers.Handle(new GetTaxReportQuery(harness.Location.Id, Day1, Whole), CancellationToken.None);

        var gst = result.Rows.Single(r => r.TaxName == "GST");
        gst.Rate.Should().Be(5m);
        gst.TaxCollected.Should().Be(15m);
        gst.TransactionCount.Should().Be(2);

        var pst = result.Rows.Single(r => r.TaxName == "PST");
        pst.TaxCollected.Should().Be(21m);

        result.TotalTaxCollected.Should().Be(36m);
        result.TotalNetSales.Should().Be(300m);
    }

    /// <summary>
    /// A rate change mid-period is normal, and merging the two would produce a figure that
    /// reconciles against nothing. The rate comes from each sale's own snapshot for exactly this.
    /// </summary>
    [Fact]
    public async Task A_rate_change_inside_the_period_produces_two_buckets_not_one()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        var before = await harness.AddSaleAsync(widget, 1m, 100m, 40m, Day1, tax1: 5m);
        var after = await harness.AddSaleAsync(widget, 1m, 100m, 40m, Day1, tax1: 6m);

        // The second sale was rung after the rate rose to 6%.
        var laterSnapshot = harness.Db.SaleTaxSnapshots.Single(s => s.TransactionId == after.Id);
        laterSnapshot.Tax1Rate = 6m;
        await harness.Db.SaveChangesAsync();

        var handlers = new FinancialReportHandlers(harness.Db);
        var result = await handlers.Handle(new GetTaxReportQuery(harness.Location.Id, Day1, Whole), CancellationToken.None);

        var gstBuckets = result.Rows.Where(r => r.TaxName == "GST").ToList();
        gstBuckets.Should().HaveCount(2);
        gstBuckets.Should().ContainSingle(r => r.Rate == 5m && r.TaxCollected == 5m);
        gstBuckets.Should().ContainSingle(r => r.Rate == 6m && r.TaxCollected == 6m);

        before.Should().NotBeNull();
    }

    [Fact]
    public async Task A_training_sale_is_never_taxed_in_the_report()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var widget = await harness.AddProductAsync("W-1", "Widget");

        await harness.AddSaleAsync(widget, 1m, 100m, 40m, Day1, tax1: 5m);
        await harness.AddSaleAsync(widget, 1m, 999m, 40m, Day1, tax1: 50m, isTraining: true);

        var handlers = new FinancialReportHandlers(harness.Db);
        var result = await handlers.Handle(new GetTaxReportQuery(harness.Location.Id, Day1, Whole), CancellationToken.None);

        result.TotalTaxCollected.Should().Be(5m);
    }

    [Fact]
    public async Task Reward_points_separate_activity_in_the_window_from_the_balance_today()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Jane", "Doe");

        harness.Db.CustomerPricingProfiles.Add(CustomerPricingProfile.Create(customer.Id));
        await harness.Db.SaveChangesAsync();

        var profile = harness.Db.CustomerPricingProfiles.Single(p => p.CustomerId == customer.Id);
        profile.RewardPoints = 500;

        harness.Db.LoyaltyLedgerEntries.Add(new LoyaltyLedgerEntry
        {
            CustomerId = customer.Id,
            EntryType = LoyaltyEntryType.Earned,
            Points = 120,
            OccurredAt = Day1.ToDateTime(new TimeOnly(10, 0)),
        });

        harness.Db.LoyaltyLedgerEntries.Add(new LoyaltyLedgerEntry
        {
            CustomerId = customer.Id,
            EntryType = LoyaltyEntryType.Redeemed,
            Points = -50,
            OccurredAt = Day1.ToDateTime(new TimeOnly(11, 0)),
        });

        await harness.Db.SaveChangesAsync();

        var handlers = new FinancialReportHandlers(harness.Db);
        var result = await handlers.Handle(
            new GetRewardPointsActivityQuery(harness.Location.Id, Day1, Whole),
            CancellationToken.None);

        result.Rows.Should().ContainSingle();
        var row = result.Rows[0];
        row.CustomerName.Should().Be("Jane Doe");
        row.Earned.Should().Be(120);
        row.Redeemed.Should().Be(50);
        row.NetChange.Should().Be(70);
        row.CurrentBalance.Should().Be(500, "the balance is what they actually hold, not the window's net");
    }
}
