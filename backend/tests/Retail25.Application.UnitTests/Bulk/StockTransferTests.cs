using FluentAssertions;
using Retail25.Application.Common;
using Retail25.Application.Inventory;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Inventory;
using Xunit;

namespace Retail25.Application.UnitTests.Bulk;

/// <summary>
/// Moving stock between stores. The invariant that matters throughout: what leaves the source and
/// what arrives at the destination are two separate events, and between them the goods are in
/// neither place.
/// </summary>
public sealed class StockTransferTests
{
    private static async Task<(MastersTestHarness Harness, Guid ToLocationId)> TwoStoresAsync()
    {
        var harness = await MastersTestHarness.CreateAsync();
        var destination = await harness.AddLocationAsync("Second Store", "SND");
        return (harness, destination.Id);
    }

    private static async Task<TransferDto> DraftAsync(
        MastersTestHarness harness, Guid toLocationId, Guid productId, decimal quantity)
    {
        var created = await harness.Transfers.Handle(
            new CreateTransferCommand(harness.Location.Id, toLocationId), CancellationToken.None);

        var withLine = await harness.Transfers.Handle(
            new UpsertTransferLineCommand(created.Value.Id, productId, quantity), CancellationToken.None);

        return withLine.Value;
    }

    [Fact]
    public async Task A_transfer_to_the_same_place_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.Transfers.Handle(
            new CreateTransferCommand(harness.Location.Id, harness.Location.Id), CancellationToken.None);

        result.Error.Should().Be(StockTransfer.SameLocation);
    }

    [Fact]
    public async Task A_transfer_to_a_location_that_does_not_exist_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.Transfers.Handle(
            new CreateTransferCommand(harness.Location.Id, Guid.NewGuid()), CancellationToken.None);

        result.Error.Should().Be(TransferHandlers.LocationNotFound);
    }

    [Fact]
    public async Task Adding_the_same_item_twice_changes_the_quantity_rather_than_adding_a_line()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 5m);

        var updated = await harness.Transfers.Handle(
            new UpsertTransferLineCommand(draft.Id, product.Id, 8m), CancellationToken.None);

        updated.Value.Lines.Should().ContainSingle().Which.Quantity.Should().Be(8m);
    }

    [Fact]
    public async Task An_item_from_another_store_cannot_be_put_on_the_transfer()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var elsewhere = await harness.AddProductAtAsync(to, "A-1", "Widget", onHand: 50m);

        var created = await harness.Transfers.Handle(
            new CreateTransferCommand(harness.Location.Id, to), CancellationToken.None);

        var result = await harness.Transfers.Handle(
            new UpsertTransferLineCommand(created.Value.Id, elsewhere.Id, 1m), CancellationToken.None);

        result.Error.Code.Should().Be(TransferHandlers.ProductNotFound.Code);
    }

    [Fact]
    public async Task An_empty_transfer_cannot_be_shipped()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var created = await harness.Transfers.Handle(
            new CreateTransferCommand(harness.Location.Id, to), CancellationToken.None);

        var result = await harness.Transfers.Handle(new ShipTransferCommand(created.Value.Id), CancellationToken.None);

        result.Error.Should().Be(StockTransfer.NothingToShip);
    }

    [Fact]
    public async Task Shipping_takes_the_stock_off_the_source_and_writes_a_transfer_out()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 12m);

        var shipped = await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        shipped.Value.Status.Should().Be(TransferStatus.InTransit);
        shipped.Value.ShippedAt.Should().NotBeNull();

        harness.Db.Products.Single(p => p.Id == product.Id).OnHand.Should().Be(38m);

        var entry = harness.Db.StockLedgerEntries.Single(e => e.MovementType == MovementType.TransferOut);
        entry.Quantity.Should().Be(-12m);
        entry.LocationId.Should().Be(harness.Location.Id);
        entry.ReferenceId.Should().Be(draft.Id);
    }

    /// <summary>
    /// Every line is checked before any stock moves. Shipping the lines that fit and failing on the
    /// one that does not would leave the van loaded with goods the system says are on the shelf.
    /// </summary>
    [Fact]
    public async Task A_short_line_stops_the_whole_shipment()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var plenty = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var short_ = await harness.AddProductAsync("B-2", "Scarce", onHand: 1m);

        var draft = await DraftAsync(harness, to, plenty.Id, 5m);
        await harness.Transfers.Handle(new UpsertTransferLineCommand(draft.Id, short_.Id, 10m), CancellationToken.None);

        var result = await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        result.Error.Code.Should().Be(TransferHandlers.InsufficientStock.Code);
        harness.Db.Products.Single(p => p.Id == plenty.Id).OnHand.Should().Be(50m);
        harness.Db.StockLedgerEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Receiving_puts_the_stock_on_the_destination_and_closes_the_transfer()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await harness.AddProductAtAsync(to, "A-1", "Widget", onHand: 3m);

        var draft = await DraftAsync(harness, to, source.Id, 12m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        var received = await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        received.Value.Status.Should().Be(TransferStatus.Received);
        received.Value.ReceivedAt.Should().NotBeNull();

        harness.Db.Products.Single(p => p.LocationId == to && p.StockCode == "A-1").OnHand.Should().Be(15m);
        harness.Db.StockLedgerEntries.Single(e => e.MovementType == MovementType.TransferIn).Quantity.Should().Be(12m);
    }

    /// <summary>
    /// The destination's on-order is a real purchase order with a supplier. A transfer arriving must
    /// not work itself off against it.
    /// </summary>
    [Fact]
    public async Task Receiving_does_not_touch_the_destinations_on_order()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var destination = await harness.AddProductAtAsync(to, "A-1", "Widget", onHand: 0m);
        destination.UpdateStockLevels(0m, onOrder: 20m);
        await harness.Db.SaveChangesAsync();

        var draft = await DraftAsync(harness, to, source.Id, 12m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);
        await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        var saved = harness.Db.Products.Single(p => p.Id == destination.Id);
        saved.OnHand.Should().Be(12m);
        saved.OnOrder.Should().Be(20m, because: "a transfer is not a delivery against a purchase order");
    }

    /// <summary>
    /// Products are one row per (location, stock code), so the first time an item is sent somewhere
    /// the destination has no row for it and one has to be made from the source's.
    /// </summary>
    [Fact]
    public async Task An_item_new_to_the_destination_gets_a_row_copied_from_the_source()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var department = await harness.AddDepartmentAsync("Hardware");
        var source = await harness.AddProductAsync("A-1", "Widget", price: 19.99m, onHand: 50m, departmentId: department.Id);
        source.UpdateDetails("Widget", "A useful widget", "0123456789012", "B12", null);
        source.UpdateOrdering(baseStock: 20, reorderPoint: 5, reorderQty: 10, caseQty: 6m, shipWeight: 1.2m);
        await harness.Db.SaveChangesAsync();

        var draft = await DraftAsync(harness, to, source.Id, 4m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);
        await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        var created = harness.Db.Products.Single(p => p.LocationId == to && p.StockCode == "A-1");

        created.Name.Should().Be("Widget");
        created.RegularPrice.Should().Be(19.99m);
        created.DepartmentId.Should().Be(department.Id);
        created.ReorderPoint.Should().Be(5);
        created.CaseQty.Should().Be(6m);
        created.OnHand.Should().Be(4m, because: "the only stock it has is what just arrived");
    }

    /// <summary>
    /// Creating the destination row is a catalogue write. Without that permission the transfer must
    /// stop — otherwise sending an item somewhere would be a way to add items to a catalogue you
    /// have no rights to edit.
    /// </summary>
    [Fact]
    public async Task Creating_the_destination_row_needs_catalogue_write()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, source.Id, 4m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        harness.CurrentUser.Revoke(PermissionKeys.Catalog.Write);

        var result = await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        result.Error.Code.Should().Be(TransferHandlers.CannotCreateAtDestination.Code);
    }

    /// <summary>Receiving into a row that already exists needs no catalogue rights at all.</summary>
    [Fact]
    public async Task Receiving_into_an_existing_row_does_not_need_catalogue_write()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await harness.AddProductAtAsync(to, "A-1", "Widget");

        var draft = await DraftAsync(harness, to, source.Id, 4m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        harness.CurrentUser.Revoke(PermissionKeys.Catalog.Write);

        var result = await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_partial_receipt_leaves_the_rest_in_transit()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await harness.AddProductAtAsync(to, "A-1", "Widget");

        var draft = await DraftAsync(harness, to, source.Id, 10m);
        var shipped = await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);
        var lineId = shipped.Value.Lines.Single().Id;

        var received = await harness.Transfers.Handle(
            new ReceiveTransferCommand(draft.Id, [new ReceiveTransferLine(lineId, 4m)]), CancellationToken.None);

        received.Value.Status.Should().Be(TransferStatus.InTransit);
        received.Value.Lines.Single().Outstanding.Should().Be(6m);
        harness.Db.Products.Single(p => p.LocationId == to).OnHand.Should().Be(4m);
    }

    [Fact]
    public async Task The_rest_of_a_partial_receipt_closes_the_transfer()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await harness.AddProductAtAsync(to, "A-1", "Widget");

        var draft = await DraftAsync(harness, to, source.Id, 10m);
        var shipped = await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);
        var lineId = shipped.Value.Lines.Single().Id;

        await harness.Transfers.Handle(
            new ReceiveTransferCommand(draft.Id, [new ReceiveTransferLine(lineId, 4m)]), CancellationToken.None);

        var final = await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        final.Value.Status.Should().Be(TransferStatus.Received);
        harness.Db.Products.Single(p => p.LocationId == to).OnHand.Should().Be(10m);
    }

    [Fact]
    public async Task Receiving_more_than_was_shipped_is_refused()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await harness.AddProductAtAsync(to, "A-1", "Widget");

        var draft = await DraftAsync(harness, to, source.Id, 10m);
        var shipped = await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);
        var lineId = shipped.Value.Lines.Single().Id;

        var result = await harness.Transfers.Handle(
            new ReceiveTransferCommand(draft.Id, [new ReceiveTransferLine(lineId, 11m)]), CancellationToken.None);

        result.Error.Code.Should().Be(StockTransferLine.OverReceipt.Code);
    }

    /// <summary>
    /// Cost is frozen when the van leaves. A sale at the source afterwards must not change what the
    /// goods already in the van are worth to the destination.
    /// </summary>
    [Fact]
    public async Task The_cost_the_destination_receives_at_is_frozen_when_the_van_leaves()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var source = await harness.AddProductAsync("A-1", "Widget", onHand: 0m);
        source.ReceiveStock(10m, 5m, 0m);
        await harness.Db.SaveChangesAsync();

        await harness.AddProductAtAsync(to, "A-1", "Widget");

        var draft = await DraftAsync(harness, to, source.Id, 4m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        // The source takes a much dearer delivery after the van has gone.
        source.ReceiveStock(100m, 50m, 0m);
        await harness.Db.SaveChangesAsync();

        await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        harness.Db.Products.Single(p => p.LocationId == to).AvgCost.Should().Be(5m);
    }

    [Fact]
    public async Task A_draft_can_be_cancelled()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 5m);

        var result = await harness.Transfers.Handle(new CancelTransferCommand(draft.Id), CancellationToken.None);

        result.Value.Status.Should().Be(TransferStatus.Cancelled);
    }

    /// <summary>
    /// Once the goods are on a motorway the paperwork has to follow them. Cancelling would leave
    /// stock that has left one store and will never arrive at the other.
    /// </summary>
    [Fact]
    public async Task A_shipped_transfer_cannot_be_cancelled()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 5m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        var result = await harness.Transfers.Handle(new CancelTransferCommand(draft.Id), CancellationToken.None);

        result.Error.Should().Be(StockTransfer.NotDraft);
    }

    [Fact]
    public async Task A_shipped_transfer_cannot_have_its_lines_changed()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 5m);
        await harness.Transfers.Handle(new ShipTransferCommand(draft.Id), CancellationToken.None);

        var result = await harness.Transfers.Handle(
            new UpsertTransferLineCommand(draft.Id, product.Id, 9m), CancellationToken.None);

        result.Error.Should().Be(StockTransfer.NotDraft);
    }

    [Fact]
    public async Task An_unshipped_transfer_cannot_be_received()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        var draft = await DraftAsync(harness, to, product.Id, 5m);

        var result = await harness.Transfers.Handle(new ReceiveTransferCommand(draft.Id), CancellationToken.None);

        result.Error.Should().Be(StockTransfer.NotInTransit);
    }

    [Fact]
    public async Task The_browse_shows_both_ends_of_a_transfer()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await DraftAsync(harness, to, product.Id, 5m);

        var outbound = await harness.Transfers.Handle(
            new BrowseTransfersQuery(harness.Location.Id), CancellationToken.None);

        var inbound = await harness.Transfers.Handle(
            new BrowseTransfersQuery(to), CancellationToken.None);

        outbound.Should().ContainSingle();
        inbound.Should().ContainSingle(because: "the receiving store needs to see what is coming");

        var row = inbound.Single();
        row.FromLocationName.Should().Be(harness.Location.Name);
        row.ToLocationName.Should().Be("Second Store");
        row.LineCount.Should().Be(1);
    }

    [Fact]
    public async Task The_receiving_store_can_be_excluded_from_the_browse()
    {
        var (harness, to) = await TwoStoresAsync();
        using var _ = harness;

        var product = await harness.AddProductAsync("A-1", "Widget", onHand: 50m);
        await DraftAsync(harness, to, product.Id, 5m);

        var rows = await harness.Transfers.Handle(
            new BrowseTransfersQuery(to, IncludeInbound: false), CancellationToken.None);

        rows.Should().BeEmpty();
    }
}
