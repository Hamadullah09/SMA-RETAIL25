using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>The exit-gate alarm against a real database: every change of a tag's flag the gate, the till and a sale make.</summary>
public abstract class GateAlarmTests(DatabaseFixture db)
{
    private static int _station = 600;
    private static readonly DateTime T0 = new(2026, 11, 14, 10, 0, 0);
    private DataContext Data => db.Data;

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        await new SetupService(Data).SeedAsync(1);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Gate test");
        var session = new SessionInfo
        {
            StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code,
            UserId = Guid.CreateVersion7(), UserName = "supervisor", AccessLevel = 3,
        };
        return (session, location);
    }

    private async Task<(Item Item, string Epc)> TaggedAsync(Guid locationId, string plu)
    {
        var item = await new InventoryService(Data).SaveAsync(
            new Item { LocationId = locationId, Plu = plu, Name = "Jacket " + plu, Price1 = 49.90m, OnHand = 3, LastCost = 20m, AverageCost = 20m }, null, null, null);
        var epc = $"E2801190{Random.Shared.Next():X8}{Random.Shared.Next(0, 0xFFFF):X4}";
        await new RfidService(Data).RegisterTagAsync(epc, item.Id);
        return (item, epc);
    }

    private static GateRead At(string epc, int station, DateTime at) => new(epc, station, at, "Door reader", 2);

    private async Task<ItemTag> TagAsync(string epc)
    {
        await using var c = db.CreateContext();
        return await c.ItemTags.AsNoTracking().SingleAsync(t => t.Epc == epc);
    }

    private async Task<List<GateAlarm>> AlarmsAsync(string epc)
    {
        await using var c = db.CreateContext();
        return await c.GateAlarms.AsNoTracking().Where(a => a.Epc == epc).ToListAsync();
    }

    private async Task<List<AuditEntry>> AuditAsync(Guid alarmId)
    {
        await using var c = db.CreateContext();
        return await c.AuditEntries.AsNoTracking().Where(a => a.EntityName == nameof(GateAlarm) && a.EntityId == alarmId).OrderBy(a => a.AtUtc).ToListAsync();
    }

    private async Task<Sale> SellAsync(SessionInfo session, Item item, string epc, SaleLineKind kind = SaleLineKind.Sale)
    {
        decimal qty = kind == SaleLineKind.Return ? -1 : 1;
        var draft = new SaleDraft
        {
            Lines = [new SaleDraftLine { ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = 10m,
                Kind = kind, Restock = kind == SaleLineKind.Return, Epcs = [epc] }],
        };
        return await new SalesService(Data).PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, 10m * qty, 10m * qty)], session);
    }

    [SkippableFact]
    public async Task An_item_in_stock_read_at_the_gate_raises_an_alarm_and_gets_flag_3()
    {
        var (session, location) = await SetUpAsync();
        var (item, epc) = await TaggedAsync(location.Id, "GATE-1");
        var gate = new GateAlarmService(Data);

        var result = await gate.ReadAsync(At(epc.ToLowerInvariant(), session.StationNumber, T0));

        Assert.Equal(GateOutcome.NewAlarm, result.Outcome);
        Assert.True(result.Sounds);
        var alarm = Assert.Single(await AlarmsAsync(epc));
        Assert.Equal(result.Alarm!.Id, alarm.Id);
        Assert.Equal((item.Id, location.Id, session.StationNumber, "GATE-1", "Jacket GATE-1", 49.90m), (alarm.ItemId, alarm.LocationId, alarm.StationNumber, alarm.Plu, alarm.ItemName, alarm.Price));
        Assert.Equal((T0, T0, 1, "Door reader", 2), (alarm.RaisedAt, alarm.LastSeenAt, alarm.Passes, alarm.ReaderName, alarm.Antenna));
        Assert.Equal(GateAlarmResolution.Open, alarm.Resolution);
        Assert.Equal(db.Station!.StoreId, alarm.StoreId);
        var tag = await TagAsync(epc);
        Assert.Equal(tag.Id, alarm.TagId);
        Assert.Equal(ItemTagStatus.Unsold, tag.Status);
        Assert.Equal(3, TagFlags.Number(tag.Status));
        Assert.Contains(alarm.Id, (await gate.GetOpenAsync(session.StationNumber)).Select(a => a.Id));
        var audit = Assert.Single(await AuditAsync(alarm.Id));
        Assert.Equal((AuditActions.GateAlarm, GateAlarmService.GateUser), (audit.Action, audit.UserName));
        Assert.Contains(epc, audit.Details, StringComparison.Ordinal);

        // Flag 3 is still the shop's stock: the tagged-item sale limit counts it.
        Assert.Equal(1, await new RfidService(Data).CountShopStockTagsAsync(item.Id));
        Assert.Equal(0, await new RfidService(Data).CountTagsAsync(item.Id, ItemTagStatus.InStock));
    }

    [SkippableFact]
    public async Task A_sold_item_or_an_unknown_tag_raises_no_alarm()
    {
        var (session, location) = await SetUpAsync();
        var (item, epc) = await TaggedAsync(location.Id, "GATE-2");
        await SellAsync(session, item, epc);
        Assert.Equal(ItemTagStatus.Sold, (await TagAsync(epc)).Status);
        var gate = new GateAlarmService(Data);

        var sold = await gate.ReadAsync(At(epc, session.StationNumber, T0));
        Assert.Equal(GateOutcome.Sold, sold.Outcome);
        Assert.False(sold.Sounds);
        Assert.Null(sold.Alarm);
        Assert.Empty(await AlarmsAsync(epc));
        Assert.Equal(ItemTagStatus.Sold, (await TagAsync(epc)).Status);

        var unknown = $"E2009999{Random.Shared.Next():X8}0000";
        var none = await gate.ReadAsync(At(unknown, session.StationNumber, T0));
        Assert.Equal(GateOutcome.Unknown, none.Outcome);
        Assert.False(none.Sounds);
        Assert.Empty(await AlarmsAsync(unknown));
    }

    [SkippableFact]
    public async Task Reads_of_one_pass_are_one_and_an_open_alarm_is_never_raised_twice()
    {
        var (session, location) = await SetUpAsync();
        var (_, epc) = await TaggedAsync(location.Id, "GATE-3");
        var gate = new GateAlarmService(Data, TimeSpan.FromSeconds(10));

        Assert.Equal(GateOutcome.NewAlarm, (await gate.ReadAsync(At(epc, session.StationNumber, T0))).Outcome);
        // The gate antenna reads the tag again and again while it passes.
        Assert.Equal(GateOutcome.Repeated, (await gate.ReadAsync(At(epc, session.StationNumber, T0.AddSeconds(2)))).Outcome);
        Assert.Equal(GateOutcome.Repeated, (await gate.ReadAsync(At(epc, session.StationNumber, T0.AddSeconds(8)))).Outcome);
        var first = Assert.Single(await AlarmsAsync(epc));
        Assert.Equal(1, first.Passes);

        // A new pass later, with the alarm still open: the alarm comes back and sounds, but there is no second record.
        var again = await gate.ReadAsync(At(epc, session.StationNumber, T0.AddMinutes(2)));
        Assert.Equal(GateOutcome.OpenAlarm, again.Outcome);
        Assert.True(again.Sounds);
        Assert.Equal(first.Id, again.Alarm!.Id);
        var alarm = Assert.Single(await AlarmsAsync(epc));
        Assert.Equal((2, T0.AddMinutes(2), T0), (alarm.Passes, alarm.LastSeenAt, alarm.RaisedAt));
        Assert.Single(await AuditAsync(alarm.Id));
        Assert.Equal(ItemTagStatus.Unsold, (await TagAsync(epc)).Status);

        // Another program at the same database (a second service) also finds the open alarm.
        var other = await new GateAlarmService(Data).ReadAsync(At(epc, session.StationNumber, T0.AddMinutes(3)));
        Assert.Equal(GateOutcome.OpenAlarm, other.Outcome);
        Assert.Single(await AlarmsAsync(epc));
    }

    [SkippableFact]
    public async Task Back_in_stock_gives_flag_1_and_left_the_store_makes_the_tag_lost()
    {
        var (session, location) = await SetUpAsync();
        var (_, back) = await TaggedAsync(location.Id, "GATE-4");
        var (_, gone) = await TaggedAsync(location.Id, "GATE-5");
        var gate = new GateAlarmService(Data);
        var a = (await gate.ReadAsync(At(back, session.StationNumber, T0))).Alarm!;
        var b = (await gate.ReadAsync(At(gone, session.StationNumber, T0.AddSeconds(1)))).Alarm!;
        Assert.Equal([a.Id, b.Id], (await gate.GetOpenAsync(session.StationNumber)).Select(x => x.Id));

        var resolved = await gate.ResolveAsync(a.Id, GateAlarmResolution.BackInStock, session);
        Assert.Equal(GateAlarmResolution.BackInStock, resolved.Resolution);
        Assert.Equal(ItemTagStatus.InStock, (await TagAsync(back)).Status);
        await gate.ResolveAsync(b.Id, GateAlarmResolution.LeftStore, session);
        Assert.Equal(ItemTagStatus.Lost, (await TagAsync(gone)).Status);
        Assert.Empty(await gate.GetOpenAsync(session.StationNumber));

        var stored = Assert.Single(await AlarmsAsync(back));
        Assert.Equal(("supervisor", session.UserId), (stored.ResolvedBy, stored.ResolvedById));
        Assert.NotNull(stored.ResolvedAt);
        var audit = await AuditAsync(a.Id);
        Assert.Equal([AuditActions.GateAlarm, AuditActions.GateAlarmResolved], audit.Select(x => x.Action));
        Assert.Equal(("supervisor", session.UserId), (audit[1].UserName, audit[1].UserId));

        // Resolving twice is refused; so is a resolution the till does not offer.
        await Assert.ThrowsAsync<BusinessRuleException>(() => gate.ResolveAsync(a.Id, GateAlarmResolution.LeftStore, session));
        var (_, third) = await TaggedAsync(location.Id, "GATE-6");
        var c = (await gate.ReadAsync(At(third, session.StationNumber, T0))).Alarm!;
        await Assert.ThrowsAsync<BusinessRuleException>(() => gate.ResolveAsync(c.Id, GateAlarmResolution.SoldAfterwards, session));

        // A piece back in stock sounds again on its next pass; a lost one does not.
        Assert.Equal(GateOutcome.NewAlarm, (await gate.ReadAsync(At(back, session.StationNumber, T0.AddMinutes(5)))).Outcome);
        Assert.Equal(2, (await AlarmsAsync(back)).Count);
        Assert.Equal(GateOutcome.Lost, (await gate.ReadAsync(At(gone, session.StationNumber, T0.AddMinutes(5)))).Outcome);
        Assert.Single(await AlarmsAsync(gone));
    }

    [SkippableFact]
    public async Task Selling_an_item_with_flag_3_resolves_its_alarm_and_a_return_gives_flag_1()
    {
        var (session, location) = await SetUpAsync();
        var (item, epc) = await TaggedAsync(location.Id, "GATE-7");
        var gate = new GateAlarmService(Data);
        var alarm = (await gate.ReadAsync(At(epc, session.StationNumber, T0))).Alarm!;

        // The customer comes back and pays.
        session.UserName = "cashier";
        var sale = await SellAsync(session, item, epc);
        var tag = await TagAsync(epc);
        Assert.Equal((ItemTagStatus.Sold, sale.InvoiceNumber), (tag.Status, tag.SoldOnInvoice));
        var after = Assert.Single(await AlarmsAsync(epc));
        Assert.Equal(alarm.Id, after.Id);
        Assert.Equal((GateAlarmResolution.SoldAfterwards, sale.InvoiceNumber, "cashier"), (after.Resolution, after.SoldOnInvoice, after.ResolvedBy));
        Assert.Empty(await gate.GetOpenAsync(session.StationNumber));
        Assert.Equal([AuditActions.GateAlarm, AuditActions.GateAlarmResolved], (await AuditAsync(alarm.Id)).Select(x => x.Action));
        // Now sold, it goes out without an alarm.
        Assert.Equal(GateOutcome.Sold, (await gate.ReadAsync(At(epc, session.StationNumber, T0.AddMinutes(10)))).Outcome);

        // Brought back for a refund: flag 1 again, and the old alarm stays resolved.
        await SellAsync(session, item, epc, SaleLineKind.Return);
        Assert.Equal(ItemTagStatus.InStock, (await TagAsync(epc)).Status);
        Assert.Equal(GateAlarmResolution.SoldAfterwards, Assert.Single(await AlarmsAsync(epc)).Resolution);
    }
}

public sealed class GateAlarmTestsOnSqlite(SqliteFixture db) : GateAlarmTests(db), IClassFixture<SqliteFixture>;

public sealed class GateAlarmTestsOnSqlServer(SqlServerFixture db) : GateAlarmTests(db), IClassFixture<SqlServerFixture>;
