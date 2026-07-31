using FluentAssertions;
using Retail25.Application.Inventory;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Inventory;
using Xunit;

namespace Retail25.Application.UnitTests.Bulk;

/// <summary>
/// Counting the shop. The point of separating counting from posting is that a variance gets looked
/// at before it moves anything, so most of these are about what does <em>not</em> happen until post.
/// </summary>
public sealed class StockCountTests
{
    private static async Task<StockCountDto> OpenCountAsync(MastersTestHarness harness, Guid? departmentId = null)
    {
        var started = await harness.StockCounts.Handle(
            new StartStockCountCommand(harness.Location.Id, departmentId), CancellationToken.None);

        return started.Value;
    }

    [Fact]
    public async Task Importing_snapshots_what_the_system_believed_at_the_time()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        result.Value.Imported.Should().Be(1);

        var line = harness.Db.StockCountLines.Single();
        line.CountedQty.Should().Be(8m);
        line.SystemQtyAtCount.Should().Be(10m);
        line.Variance.Should().Be(-2m);
    }

    /// <summary>
    /// The whole reason for the snapshot: a sale between counting and posting must not silently
    /// absorb the variance.
    /// </summary>
    [Fact]
    public async Task A_sale_after_the_count_does_not_change_the_recorded_variance()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        product.UpdateStockLevels(7m, 0m);
        await harness.Db.SaveChangesAsync();

        harness.Db.StockCountLines.Single().SystemQtyAtCount.Should().Be(10m);
        harness.Db.StockCountLines.Single().Variance.Should().Be(-2m);
    }

    [Fact]
    public async Task A_code_that_matches_nothing_is_reported_and_the_rest_still_imports()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m), new CountedItem("NOPE", 3m)]),
            CancellationToken.None);

        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().ContainSingle().Which.Should().Contain("NOPE");
    }

    [Fact]
    public async Task A_file_where_nothing_matches_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("NOPE", 3m)]), CancellationToken.None);

        result.Error.Code.Should().Be(StockCountHandlers.NothingImported.Code);
    }

    /// <summary>Two people counting the same shelf is a correction, not two shelves.</summary>
    [Fact]
    public async Task Counting_an_item_twice_replaces_the_figure()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        var second = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 9m)]), CancellationToken.None);

        second.Value.Imported.Should().Be(0);
        second.Value.Updated.Should().Be(1);
        harness.Db.StockCountLines.Should().ContainSingle().Which.CountedQty.Should().Be(9m);
    }

    [Fact]
    public async Task A_lower_case_code_still_matches()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem(" a-1 ", 8m)]), CancellationToken.None);

        result.Value.Imported.Should().Be(1);
    }

    /// <summary>
    /// A count scoped to one department must not accept a code from another. Otherwise "we are short
    /// six" and "we did not count that aisle" look identical.
    /// </summary>
    [Fact]
    public async Task A_departmental_count_refuses_items_from_elsewhere()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var hardware = await harness.AddDepartmentAsync("Hardware");
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m, departmentId: hardware.Id);
        await harness.AddProductAsync("B-2", "Elsewhere", onHand: 10m);

        var count = await OpenCountAsync(harness, hardware.Id);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m), new CountedItem("B-2", 8m)]),
            CancellationToken.None);

        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().ContainSingle().Which.Should().Contain("B-2");
    }

    [Fact]
    public async Task A_negative_count_is_refused_for_that_line_alone()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);
        await harness.AddProductAsync("B-2", "Gadget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", -1m), new CountedItem("B-2", 5m)]),
            CancellationToken.None);

        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().ContainSingle().Which.Should().Contain("A-1");
    }

    [Fact]
    public async Task Posting_sets_on_hand_to_what_was_counted_and_writes_a_variance_entry()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        var posted = await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        posted.Value.Status.Should().Be(StockCountStatus.Posted);
        posted.Value.PostedAt.Should().NotBeNull();

        harness.Db.Products.Single(p => p.Id == product.Id).OnHand.Should().Be(8m);

        var entry = harness.Db.StockLedgerEntries.Single();
        entry.MovementType.Should().Be(MovementType.CountVariance);
        entry.Quantity.Should().Be(-2m);
        entry.ReferenceId.Should().Be(count.Id);
        entry.Reason.Should().Contain(count.CountNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// On-hand is set to the counted figure, not adjusted by the variance. The count is the
    /// authority on what is on the shelf; adding the difference would re-apply a sale that the
    /// counted figure already accounts for.
    /// </summary>
    [Fact]
    public async Task Posting_sets_rather_than_adjusts()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        // Two sold while the count was being written up.
        product.UpdateStockLevels(8m, 0m);
        await harness.Db.SaveChangesAsync();

        await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        harness.Db.Products.Single(p => p.Id == product.Id).OnHand.Should().Be(8m,
            because: "adjusting by the -2 variance would have taken it to 6");
    }

    /// <summary>
    /// A line that agrees moves nothing. Writing a zero-quantity ledger entry for every item in the
    /// shop would bury the entries that mean something.
    /// </summary>
    [Fact]
    public async Task A_line_that_agrees_writes_no_ledger_entry()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Agrees", onHand: 10m);
        await harness.AddProductAsync("B-2", "Differs", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 10m), new CountedItem("B-2", 7m)]),
            CancellationToken.None);

        await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        harness.Db.StockLedgerEntries.Should().ContainSingle().Which.Quantity.Should().Be(-3m);
    }

    [Fact]
    public async Task Posting_keeps_the_stock_level_row_in_step_with_the_product()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);
        await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        harness.Db.StockLevels.Single(s => s.ProductId == product.Id).OnHand.Should().Be(8m);
    }

    [Fact]
    public async Task An_empty_count_cannot_be_posted()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        result.Error.Should().Be(StockCount.NothingCounted);
    }

    [Fact]
    public async Task A_posted_count_cannot_be_posted_again()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);
        await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        var again = await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        again.Error.Should().Be(StockCount.NotInProgress);
        harness.Db.StockLedgerEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task A_posted_count_cannot_take_more_lines()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);
        await harness.AddProductAsync("B-2", "Gadget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);
        await harness.StockCounts.Handle(new PostStockCountCommand(count.Id), CancellationToken.None);

        var result = await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("B-2", 5m)]), CancellationToken.None);

        result.Error.Should().Be(StockCount.NotInProgress);
    }

    [Fact]
    public async Task A_cancelled_count_moves_no_stock()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        var cancelled = await harness.StockCounts.Handle(new CancelStockCountCommand(count.Id), CancellationToken.None);

        cancelled.Value.Status.Should().Be(StockCountStatus.Cancelled);
        harness.Db.Products.Single(p => p.Id == product.Id).OnHand.Should().Be(10m);
        harness.Db.StockLedgerEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task The_variance_view_hides_the_lines_that_agree()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Agrees", onHand: 10m);
        await harness.AddProductAsync("B-2", "Differs", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 10m), new CountedItem("B-2", 7m)]),
            CancellationToken.None);

        var full = await harness.StockCounts.Handle(new GetStockCountQuery(count.Id), CancellationToken.None);
        var variances = await harness.StockCounts.Handle(
            new GetStockCountQuery(count.Id, VarianceOnly: true), CancellationToken.None);

        full.Value.Lines.Should().HaveCount(2);
        variances.Value.Lines.Should().ContainSingle().Which.StockCode.Should().Be("B-2");

        // The totals count everything either way — they describe the count, not the view of it.
        variances.Value.LineCount.Should().Be(2);
        variances.Value.VarianceCount.Should().Be(1);
    }

    [Fact]
    public async Task The_net_variance_is_valued_at_the_frozen_cost()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        // Costed before stock is put on: the average is a weighted mean over what is already there,
        // so seeding on-hand first would average 4.00 against the ten units already sitting at zero.
        var product = await harness.AddProductAsync("A-1", "Widget");
        product.RecalculateAvgCost(10m, 4m, 0m);
        product.UpdateStockLevels(10m, 0m);
        await harness.Db.SaveChangesAsync();

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 8m)]), CancellationToken.None);

        var dto = await harness.StockCounts.Handle(new GetStockCountQuery(count.Id), CancellationToken.None);

        dto.Value.NetVarianceValue.Should().Be(-8m, because: "two missing at an average cost of 4.00");
    }

    [Fact]
    public async Task The_export_is_the_variance_sheet()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Agrees", onHand: 10m);
        await harness.AddProductAsync("B-2", "Differs", onHand: 10m);

        var count = await OpenCountAsync(harness);
        await harness.StockCounts.Handle(
            new ImportCountLinesCommand(count.Id, [new CountedItem("A-1", 10m), new CountedItem("B-2", 7m)]),
            CancellationToken.None);

        var csv = await harness.StockCounts.Handle(new ExportStockCountQuery(count.Id), CancellationToken.None);

        csv.Value.Should().Contain("B-2").And.NotContain("A-1");
        csv.Value.Should().StartWith("Code,Description,Counted,System,Variance");
    }

    [Fact]
    public async Task Each_count_gets_its_own_number()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var first = await OpenCountAsync(harness);
        var second = await OpenCountAsync(harness);

        second.CountNumber.Should().BeGreaterThan(first.CountNumber);
    }

    /* ---------------------------------------------------------------------------------------------
     * CSV parsing
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public void A_plain_two_column_file_parses()
    {
        var (items, skipped) = StockCountHandlers.ParseCsv("A-1,8\nB-2,3.5");

        skipped.Should().BeEmpty();
        items.Should().HaveCount(2);
        items[0].Should().Be(new CountedItem("A-1", 8m));
        items[1].CountedQty.Should().Be(3.5m);
    }

    /// <summary>A spreadsheet export has a header row and a handheld's does not — both have to work.</summary>
    [Fact]
    public void A_header_row_is_dropped_without_complaint()
    {
        var (items, skipped) = StockCountHandlers.ParseCsv("StockCode,CountedQty\nA-1,8");

        skipped.Should().BeEmpty();
        items.Should().ContainSingle().Which.StockCode.Should().Be("A-1");
    }

    [Fact]
    public void A_quoted_note_containing_a_comma_survives()
    {
        var (items, _) = StockCountHandlers.ParseCsv("A-1,8,\"damaged, front shelf\"");

        items.Should().ContainSingle().Which.Notes.Should().Be("damaged, front shelf");
    }

    [Fact]
    public void A_doubled_quote_inside_a_note_is_one_quote()
    {
        var (items, _) = StockCountHandlers.ParseCsv("A-1,8,\"the \"\"big\"\" box\"");

        items.Should().ContainSingle().Which.Notes.Should().Be("the \"big\" box");
    }

    [Fact]
    public void A_row_with_a_quantity_that_is_not_a_number_is_reported()
    {
        var (items, skipped) = StockCountHandlers.ParseCsv("A-1,8\nB-2,lots");

        items.Should().ContainSingle();
        skipped.Should().ContainSingle().Which.Should().Contain("Line 2");
    }

    [Fact]
    public void A_row_with_only_one_column_is_reported()
    {
        var (items, skipped) = StockCountHandlers.ParseCsv("A-1,8\nB-2");

        items.Should().ContainSingle();
        skipped.Should().ContainSingle().Which.Should().Contain("needs a code and a quantity");
    }

    [Fact]
    public void Windows_line_endings_and_blank_lines_are_handled()
    {
        var (items, skipped) = StockCountHandlers.ParseCsv("A-1,8\r\n\r\nB-2,3\r\n");

        skipped.Should().BeEmpty();
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountCsvCommand(count.Id, "   "), CancellationToken.None);

        result.Error.Should().Be(StockCountHandlers.EmptyFile);
    }

    [Fact]
    public async Task A_csv_import_reports_both_bad_rows_and_unmatched_codes()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget", onHand: 10m);

        var count = await OpenCountAsync(harness);

        var result = await harness.StockCounts.Handle(
            new ImportCountCsvCommand(count.Id, "A-1,8\nB-2,3\nC-3,lots"), CancellationToken.None);

        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().HaveCount(2);
        result.Value.Skipped.Should().Contain(s => s.Contains("Line 3"));
        result.Value.Skipped.Should().Contain(s => s.Contains("B-2"));
    }
}
