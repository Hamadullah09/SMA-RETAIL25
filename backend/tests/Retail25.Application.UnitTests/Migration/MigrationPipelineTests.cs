using System.Text;
using FluentAssertions;
using Retail25.Application.Migration;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Inventory;
using Retail25.Domain.Migration;
using Xunit;

namespace Retail25.Application.UnitTests.Migration;

/// <summary>
/// The pipeline end to end (doc 09 §3): analyze → stage → validate → dry-run → import.
/// <para>
/// Everything here runs against synthetic fixtures built to the documented field orders. That proves
/// the code; it does not prove anyone's actual data, and the exit criterion for Phase 7 stays open
/// until a real extract exists to run through it.
/// </para>
/// </summary>
public sealed class MigrationPipelineTests
{
    /// <summary>The eleven-column inventory export (guide p.28), as a CSV with no header.</summary>
    private const string TwoGoodItems =
        "Columbia polo,POLO01,Clothing,Shirts,L,1,18.50,49.99,12,Acme,AC-99\n" +
        "Work boots,BOOT01,Footwear,Boots,10,1,45.00,99.99,4,Acme,AC-42";

    private static string Base64(string content) => Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

    private static async Task<long> StageAsync(MastersTestHarness harness, string content, string entity = "Inventory")
    {
        var staged = await harness.Migration.Handle(
            new StageMigrationFileCommand(harness.Location.Id, "TSTINV.DBF", entity, Base64(content), IsBase64: true),
            CancellationToken.None);

        staged.IsSuccess.Should().BeTrue(staged.IsFailure ? staged.Error.Code : string.Empty);
        return staged.Value.Id;
    }

    private static async Task<long> StagedAndValidatedAsync(MastersTestHarness harness, string content)
    {
        var id = await StageAsync(harness, content);
        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);
        return id;
    }

    /* ---------------------------------------------------------------------------------------------
     * Analyze and stage
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Staging_holds_every_row_and_profiles_the_file()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StageAsync(harness, TwoGoodItems);

        var batch = await harness.Migration.Handle(new GetMigrationBatchQuery(id), CancellationToken.None);

        batch.Value.RowsStaged.Should().Be(2);
        batch.Value.Stage.Should().Be(MigrationStage.Staged);
        batch.Value.SourceHash.Should().HaveLength(64);

        var analysis = await harness.Migration.Handle(new GetAnalysisQuery(id), CancellationToken.None);

        analysis.Value.RowCount.Should().Be(2);
        analysis.Value.ColumnCount.Should().Be(11);
        analysis.Value.DetectedLayout.Should().Contain("Inventory");
        analysis.Value.GuideReference.Should().Be("guide p.28");

        var stockCode = analysis.Value.Columns.Single(c => c.Name == "StockCode");
        stockCode.Populated.Should().Be(2);
        stockCode.DistinctValues.Should().Be(2);
        stockCode.Samples.Should().Contain("POLO01");
    }

    /// <summary>
    /// The legacy convention the guide documents, and the one thing this reader must not get wrong:
    /// two adjacent commas are an empty field, not nothing at all. Collapsing them shifts every
    /// column after it, which would put a price into a quantity with nothing to notice.
    /// </summary>
    [Fact]
    public async Task A_double_comma_is_an_empty_field_and_does_not_shift_the_columns()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(harness, "Widget,A-1,,,L,1,4.00,9.99,10,,AC-1");

        var rows = await harness.Migration.Handle(
            new BrowseStagingQuery(id, ProblemsOnly: false), CancellationToken.None);

        var row = rows.Value.Single();

        row.Values["Department"].Should().BeNull();
        row.Values["Category"].Should().BeNull();
        row.Values["Size"].Should().Be("L", because: "the columns after the gap must not have shifted");
        row.Values["Price"].Should().Be("9.99");
        row.Values["ReorderNumber"].Should().Be("AC-1");
    }

    [Fact]
    public async Task A_quoted_description_containing_a_comma_survives()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(harness, "\"Polo, short sleeve\",POLO01,Clothing,,,1,18.50,49.99,12,,");

        var rows = await harness.Migration.Handle(
            new BrowseStagingQuery(id, ProblemsOnly: false), CancellationToken.None);

        rows.Value.Single().Values["ItemName"].Should().Be("Polo, short sleeve");
    }

    [Fact]
    public async Task A_dbf_is_recognised_by_its_header_not_its_name()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var table = DbfFixture.Inventory(
            [["Columbia polo", "POLO01", "Clothing", "Shirts", "L", "1", "18.500", "49.99", "12", "Acme", "AC-99"]]);

        var staged = await harness.Migration.Handle(
            new StageMigrationFileCommand(
                harness.Location.Id, "INVENTORY.TXT", "Inventory", Convert.ToBase64String(table), IsBase64: true),
            CancellationToken.None);

        staged.IsSuccess.Should().BeTrue();

        var analysis = await harness.Migration.Handle(new GetAnalysisQuery(staged.Value.Id), CancellationToken.None);

        analysis.Value.Format.Should().Contain("DBF");
        analysis.Value.RowCount.Should().Be(1);
    }

    [Fact]
    public async Task Rows_the_legacy_system_deleted_are_counted_and_kept_out()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var table = DbfFixture.Inventory(
            [
                ["Kept", "A-1", "", "", "", "", "4.00", "9.99", "5", "", ""],
                ["Gone", "B-2", "", "", "", "", "4.00", "9.99", "5", "", ""],
            ],
            deletedRows: [1]);

        var staged = await harness.Migration.Handle(
            new StageMigrationFileCommand(
                harness.Location.Id, "TSTINV.DBF", "Inventory", Convert.ToBase64String(table), IsBase64: true),
            CancellationToken.None);

        staged.Value.RowsStaged.Should().Be(2);
        staged.Value.RowsDeletedInSource.Should().Be(1);

        await harness.Migration.Handle(new ValidateMigrationBatchCommand(staged.Value.Id), CancellationToken.None);
        await harness.Migration.Handle(new DryRunMigrationCommand(staged.Value.Id), CancellationToken.None);

        var imported = await harness.Migration.Handle(
            new ImportMigrationBatchCommand(staged.Value.Id), CancellationToken.None);

        imported.Value.RowsWouldImport.Should().Be(1);
        harness.Db.Products.Should().ContainSingle().Which.StockCode.Should().Be("A-1");
    }

    [Fact]
    public async Task An_unreadable_file_type_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.Migration.Handle(
            new StageMigrationFileCommand(harness.Location.Id, "x.csv", "Payroll", Base64("a,b"), IsBase64: true),
            CancellationToken.None);

        result.Error.Code.Should().Be(MigrationHandlers.UnknownEntity.Code);
    }

    /* ---------------------------------------------------------------------------------------------
     * Validate
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task A_clean_file_validates_with_nothing_blocking()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var batch = await harness.Migration.Handle(new GetMigrationBatchQuery(id), CancellationToken.None);

        batch.Value.Stage.Should().Be(MigrationStage.Validated);
        batch.Value.BlockingErrors.Should().Be(0);
    }

    [Fact]
    public async Task A_row_with_no_stock_code_is_blocked_and_named()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StagedAndValidatedAsync(harness, TwoGoodItems + "\nOrphan,,Clothing,,,1,1.00,2.00,1,,");

        var findings = await harness.Migration.Handle(new GetValidationQuery(id), CancellationToken.None);

        var blocking = findings.Value.Where(f => f.Severity == FindingSeverity.Blocking).ToList();

        blocking.Should().ContainSingle();
        blocking[0].RowNumber.Should().Be(3, because: "every finding has to be addressable to its row");
        blocking[0].Column.Should().Be("StockCode");
        blocking[0].Code.Should().Be("migration.missing_code");
    }

    [Fact]
    public async Task An_unparseable_price_is_blocked_with_the_value_that_caused_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StagedAndValidatedAsync(harness, "Widget,A-1,,,,1,4.00,ninety-nine,10,,");

        var findings = await harness.Migration.Handle(new GetValidationQuery(id), CancellationToken.None);

        var finding = findings.Value.Single(f => f.Code == "migration.unparseable_number");
        finding.Column.Should().Be("Price");
        finding.Value.Should().Be("ninety-nine");
    }

    [Fact]
    public async Task A_duplicate_stock_code_blocks_both_rows()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StagedAndValidatedAsync(
            harness,
            "Widget,A-1,,,,1,4.00,9.99,10,,\nOther widget,A-1,,,,1,5.00,11.99,3,,");

        var findings = await harness.Migration.Handle(new GetValidationQuery(id), CancellationToken.None);

        var duplicates = findings.Value.Where(f => f.Code == "migration.duplicate_key").ToList();

        duplicates.Should().HaveCount(2, because: "the second row is only wrong because of the first");
        duplicates.Select(d => d.RowNumber).Should().BeEquivalentTo([1, 2]);
    }

    /// <summary>Not a fault, but the shape of a cost-and-price-swapped column.</summary>
    [Fact]
    public async Task Cost_above_price_is_a_warning_rather_than_a_block()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StagedAndValidatedAsync(harness, "Widget,A-1,,,,1,99.99,4.00,10,,");

        var batch = await harness.Migration.Handle(new GetMigrationBatchQuery(id), CancellationToken.None);
        var findings = await harness.Migration.Handle(new GetValidationQuery(id), CancellationToken.None);

        batch.Value.BlockingErrors.Should().Be(0);
        findings.Value.Should().Contain(f => f.Code == "migration.cost_above_price");
    }

    [Fact]
    public async Task The_problems_view_shows_only_the_rows_that_need_looking_at()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StagedAndValidatedAsync(harness, TwoGoodItems + "\nOrphan,,,,,1,1.00,2.00,1,,");

        var all = await harness.Migration.Handle(new BrowseStagingQuery(id, ProblemsOnly: false), CancellationToken.None);
        var problems = await harness.Migration.Handle(new BrowseStagingQuery(id), CancellationToken.None);

        all.Value.Should().HaveCount(3);
        problems.Value.Should().ContainSingle().Which.RowNumber.Should().Be(3);
        problems.Value[0].Problems.Should().Contain("stock code");
    }

    /* ---------------------------------------------------------------------------------------------
     * Dry run
     * ------------------------------------------------------------------------------------------- */

    /// <summary>The whole point of a dry run: the figures are real and nothing was written.</summary>
    [Fact]
    public async Task A_dry_run_produces_the_totals_and_writes_nothing()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var report = await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);

        report.Value.RowsWouldImport.Should().Be(2);
        report.Value.Lines.Single(l => l.Measure == "Items").Imported.Should().Be(2);

        // 12 at 18.50 plus 4 at 45.00.
        report.Value.Lines.Single(l => l.Measure.StartsWith("Inventory value", StringComparison.Ordinal))
            .Imported.Should().Be(402.00m);

        harness.Db.Products.Should().BeEmpty();
        harness.Db.StockLedgerEntries.Should().BeEmpty();

        var batch = await harness.Migration.Handle(new GetMigrationBatchQuery(id), CancellationToken.None);
        batch.Value.Stage.Should().Be(MigrationStage.DryRun);
    }

    [Fact]
    public async Task A_dry_run_reconciles_against_the_legacy_figures_when_they_are_given()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var report = await harness.Migration.Handle(
            new DryRunMigrationCommand(id, new LegacyControlTotals(ItemCount: 2, InventoryValue: 402.00m)),
            CancellationToken.None);

        report.Value.Reconciles.Should().BeTrue();
        report.Value.Lines.Should().OnlyContain(l => l.Matches);
    }

    /// <summary>
    /// A variance is the whole reason for the exercise. It has to show as a number, not as a
    /// pass/fail, so whoever is signing off can see how far out it is.
    /// </summary>
    [Fact]
    public async Task A_variance_against_the_legacy_figures_is_reported_with_its_size()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var report = await harness.Migration.Handle(
            new DryRunMigrationCommand(id, new LegacyControlTotals(ItemCount: 3, InventoryValue: 500.00m)),
            CancellationToken.None);

        report.Value.Reconciles.Should().BeFalse();

        var items = report.Value.Lines.Single(l => l.Measure == "Items");
        items.LegacyReported.Should().Be(3);
        items.Variance.Should().Be(-1);
        items.Matches.Should().BeFalse();
    }

    /// <summary>A measure with nothing to compare against must not be counted as reconciling.</summary>
    [Fact]
    public async Task A_measure_with_no_legacy_figure_is_reported_as_imported_only()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var report = await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);

        report.Value.Lines.Should().OnlyContain(l => l.LegacyReported == null);
        report.Value.Lines.Should().OnlyContain(l => l.Variance == null);
    }

    [Fact]
    public async Task A_batch_that_has_not_been_validated_cannot_be_dry_run()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StageAsync(harness, TwoGoodItems);

        var result = await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);

        result.Error.Should().Be(MigrationBatch.NotValidated);
    }

    /* ---------------------------------------------------------------------------------------------
     * Import
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task An_import_creates_the_items_with_their_prices_and_departments()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        var report = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        report.Value.RowsWouldImport.Should().Be(2);

        var polo = harness.Db.Products.Single(p => p.StockCode == "POLO01");
        polo.Name.Should().Be("Columbia polo");
        polo.RegularPrice.Should().Be(49.99m);
        polo.LastCost.Should().Be(18.50m);
        polo.DepartmentId.Should().NotBeNull();

        harness.Db.Departments.Select(d => d.Name).Should().BeEquivalentTo(["Clothing", "Footwear"]);
        harness.Db.Categories.Select(c => c.Name).Should().BeEquivalentTo(["Shirts", "Boots"]);
    }

    /// <summary>
    /// The doc's rule, and the reason it is a rule: opening stock arrives as a ledger entry, so the
    /// ledger is authoritative from row one rather than from the first sale after cutover.
    /// </summary>
    [Fact]
    public async Task Opening_stock_arrives_as_a_ledger_entry_not_a_raw_on_hand_write()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        var entries = harness.Db.StockLedgerEntries.ToList();

        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.MovementType == MovementType.Adjustment);
        entries.Should().OnlyContain(e => e.Reason == "Legacy opening balance");
        entries.Should().OnlyContain(e => e.ReferenceType == nameof(MigrationBatch));

        entries.Single(e => e.Quantity == 12m).UnitCost.Should().Be(18.50m);
        harness.Db.Products.Single(p => p.StockCode == "POLO01").OnHand.Should().Be(12m);
    }

    /// <summary>The doc's precondition, enforced rather than documented.</summary>
    [Fact]
    public async Task An_import_without_a_dry_run_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        var result = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        result.Error.Should().Be(MigrationBatch.DryRunRequired);
        harness.Db.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task An_import_with_blocking_errors_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, "Orphan,,,,,1,1.00,2.00,1,,");

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);

        var result = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        result.Error.Should().Be(MigrationBatch.HasBlockingErrors);
        harness.Db.Products.Should().BeEmpty();
    }

    /// <summary>
    /// Re-validating after a fix has to invalidate the dry run: the figures it produced described a
    /// state that has since been re-examined, and an import must not lean on them.
    /// </summary>
    [Fact]
    public async Task Re_validating_invalidates_the_dry_run()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);

        var result = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        result.Error.Should().Be(MigrationBatch.DryRunRequired);
    }

    [Fact]
    public async Task Importing_twice_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        var again = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        again.Error.Should().Be(MigrationBatch.AlreadyImported);
        harness.Db.Products.Should().HaveCount(2);
    }

    /// <summary>
    /// The point of keying on the legacy identifier: a second attempt after a failed cutover updates
    /// what it already brought across rather than doubling the catalogue.
    /// </summary>
    [Fact]
    public async Task A_re_import_of_the_same_items_does_not_duplicate_them()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var first = await StagedAndValidatedAsync(harness, TwoGoodItems);
        await harness.Migration.Handle(new DryRunMigrationCommand(first), CancellationToken.None);
        await harness.Migration.Handle(new ImportMigrationBatchCommand(first), CancellationToken.None);

        var second = await StagedAndValidatedAsync(harness, TwoGoodItems);
        await harness.Migration.Handle(new DryRunMigrationCommand(second), CancellationToken.None);
        var report = await harness.Migration.Handle(new ImportMigrationBatchCommand(second), CancellationToken.None);

        harness.Db.Products.Should().HaveCount(2);
        report.Value.Lines.Should().Contain(l => l.Measure == "Already imported previously" && l.Imported == 2);
    }

    [Fact]
    public async Task Cancelling_a_batch_clears_its_staging()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StageAsync(harness, TwoGoodItems);

        harness.Db.MigrationStagingRows.Should().NotBeEmpty();

        var cancelled = await harness.Migration.Handle(new CancelMigrationBatchCommand(id), CancellationToken.None);

        cancelled.IsSuccess.Should().BeTrue();
        harness.Db.MigrationStagingRows.Should().BeEmpty();
        harness.Db.MigrationBatches.Single().Stage.Should().Be(MigrationStage.Cancelled);
    }

    [Fact]
    public async Task An_imported_batch_cannot_be_cancelled()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var id = await StagedAndValidatedAsync(harness, TwoGoodItems);

        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        var result = await harness.Migration.Handle(new CancelMigrationBatchCommand(id), CancellationToken.None);

        result.Error.Should().Be(MigrationBatch.AlreadyImported);
    }

    /* ---------------------------------------------------------------------------------------------
     * Clients and suppliers
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Clients_import_with_their_legacy_numbers_kept()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(
            harness,
            "1042,Jane,Roe,Roe Holdings,12 Example St,,Toronto,ON,M5V1A1,416-555-0100,,jane@example.com,Retail,500\n"
            + "1043,Sam,Kerr,,4 Other Rd,,Toronto,ON,M5V2B2,416-555-0101,,,Retail,0",
            "Client");

        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        var report = await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        report.Value.RowsWouldImport.Should().Be(2);

        // The numbers are printed on twenty years of statements, so they come across as they are.
        harness.Db.Customers.Select(c => c.CustomerNumber).Should().BeEquivalentTo([1042L, 1043L]);

        // Only the one with a credit limit gets an account.
        harness.Db.CustomerAccounts.Should().ContainSingle().Which.CreditLimit.Should().Be(500m);
    }

    [Fact]
    public async Task A_client_with_neither_a_surname_nor_a_company_is_blocked()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(harness, "1044,Jane,,,,,,,,,,,,", "Client");
        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);

        var findings = await harness.Migration.Handle(new GetValidationQuery(id), CancellationToken.None);

        findings.Value.Should().Contain(f => f.Code == "migration.missing_name" && f.Severity == FindingSeverity.Blocking);
    }

    [Fact]
    public async Task Suppliers_import_from_the_documented_fifteen_column_layout()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(
            harness,
            "SUP-1,Acme Supplies,Bill,1 Trade Park,,Toronto,ON,M4B1B3,416-555-0200,,bill@acme.example,ACC-1,Net 30,250,Reliable",
            "Supplier");

        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);
        await harness.Migration.Handle(new ImportMigrationBatchCommand(id), CancellationToken.None);

        var supplier = harness.Db.Suppliers.Should().ContainSingle().Subject;
        supplier.Company.Should().Be("Acme Supplies");
        supplier.SupplierNumber.Should().Be("SUP-1");
    }

    /// <summary>
    /// The file types whose importers are not built yet must say so plainly rather than reporting a
    /// successful import of nothing.
    /// </summary>
    [Fact]
    public async Task A_file_type_with_no_importer_yet_stages_and_says_so()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var id = await StageAsync(harness, "A-1,Widget,3,29.97", "RegisterSales");
        await harness.Migration.Handle(new ValidateMigrationBatchCommand(id), CancellationToken.None);

        var report = await harness.Migration.Handle(new DryRunMigrationCommand(id), CancellationToken.None);

        report.Value.RowsWouldImport.Should().Be(0);
        report.Value.Warnings.Should().ContainSingle().Which.Should().Contain("not built yet");
    }
}
