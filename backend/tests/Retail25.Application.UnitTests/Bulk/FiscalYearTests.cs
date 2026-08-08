using FluentAssertions;
using Retail25.Application.Inventory;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Inventory;
using Retail25.Domain.Sales;
using Xunit;

namespace Retail25.Application.UnitTests.Bulk;

/// <summary>
/// The year-end close (guide p.29).
/// <para>
/// The thing worth proving over and over is that it destroys nothing: the sale lines and the stock
/// ledger it reads from are exactly as they were afterwards, and every figure it writes is still
/// derivable from them.
/// </para>
/// </summary>
public sealed class FiscalYearTests
{
    /// <summary>The harness clock sits in July 2026, so 2025 is the most recent finished year.</summary>
    private const int ClosableYear = 2025;

    private static async Task<FiscalYearDto> OpenYearAsync(MastersTestHarness harness, int year = ClosableYear)
    {
        var opened = await harness.FiscalYears.Handle(
            new OpenFiscalYearCommand(harness.Location.Id, year), CancellationToken.None);

        opened.IsSuccess.Should().BeTrue(opened.IsFailure ? opened.Error.Code : string.Empty);
        return opened.Value;
    }

    /* ---------------------------------------------------------------------------------------------
     * Opening a year
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task A_calendar_year_runs_from_january_to_december()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var year = await OpenYearAsync(harness);

        year.StartsOn.Should().Be(new DateOnly(ClosableYear, 1, 1));
        year.EndsOn.Should().Be(new DateOnly(ClosableYear, 12, 31));
        year.Status.Should().Be(FiscalYearStatus.Open);
    }

    [Fact]
    public async Task A_year_that_ends_before_it_starts_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.FiscalYears.Handle(
            new OpenFiscalYearCommand(
                harness.Location.Id, 2025, new DateOnly(2025, 6, 1), new DateOnly(2025, 3, 1)),
            CancellationToken.None);

        result.Error.Should().Be(FiscalYear.EndsBeforeItStarts);
    }

    [Fact]
    public async Task Opening_the_same_year_twice_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await OpenYearAsync(harness);

        var again = await harness.FiscalYears.Handle(
            new OpenFiscalYearCommand(harness.Location.Id, ClosableYear), CancellationToken.None);

        again.Error.Code.Should().Be(FiscalYear.Overlaps.Code);
    }

    /// <summary>
    /// A store on a non-calendar year could otherwise open one running April–March and another
    /// running January–December, and a sale in June would belong to both.
    /// </summary>
    [Fact]
    public async Task An_overlapping_period_is_refused_even_under_a_different_year_number()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await OpenYearAsync(harness);

        var overlapping = await harness.FiscalYears.Handle(
            new OpenFiscalYearCommand(
                harness.Location.Id, 2026, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31)),
            CancellationToken.None);

        overlapping.Error.Code.Should().Be(FiscalYear.Overlaps.Code);
    }

    /* ---------------------------------------------------------------------------------------------
     * Closing
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Closing_rolls_the_year_up_by_month_and_item()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.AddSaleAsync(product, 3m, 20m, 8m, new DateOnly(ClosableYear, 3, 28));
        await harness.AddSaleAsync(product, 1m, 20m, 8m, new DateOnly(ClosableYear, 9, 2));

        var closed = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        closed.Value.ArchiveRows.Should().Be(2, because: "March and September are two months");
        closed.Value.NetSales.Should().Be(120m, because: "six units at 20");
        closed.Value.CostOfGoodsSold.Should().Be(48m);
        closed.Value.GrossMargin.Should().Be(72m);
        closed.Value.TransactionsCovered.Should().Be(3);

        var march = harness.Db.SalesHistoryArchives.Single(a => a.Month == 3);
        march.QuantitySold.Should().Be(5m);
        march.NetSales.Should().Be(100m);
        march.TransactionCount.Should().Be(2);
        march.StockCodeSnapshot.Should().Be("A-1");
    }

    /// <summary>The dry run is what this should be driven by the first time — it has to write nothing.</summary>
    [Fact]
    public async Task A_dry_run_reports_the_same_figures_and_writes_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));

        var dry = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id, DryRun: true), CancellationToken.None);

        dry.Value.WasDryRun.Should().BeTrue();
        dry.Value.NetSales.Should().Be(40m);

        harness.Db.SalesHistoryArchives.Should().BeEmpty();
        harness.Db.StockLedgerEntries.Should().BeEmpty();
        harness.Db.FiscalYears.Single().Status.Should().Be(FiscalYearStatus.Open);

        var real = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        real.Value.NetSales.Should().Be(dry.Value.NetSales);
        real.Value.ArchiveRows.Should().Be(dry.Value.ArchiveRows);
    }

    /// <summary>
    /// The checkpoint is a marker, not a movement. Anything other than zero would mean replaying the
    /// ledger no longer produces the on-hand it started from.
    /// </summary>
    [Fact]
    public async Task The_checkpoint_moves_no_stock_and_records_what_was_on_hand()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 42m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 1m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));

        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        var checkpoint = harness.Db.StockLedgerEntries.Single(e => e.MovementType == MovementType.YearEnd);
        checkpoint.Quantity.Should().Be(0m);
        checkpoint.ProductId.Should().Be(product.Id);
        checkpoint.ReferenceId.Should().Be(year.Id);
        checkpoint.Reason.Should().Contain("42");

        // Stamped at the last moment of the year, so it sorts inside the year it belongs to.
        checkpoint.OccurredAt.Year.Should().Be(ClosableYear);
        checkpoint.OccurredAt.Month.Should().Be(12);

        harness.Db.Products.Single().OnHand.Should().Be(42m);
    }

    /// <summary>A void is money that never changed hands. Archiving it bakes a wrong figure in.</summary>
    [Fact]
    public async Task A_voided_sale_is_left_out()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.AddSaleAsync(product, 5m, 20m, 8m, new DateOnly(ClosableYear, 3, 15), status: TransactionStatus.Voided);

        var closed = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        closed.Value.NetSales.Should().Be(40m);
    }

    /// <summary>A practice sale never happened, so it cannot appear in the one place nobody re-derives.</summary>
    [Fact]
    public async Task A_training_sale_is_left_out()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.AddSaleAsync(product, 5m, 20m, 8m, new DateOnly(ClosableYear, 3, 15), isTraining: true);

        var closed = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        closed.Value.NetSales.Should().Be(40m);
    }

    [Fact]
    public async Task A_sale_outside_the_year_is_left_out()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 12, 31));
        await harness.AddSaleAsync(product, 9m, 20m, 8m, new DateOnly(ClosableYear + 1, 1, 1));

        var closed = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        closed.Value.NetSales.Should().Be(40m, because: "the last day is in and the first day of the next is not");
    }

    /// <summary>
    /// Closing a year that has not finished would archive a partial year under a whole year's
    /// heading, and be wrong forever after without anything saying so.
    /// </summary>
    [Fact]
    public async Task A_year_that_has_not_finished_cannot_be_closed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var year = await OpenYearAsync(harness, 2026);

        var result = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        result.Error.Code.Should().Be(FiscalYearHandlers.EndsInTheFuture.Code);
    }

    /// <summary>Out-of-order closes leave a gap nobody notices until a five-year comparison.</summary>
    [Fact]
    public async Task An_earlier_open_year_blocks_a_later_close()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await OpenYearAsync(harness, 2024);
        var later = await OpenYearAsync(harness, 2025);

        var result = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(later.Id), CancellationToken.None);

        result.Error.Code.Should().Be(FiscalYear.EarlierYearStillOpen.Code);
    }

    [Fact]
    public async Task Closing_in_order_works()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var earlier = await OpenYearAsync(harness, 2024);
        var later = await OpenYearAsync(harness, 2025);

        (await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(earlier.Id), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        (await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(later.Id), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Closing_twice_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));

        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        var again = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        again.Error.Code.Should().Be(FiscalYear.AlreadyClosed.Code);
        harness.Db.SalesHistoryArchives.Should().ContainSingle(because: "the figures must not double");
    }

    [Fact]
    public async Task A_quiet_year_closes_with_nothing_in_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var year = await OpenYearAsync(harness);

        var closed = await harness.FiscalYears.Handle(
            new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        closed.IsSuccess.Should().BeTrue();
        closed.Value.ArchiveRows.Should().Be(0);
        harness.Db.FiscalYears.Single().Status.Should().Be(FiscalYearStatus.Closed);
    }

    /// <summary>
    /// The whole claim of this design. The transactions the close read from have to be exactly as
    /// they were, because everything else is derived from them.
    /// </summary>
    [Fact]
    public async Task Closing_destroys_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.AddSaleAsync(product, 1m, 20m, 8m, new DateOnly(ClosableYear, 9, 2));

        var transactionsBefore = harness.Db.SalesTransactions.Count();
        var linesBefore = harness.Db.SaleLines.Count();

        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        harness.Db.SalesTransactions.Count().Should().Be(transactionsBefore);
        harness.Db.SaleLines.Count().Should().Be(linesBefore);
        harness.Db.Products.Single().OnHand.Should().Be(50m);
    }

    /* ---------------------------------------------------------------------------------------------
     * Reopening
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Reopening_drops_the_archive_and_the_checkpoints()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        harness.Db.SalesHistoryArchives.Should().NotBeEmpty();

        var reopened = await harness.FiscalYears.Handle(
            new ReopenFiscalYearCommand(year.Id), CancellationToken.None);

        reopened.Value.Status.Should().Be(FiscalYearStatus.Open);
        reopened.Value.ArchivedRows.Should().Be(0);
        harness.Db.SalesHistoryArchives.Should().BeEmpty();
        harness.Db.StockLedgerEntries.Where(e => e.MovementType == MovementType.YearEnd).Should().BeEmpty();
    }

    /// <summary>Reopening then re-closing has to land on the same figures, not doubled ones.</summary>
    [Fact]
    public async Task Re_closing_after_a_reopen_produces_the_same_figures()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));

        var first = await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);
        await harness.FiscalYears.Handle(new ReopenFiscalYearCommand(year.Id), CancellationToken.None);
        var second = await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        second.Value.NetSales.Should().Be(first.Value.NetSales);
        second.Value.ArchiveRows.Should().Be(first.Value.ArchiveRows);
        harness.Db.SalesHistoryArchives.Should().ContainSingle();
        harness.Db.StockLedgerEntries.Count(e => e.MovementType == MovementType.YearEnd).Should().Be(1);
    }

    /// <summary>Reopening leaves the sales it was derived from alone — that is what makes it safe.</summary>
    [Fact]
    public async Task Reopening_touches_no_sales()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);
        await harness.FiscalYears.Handle(new ReopenFiscalYearCommand(year.Id), CancellationToken.None);

        harness.Db.SalesTransactions.Should().ContainSingle();
        harness.Db.SaleLines.Should().ContainSingle();
    }

    [Fact]
    public async Task An_open_year_cannot_be_reopened()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var year = await OpenYearAsync(harness);

        var result = await harness.FiscalYears.Handle(
            new ReopenFiscalYearCommand(year.Id), CancellationToken.None);

        result.Error.Code.Should().Be(FiscalYear.NotClosed.Code);
    }

    /* ---------------------------------------------------------------------------------------------
     * The archive
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Each_closed_year_keeps_its_own_rows()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);

        var first = await OpenYearAsync(harness, 2024);
        var second = await OpenYearAsync(harness, 2025);

        await harness.AddSaleAsync(product, 1m, 20m, 8m, new DateOnly(2024, 6, 1));
        await harness.AddSaleAsync(product, 4m, 20m, 8m, new DateOnly(2025, 6, 1));

        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(first.Id), CancellationToken.None);
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(second.Id), CancellationToken.None);

        var history = await harness.FiscalYears.Handle(
            new GetSalesHistoryQuery(harness.Location.Id), CancellationToken.None);

        history.Should().HaveCount(2, because: "the legacy close overwrote last year; this one does not");
        history.Single(r => r.Year == 2024).QuantitySold.Should().Be(1m);
        history.Single(r => r.Year == 2025).QuantitySold.Should().Be(4m);
    }

    [Fact]
    public async Task The_history_can_be_narrowed_to_one_year()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);

        var first = await OpenYearAsync(harness, 2024);
        var second = await OpenYearAsync(harness, 2025);

        await harness.AddSaleAsync(product, 1m, 20m, 8m, new DateOnly(2024, 6, 1));
        await harness.AddSaleAsync(product, 4m, 20m, 8m, new DateOnly(2025, 6, 1));

        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(first.Id), CancellationToken.None);
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(second.Id), CancellationToken.None);

        var history = await harness.FiscalYears.Handle(
            new GetSalesHistoryQuery(harness.Location.Id, Year: 2025), CancellationToken.None);

        history.Should().ContainSingle().Which.Year.Should().Be(2025);
    }

    [Fact]
    public async Task The_history_exports_as_a_csv()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        var csv = await harness.FiscalYears.Handle(
            new ExportSalesHistoryQuery(new GetSalesHistoryQuery(harness.Location.Id)), CancellationToken.None);

        csv.Should().StartWith("Year,Month,Code,Description,Quantity,Net sales,Cost,Margin,Transactions");
        csv.Should().Contain("A-1");
        csv.Should().Contain("2025,3");
    }

    [Fact]
    public async Task The_margin_on_an_archive_row_is_sales_less_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        var history = await harness.FiscalYears.Handle(
            new GetSalesHistoryQuery(harness.Location.Id), CancellationToken.None);

        var row = history.Should().ContainSingle().Subject;
        row.NetSales.Should().Be(40m);
        row.CostOfGoodsSold.Should().Be(16m);
        row.GrossMargin.Should().Be(24m);
    }

    [Fact]
    public async Task A_close_records_what_it_archived_on_the_year()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 20m, onHand: 50m);
        var year = await OpenYearAsync(harness);

        await harness.AddSaleAsync(product, 2m, 20m, 8m, new DateOnly(ClosableYear, 3, 14));
        await harness.FiscalYears.Handle(new RunFiscalYearCloseCommand(year.Id), CancellationToken.None);

        var years = await harness.FiscalYears.Handle(
            new ListFiscalYearsQuery(harness.Location.Id), CancellationToken.None);

        var closed = years.Should().ContainSingle().Subject;
        closed.Status.Should().Be(FiscalYearStatus.Closed);
        closed.ClosedAt.Should().NotBeNull();
        closed.ArchivedRows.Should().Be(1);
        closed.ArchivedNetSales.Should().Be(40m);
    }
}
