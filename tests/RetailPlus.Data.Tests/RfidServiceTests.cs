using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>RFID readers, antenna assignment and tags against a real SQL Server database.</summary>
public sealed class RfidServiceTests(LocalDbFixture db) : IClassFixture<LocalDbFixture>
{
    private static int _station = 300;
    private DataContext Data => new(db.ConnectionString);

    private async Task<(SessionInfo Session, Location Location)> SetUpAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        await new SetupService(Data).SeedAsync(1);
        var location = await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "RFID test");
        return (new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 }, location);
    }

    private async Task<Item> ItemAsync(int locationId, string plu, decimal onHand = 10) =>
        await new InventoryService(Data).SaveAsync(new Item { LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = 10m, OnHand = onHand, LastCost = 4m, AverageCost = 4m }, null, null, null);

    private static string UniqueHost() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    [SkippableFact]
    public async Task New_reader_gets_four_antennas_and_a_unique_address()
    {
        await SetUpAsync();
        var rfid = new RfidService(Data);
        var host = UniqueHost();
        var reader = RfidService.NewReader("Front " + host);
        reader.Host = host;
        reader.Antennas.RemoveAt(3);
        var saved = await rfid.SaveReaderAsync(reader);
        var loaded = await rfid.GetReaderAsync(saved.Id);
        Assert.Equal([1, 2, 3, 4], loaded!.Antennas.Select(a => a.Port));

        var twin = RfidService.NewReader("Twin " + host);
        twin.Host = host;
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => rfid.SaveReaderAsync(twin));
        Assert.Contains("own IP address", ex.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Station_assignments_are_saved_and_listed()
    {
        var (session, _) = await SetUpAsync();
        var rfid = new RfidService(Data);
        var reader = RfidService.NewReader("Counter " + session.StationNumber);
        reader.Host = UniqueHost();
        var saved = await rfid.SaveReaderAsync(reader);
        var rows = (await rfid.GetAntennasAsync()).Where(a => a.ReaderId == saved.Id).ToList();
        await rfid.SaveAssignmentsAsync(rows.Select(r => r.Port <= 2 ? r with { StationNumber = session.StationNumber, IsEnabled = true, PowerDbm = 25 } : r));

        var mine = await rfid.GetAntennasAsync(session.StationNumber);
        Assert.Equal([1, 2], mine.Select(a => a.Port));
        Assert.All(mine, a => Assert.Equal(25, a.PowerDbm));
        var hubConfig = await rfid.GetReadersAsync("main");
        Assert.Contains(hubConfig, r => r.Id == saved.Id && r.Antennas.Count(a => a.StationNumber == session.StationNumber) == 2);
    }

    [SkippableFact]
    public async Task Tags_resolve_by_registration_gtin_and_item_code()
    {
        var (_, location) = await SetUpAsync();
        var rfid = new RfidService(Data);
        var registered = await ItemAsync(location.Id, "TAGGED1");
        var gtinItem = await ItemAsync(location.Id, "0614141123452");
        var asciiItem = await ItemAsync(location.Id, "SKU-123");
        var epc = $"E2000017{Random.Shared.Next():X8}AABB";

        await rfid.RegisterTagAsync(epc.ToLowerInvariant(), registered.Id);
        Assert.Equal(TagMatch.Registered, (await rfid.ResolveAsync(location.Id, epc))!.Match);
        Assert.Equal(registered.Id, (await rfid.ResolveAsync(location.Id, epc))!.Item.Id);

        // SGTIN-96 for GTIN 00614141123452 (indicator 0, company 0614141, item 12345, serial 1).
        var sgtin = EncodeSgtin96(614141, 7, 12345, 1);
        Assert.True(Epc.TryDecodeSgtin96(sgtin, out var gtin, out _));
        Assert.Equal("00614141123452", gtin);
        var byGtin = await rfid.ResolveAsync(location.Id, sgtin);
        Assert.Equal(gtinItem.Id, byGtin!.Item.Id);
        Assert.Equal(TagMatch.Gtin, byGtin.Match);

        var ascii = Convert.ToHexString("SKU-123"u8) + "0000000000";
        Assert.Equal(asciiItem.Id, (await rfid.ResolveAsync(location.Id, ascii))!.Item.Id);
        Assert.Null(await rfid.ResolveAsync(location.Id, "E2001234567890ABCDEF0000"));
    }

    /// <summary>Builds an SGTIN-96 EPC (filter 1, partition 5: 7-digit company prefix, 6-digit item reference).</summary>
    private static string EncodeSgtin96(long company, int companyDigits, long item, long serial)
    {
        Assert.Equal(7, companyDigits);
        var bits = new System.Text.StringBuilder();
        void Add(long value, int width) => bits.Append(Convert.ToString(value, 2).PadLeft(width, '0'));
        Add(0x30, 8);
        Add(1, 3);
        Add(5, 3);
        Add(company, 24);
        Add(item, 20);
        Add(serial, 38);
        var b = bits.ToString();
        var hex = new System.Text.StringBuilder();
        for (int i = 0; i < 96; i += 4) hex.Append(Convert.ToInt32(b.Substring(i, 4), 2).ToString("X", System.Globalization.CultureInfo.InvariantCulture));
        return hex.ToString();
    }

    [SkippableFact]
    public async Task A_tag_belongs_to_one_item_unless_moved()
    {
        var (_, location) = await SetUpAsync();
        var rfid = new RfidService(Data);
        var a = await ItemAsync(location.Id, "MOVE-A");
        var b = await ItemAsync(location.Id, "MOVE-B");
        var epc = $"E2000099{Random.Shared.Next():X8}0001";
        await rfid.RegisterTagAsync(epc, a.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => rfid.RegisterTagAsync(epc, b.Id));
        await rfid.RegisterTagAsync(epc, b.Id, move: true);
        Assert.Single(await rfid.GetTagsAsync(b.Id));
        Assert.Empty(await rfid.GetTagsAsync(a.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => rfid.RegisterTagAsync("not hex", a.Id));
    }

    [SkippableFact]
    public async Task Selling_a_tagged_item_marks_the_tag_sold_and_a_void_puts_it_back()
    {
        var (session, location) = await SetUpAsync();
        var rfid = new RfidService(Data);
        var item = await ItemAsync(location.Id, "SOLD-1");
        var epc = $"E2000055{Random.Shared.Next():X8}0001";
        await rfid.RegisterTagAsync(epc, item.Id);
        var sales = new SalesService(Data);
        var draft = new SaleDraft
        {
            Lines = [new SaleDraftLine { ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = 1, UnitPrice = 10m, Epcs = [epc] }],
        };
        var sale = await sales.PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, 10m, 10m)], session);

        var tag = (await rfid.GetTagsAsync(item.Id)).Single();
        Assert.Equal(ItemTagStatus.Sold, tag.Status);
        Assert.Equal(sale.InvoiceNumber, tag.SoldOnInvoice);
        Assert.Equal(epc, (await sales.GetSaleAsync(sale.Id))!.Lines.Single().Tags.Single().Epc);

        await sales.VoidSaleAsync(sale.Id, session);
        Assert.Equal(ItemTagStatus.InStock, (await rfid.GetTagsAsync(item.Id)).Single().Status);
    }

    [SkippableFact]
    public async Task One_line_of_several_tagged_pieces_sells_and_voids_every_tag()
    {
        var (session, location) = await SetUpAsync();
        var rfid = new RfidService(Data);
        var item = await ItemAsync(location.Id, "CHOC-7", onHand: 7);
        var epcs = Enumerable.Range(1, 3).Select(n => $"E2000077{Random.Shared.Next():X8}{n:X4}").ToList();
        foreach (var e in epcs) await rfid.RegisterTagAsync(e, item.Id);
        var sales = new SalesService(Data);
        var draft = new SaleDraft
        {
            Lines = [new SaleDraftLine { ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = 3, UnitPrice = 10m, Epcs = [.. epcs] }],
        };
        var sale = await sales.PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, 30m, 30m)], session);

        var line = (await sales.GetSaleAsync(sale.Id))!.Lines.Single();
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(epcs.Order(), line.Tags.Select(t => t.Epc).Order());
        Assert.All(await rfid.GetTagsAsync(item.Id), t => Assert.Equal(ItemTagStatus.Sold, t.Status));
        Assert.Equal(4m, (await new InventoryService(Data).GetAsync(item.Id))!.OnHand);
        var row = (await new InventoryService(Data).SearchAsync(location.Id, epcs[1], ItemSearchField.Epc)).Single();
        Assert.Equal(item.Id, row.Id);
        Assert.Equal(3, row.Tags);

        await sales.VoidSaleAsync(sale.Id, session);
        Assert.All(await rfid.GetTagsAsync(item.Id), t => Assert.Equal(ItemTagStatus.InStock, t.Status));
        Assert.Equal(7m, (await new InventoryService(Data).GetAsync(item.Id))!.OnHand);
    }

    [SkippableFact]
    public async Task Item_weight_is_saved()
    {
        var (_, location) = await SetUpAsync();
        var item = await new InventoryService(Data).SaveAsync(new Item { LocationId = location.Id, Plu = "WEIGHT-1", Name = "Chocolate", Weight = 90m, WeightUnit = "g" }, null, null, null);
        var back = (await new InventoryService(Data).GetAsync(item.Id))!;
        Assert.Equal(90m, back.Weight);
        Assert.Equal("g", back.WeightUnit);
        Assert.Equal("90 g", (await new InventoryService(Data).SearchAsync(location.Id, "WEIGHT-1")).Single().WeightText);
    }
}
