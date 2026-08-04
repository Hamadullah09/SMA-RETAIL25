using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Reports;
using Retail25.Application.Sales.Queries;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// What one run of this suite rang through the till, and what the books said beforehand.
/// </summary>
/// <param name="LocationId">The store everything happened at.</param>
/// <param name="CodeA">A £25 item, sold three times over the day.</param>
/// <param name="CodeB">A £10 item, sold four times.</param>
/// <param name="BaselineNet">
/// Net sales already on the books when this suite started. Kept for diagnostics: when a total looks
/// wrong it is the first thing worth knowing, because other suites in this collection trade against
/// the same database on the same day.
/// </param>
/// <param name="Rang">What this suite added: £115.00 across three transactions.</param>
public sealed record ReportScenario(
    long LocationId,
    long StationId,
    string CodeA,
    string CodeB,
    string Suffix,
    decimal BaselineNet,
    decimal Rang);

/// <summary>
/// Phase 6's exit criterion: the reports agree with the trading data, and with each other.
/// <para>
/// A report that runs without throwing is not a working report. The failure that matters is the
/// quiet one — a total that is plausible, presentable and wrong, which a manager then makes a
/// decision on. So every assertion here is a <em>reconciliation</em>: the report's figure against
/// the sales log's figure, or against arithmetic the test did independently.
/// </para>
/// <para>
/// The trading data is rung through the real till — real carts, real pricing, real tenders. Writing
/// rows straight into the tables would test the reports against data the application could never
/// actually produce.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class ReportReconciliationTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly CommerceApiFixture _api;

    public ReportReconciliationTests(CommerceApiFixture api) => _api = api;

    /// <summary>Built once for the whole collection, however many tests ask for it.</summary>
    private Task<ReportScenario> Scenario() => _api.ScenarioAsync(BuildAsync);

    private async Task<ReportScenario> BuildAsync(IServiceScope scope)
    {
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        // What was already on the books. Everything below is measured against this.
        var before = await sender.Send(new SalesAnalysisQuery(location.Id, Today, Today, SalesAnalysisGroupBy.Product));

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var codeA = $"RPT-A-{suffix}";
        var codeB = $"RPT-B-{suffix}";

        // Round prices with tax off, so a discrepancy in a report is the report's and not a rounding
        // argument.
        // The names carry the run's suffix too, not just the stock codes. The reports group by
        // product *name*, and on a shared database a previous run's "Report widget A" is
        // indistinguishable from this one's — which is precisely how this suite first came to
        // reconcile £2,645 against an expected £115.
        await Create(sender, location.Id, codeA, $"Report widget A {suffix}", 25.00m);
        await Create(sender, location.Id, codeB, $"Report widget B {suffix}", 10.00m);

        var cash = await db.TenderTypes.AsNoTracking().FirstAsync(t => t.Behaviour == TenderBehaviour.Cash);

        await Ring(sender, station.Id, cash.Id, (codeA, 2m));
        await Ring(sender, station.Id, cash.Id, (codeB, 3m));
        await Ring(sender, station.Id, cash.Id, (codeA, 1m), (codeB, 1m));

        return new ReportScenario(
            location.Id,
            station.Id,
            codeA,
            codeB,
            suffix,
            BaselineNet: before.GrandNetSales,
            Rang: (2 * 25.00m) + (3 * 10.00m) + 25.00m + 10.00m);
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The headline number. If sales analysis and the sales log disagree, one of the two screens a
    /// manager reconciles the drawer against is lying.
    /// </summary>
    // Needs a database of its own. Both this and the tax-exempt assertion below measure the whole
    // location for the whole day, and that number is only answerable when nothing else has rung a
    // sale into it. On the shared-database fallback a previous run's taxed sale sits in the same
    // window and the two figures differ by exactly its tax � a real difference, correctly reported,
    // about data this test did not create.
    [RequiresIsolatedDatabaseFact]
    public async Task Sales_analysis_reconciles_against_the_sales_log()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var analysis = await sender.Send(
            new SalesAnalysisQuery(scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product));

        var log = await sender.Send(new SalesLogQuery(scenario.LocationId, Today, Today, IncludeVoided: false));

        // Same window, same exclusions, so the totals must be equal — not close, equal. Money does
        // not get to be approximately right.
        //
        // Net sales and the log's grand total coincide only because this suite's items are
        // tax-exempt and undiscounted. That is deliberate: it makes the two queries directly
        // comparable, where a tax rate in between would leave a legitimate difference to argue about
        // and hide a real one.
        analysis.GrandNetSales.Should().Be(log.GrandTotal);

        // And this suite's own two products account for exactly what it rang — the check that
        // catches both queries being wrong in the same direction.
        //
        // Scoped to those two rows rather than measured as a movement in the day's total, because
        // the other suites in this collection trade on the same day against the same database. A
        // delta is only stable if nothing else is selling, and something else is.
        var mine = analysis.Rows
            .Where(r => r.GroupLabel.Contains(scenario.Suffix, StringComparison.Ordinal))
            .Sum(r => r.NetSales);

        mine.Should().Be(scenario.Rang);
    }

    /// <summary>Grouping must not change the total. It is the same money, sliced differently.</summary>
    [RequiresDockerTheory]
    [InlineData(SalesAnalysisGroupBy.Product)]
    [InlineData(SalesAnalysisGroupBy.Department)]
    [InlineData(SalesAnalysisGroupBy.Client)]
    [InlineData(SalesAnalysisGroupBy.Day)]
    [InlineData(SalesAnalysisGroupBy.Week)]
    [InlineData(SalesAnalysisGroupBy.Month)]
    public async Task Every_grouping_sums_to_the_same_total(SalesAnalysisGroupBy groupBy)
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var byProduct = await sender.Send(
            new SalesAnalysisQuery(scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product));

        var grouped = await sender.Send(new SalesAnalysisQuery(scenario.LocationId, Today, Today, groupBy));

        grouped.GrandNetSales.Should().Be(byProduct.GrandNetSales, $"grouping by {groupBy} reorganises the same money");
        grouped.Rows.Sum(r => r.NetSales).Should().Be(grouped.GrandNetSales, "the rows must add up to the total they sit under");
    }

    /// <summary>Top-N filters presentation, not arithmetic — but it must return the largest rows.</summary>
    [RequiresDockerFact]
    public async Task Top_sellers_returns_the_largest_row()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var all = await sender.Send(
            new SalesAnalysisQuery(scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product, SortBy: "NetSales"));

        var top = await sender.Send(new SalesAnalysisQuery(
            scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product, Top: 1, SortBy: "NetSales"));

        top.Rows.Should().ContainSingle();
        top.Rows[0].NetSales.Should().Be(all.Rows.Max(r => r.NetSales), "top 1 is the largest row, not the first one found");
    }

    /// <summary>Cost visibility is enforced by the server, not by hiding a column in the browser.</summary>
    [RequiresDockerFact]
    public async Task Cost_is_withheld_when_asked_for_and_present_on_the_margin_report()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var withoutCost = await sender.Send(new SalesAnalysisQuery(
            scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product, HideCost: true));

        withoutCost.GrandCogs.Should().BeNull("a caller without cost visibility is not sent cost at all");
        withoutCost.Rows.Should().OnlyContain(r => r.Cogs == null);

        var margin = await sender.Send(new MarginAnalysisQuery(
            new SalesAnalysisQuery(scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product)));

        margin.GrandNetSales.Should().Be(withoutCost.GrandNetSales, "the same money, with cost attached");
    }

    /// <summary>
    /// Stock position is derived from on-hand against reorder point, so it must move when stock does.
    /// A report that reads the same before and after a day's trading is decoration.
    /// </summary>
    [RequiresDockerFact]
    public async Task Stock_position_reflects_what_was_sold()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.StockCode == scenario.CodeA);

        // Three of A left the shelf from a standing start of zero. Negative on-hand is the honest
        // answer, and a report that clamped it at zero would hide a real stock problem.
        product.OnHand.Should().Be(-3m);

        var understocked = await sender.Send(
            new GetStockPositionQuery(scenario.LocationId, Only: StockPosition.Understock));

        understocked.Should().Contain(r => r.StockCode == scenario.CodeA);
    }

    /// <summary>A tax report that invents tax on exempt goods is an error that reaches a tax authority.</summary>
    // Whole-location, whole-day, for the same reason as above.
    [RequiresIsolatedDatabaseFact]
    public async Task The_tax_report_collects_nothing_on_exempt_goods()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var tax = await sender.Send(new GetTaxReportQuery(scenario.LocationId, Today, Today));

        tax.Rows.Sum(r => r.TaxCollected).Should().Be(0m, "both fixture items are tax-exempt");
    }

    /// <summary>
    /// Every report has a CSV twin, and it must carry the same figures. An export that quietly
    /// differs from the screen is worse than no export, because it is the copy that gets emailed.
    /// </summary>
    [RequiresDockerFact]
    public async Task The_csv_export_carries_the_same_rows_as_the_screen()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var filter = new SalesAnalysisQuery(scenario.LocationId, Today, Today, SalesAnalysisGroupBy.Product);

        var onScreen = await sender.Send(filter);
        var csv = await sender.Send(new ExportSalesAnalysisQuery(filter));

        csv.Should().NotBeNullOrWhiteSpace();
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCountGreaterThan(1, "a header and at least one row");

        foreach (var row in onScreen.Rows)
        {
            csv.Should().Contain(row.GroupLabel);
        }
    }

    /// <summary>
    /// The remaining reports, against real PostgreSQL. There is no independent figure to reconcile
    /// these against in this fixture; what is asserted is that each executes and returns a
    /// well-formed result, which is what catches an untranslatable LINQ expression or a column that
    /// does not exist — neither of which the in-memory provider would ever reveal.
    /// </summary>
    [RequiresDockerFact]
    public async Task Every_remaining_report_executes_against_real_postgres()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var from = Today.AddDays(-30);

        (await sender.Send(new GetStockValuationQuery(scenario.LocationId))).Should().NotBeNull();
        (await sender.Send(new GetStockValuationDetailQuery(scenario.LocationId, null, 0, 50))).Should().NotBeNull();
        (await sender.Send(new GetOnOrderQuery(scenario.LocationId))).Should().NotBeNull();
        (await sender.Send(new GetStockReceivedQuery(scenario.LocationId, from, Today))).Should().NotBeNull();
        (await sender.Send(new GetRewardPointsActivityQuery(scenario.LocationId, from, Today))).Should().NotBeNull();
        (await sender.Send(new HoursReportQuery(scenario.LocationId, from, Today))).Should().NotBeNull();
        (await sender.Send(new CommissionReportQuery(scenario.LocationId, from, Today))).Should().NotBeNull();
    }

    /// <summary>And every CSV twin renders, for the same reason.</summary>
    [RequiresDockerFact]
    public async Task Every_csv_twin_renders()
    {
        var scenario = await Scenario();

        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var id = scenario.LocationId;
        var from = Today.AddDays(-30);

        // Factories, not started tasks. Building these as `sender.Send(...)` in a collection
        // initialiser starts all eight at once on one scoped DbContext, and EF Core rightly refuses:
        // "a second operation was started on this context instance". They are awaited one at a time.
        var exports = new (string Name, Func<Task<string>> Run)[]
        {
            ("tax", () => sender.Send(new ExportTaxReportQuery(new GetTaxReportQuery(id, Today, Today)))),
            ("stock value", () => sender.Send(new ExportStockValuationQuery(id))),
            ("stock position", () => sender.Send(new ExportStockPositionQuery(new GetStockPositionQuery(id)))),
            ("on order", () => sender.Send(new ExportOnOrderQuery(new GetOnOrderQuery(id)))),
            ("stock received", () => sender.Send(new ExportStockReceivedQuery(new GetStockReceivedQuery(id, from, Today)))),
            ("reward points", () => sender.Send(new ExportRewardPointsActivityQuery(new GetRewardPointsActivityQuery(id, from, Today)))),
            ("hours", () => sender.Send(new ExportHoursReportQuery(new HoursReportQuery(id, from, Today)))),
            ("commissions", () => sender.Send(new ExportCommissionReportQuery(new CommissionReportQuery(id, from, Today)))),
        };

        foreach (var (name, run) in exports)
        {
            var csv = await run();

            // A header line at minimum. An empty file is a broken export, not an empty report — a
            // spreadsheet cannot tell the difference and neither can the person opening it.
            csv.Should().NotBeNullOrWhiteSpace($"the {name} export must produce a file");
            csv.Should().Contain(",", $"the {name} export must be comma-separated");
        }
    }

    // -------------------------------------------------------------------------------------------

    private static async Task Create(ISender sender, long locationId, string stockCode, string name, decimal price)
    {
        var created = await sender.Send(new CreateProductCommand(
            locationId,
            new ProductGeneralSection(stockCode, name, null, ProductType.Standard, null, null, null, null),
            price,
            Tax1Applies: false,
            Tax2Applies: false));

        created.IsSuccess.Should().BeTrue($"'{stockCode}' should be created, but failed with '{created.Error.Code}'");
    }

    private static async Task Ring(ISender sender, long stationId, long tenderTypeId, params (string Code, decimal Qty)[] lines)
    {
        var cart = await sender.Send(new CreateCartCommand(stationId));
        cart.IsSuccess.Should().BeTrue();

        var total = 0m;

        foreach (var (code, qty) in lines)
        {
            var added = await sender.Send(new AddCartLineByIdentifierCommand(cart.Value.Id, code, qty));
            added.IsSuccess.Should().BeTrue($"'{code}' should ring up, but failed with '{added.Error.Code}'");

            total = added.Value.Totals.GrandTotal;
        }

        var sale = await sender.Send(new CompleteSaleCommand(
            cart.Value.Id,
            [new TenderRequest(tenderTypeId, total, total)],
            Guid.NewGuid().ToString("N"),
            PrintReceipt: false));

        sale.IsSuccess.Should().BeTrue($"the sale should complete, but failed with '{sale.Error.Code}'");
    }
}
