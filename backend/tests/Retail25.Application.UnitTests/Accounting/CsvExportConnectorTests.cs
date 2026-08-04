using FluentAssertions;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Accounting;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Accounting;
using Xunit;

namespace Retail25.Application.UnitTests.Accounting;

/// <summary>
/// The CSV accounting adapter (doc 09 §1). What matters here is that a day's takings balance, that
/// every attempt leaves a trace, and that a failure is reported rather than thrown — accounting sits
/// downstream of selling and must never be able to stop a till.
/// </summary>
public sealed class CsvExportConnectorTests
{
    private static readonly DateOnly Day = new(2026, 7, 15);

    [Fact]
    public async Task A_days_takings_post_as_a_balanced_journal()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        harness.Db.DrawerSessions.Add(new DrawerSession
        {
            StationId = TestIds.Next(),
            LocationId = harness.Location.Id,
            OpenedByStaffId = TestIds.Next(),
            BusinessDate = Day,
            Status = DrawerSessionStatus.Closed,
            NetSales = 1000m,
            Tax1Collected = 50m,
            Tax2Collected = 70m,
            OpenedAt = harness.Clock.Now,
        });

        await harness.Db.SaveChangesAsync();

        var connector = new CsvExportConnector(harness.Db, harness.Clock);
        var result = await connector.PostPosRevenueAsync(harness.Location.Id, Day, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RecordCount.Should().Be(1);

        // 1000 of sales plus 120 of tax is 1120 banked — the debit must equal the credits.
        result.Output.Should().Contain("Bank,1120,0");
        result.Output.Should().Contain("Sales,0,1000");
        result.Output.Should().Contain("Tax 1 collected,0,50");
        result.Output.Should().Contain("Tax 2 collected,0,70");
    }

    [Fact]
    public async Task An_open_drawer_is_not_posted_because_the_day_is_not_finished()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        harness.Db.DrawerSessions.Add(new DrawerSession
        {
            StationId = TestIds.Next(),
            LocationId = harness.Location.Id,
            OpenedByStaffId = TestIds.Next(),
            BusinessDate = Day,
            Status = DrawerSessionStatus.Open,
            NetSales = 500m,
            OpenedAt = harness.Clock.Now,
        });

        await harness.Db.SaveChangesAsync();

        var connector = new CsvExportConnector(harness.Db, harness.Clock);
        var result = await connector.PostPosRevenueAsync(harness.Location.Id, Day, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RecordCount.Should().Be(0);
        result.Output.Should().BeEmpty();
    }

    /// <summary>
    /// The whole reason SyncLog exists: the legacy integration failed silently, and its manual has a
    /// troubleshooting chapter because of it (guide p.111).
    /// </summary>
    [Fact]
    public async Task Every_attempt_leaves_a_log_row_carrying_what_was_sent()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("W-1", "Widget", price: 9.99m);

        var connector = new CsvExportConnector(harness.Db, harness.Clock);
        await connector.PushItemsAsync(new Application.Accounting.SyncScope(harness.Location.Id), CancellationToken.None);

        var log = harness.Db.SyncLogs.Single();
        log.Provider.Should().Be("csv");
        log.Entity.Should().Be("Items");
        log.Direction.Should().Be(SyncDirection.Push);
        log.Status.Should().Be(SyncStatus.Success);
        log.RecordCount.Should().Be(1);
        log.ResponsePayload.Should().Contain("W-1");
    }

    [Fact]
    public async Task A_bill_lists_only_what_was_actually_received()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var supplier = await harness.AddSupplierAsync("Acme Supply", "1");
        var received = await harness.AddProductAsync("W-1", "Widget");
        var awaited = await harness.AddProductAsync("G-1", "Gadget");

        var order = new Domain.Purchasing.PurchaseOrder
        {
            PoNumber = 42,
            LocationId = harness.Location.Id,
            SupplierId = supplier.Id,
            Status = Domain.Purchasing.PurchaseOrderStatus.PartiallyReceived,
        };
        harness.Db.PurchaseOrders.Add(order);

        harness.Db.PurchaseOrderLines.Add(new Domain.Purchasing.PurchaseOrderLine
        {
            PurchaseOrderId = order.Id, ProductId = received.Id, OrderQty = 10m, QtyReceived = 6m, CostEach = 4m,
        });

        harness.Db.PurchaseOrderLines.Add(new Domain.Purchasing.PurchaseOrderLine
        {
            PurchaseOrderId = order.Id, ProductId = awaited.Id, OrderQty = 5m, QtyReceived = 0m, CostEach = 9m,
        });

        await harness.Db.SaveChangesAsync();

        var connector = new CsvExportConnector(harness.Db, harness.Clock);
        var result = await connector.PostBillAsync(order.Id, new DateOnly(2026, 8, 14), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RecordCount.Should().Be(1, "a bill covers goods received, not goods still awaited");
        result.Output.Should().Contain("W-1");
        result.Output.Should().NotContain("G-1");
        result.Output.Should().Contain("2026-08-14");
    }

    /// <summary>A bookkeeping outage must be reportable, never throwable.</summary>
    [Fact]
    public async Task A_failure_is_returned_and_logged_rather_than_thrown()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var connector = new CsvExportConnector(harness.Db, harness.Clock);

        var result = await connector.PostBillAsync(TestIds.Next(), new DateOnly(2026, 8, 1), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();

        var log = harness.Db.SyncLogs.Single();
        log.Status.Should().Be(SyncStatus.Failed);
        log.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Pulling_through_a_file_adapter_is_refused_honestly()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var connector = new CsvExportConnector(harness.Db, harness.Clock);

        var result = await connector.PullCustomersAsync(harness.Location.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("one-way");
    }
}
