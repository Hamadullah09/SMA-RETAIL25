using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>Item barcodes (UPC-A and EAN-13) against a real SQL Server database.</summary>
public sealed class BarcodeTests(LocalDbFixture db) : IClassFixture<LocalDbFixture>
{
    private DataContext Data => new(db.ConnectionString);

    private async Task<Location> LocationAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        await new SetupService(Data).SeedAsync(1);
        return await new SetupService(Data).AddLocationAsync(db.NextLocationCode(), "Barcode test");
    }

    [SkippableFact]
    public async Task Items_are_found_by_their_barcode_in_either_form()
    {
        var location = await LocationAsync();
        var inventory = new InventoryService(Data);
        var code = await inventory.NewInStoreBarcodeAsync();
        var item = await inventory.SaveAsync(new Item { LocationId = location.Id, Plu = "BC-1", Name = "Chocolate", Barcode = code, Price1 = 450m }, null, null, null);

        Assert.Equal(item.Id, (await inventory.FindByCodeAsync(location.Id, code)).Item?.Id);
        Assert.Equal(item.Id, (await inventory.FindByCodeAsync(location.Id, "0" + code)).Item?.Id);
        Assert.Equal(item.Id, (await inventory.SearchAsync(location.Id, code, ItemSearchField.Barcode)).Single().Id);
        Assert.Equal(code, (await inventory.SearchAsync(location.Id, "BC-1")).Single().Barcode);
    }

    [SkippableFact]
    public async Task Wrong_or_repeated_barcodes_are_refused_and_missing_ones_are_given()
    {
        var location = await LocationAsync();
        var inventory = new InventoryService(Data);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            inventory.SaveAsync(new Item { LocationId = location.Id, Plu = "BC-2", Name = "Bad check digit", Barcode = "036000291453" }, null, null, null));
        var first = await inventory.SaveAsync(new Item { LocationId = location.Id, Plu = "BC-3", Name = "First", Barcode = "036000291452" }, null, null, null);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            inventory.SaveAsync(new Item { LocationId = location.Id, Plu = "BC-4", Name = "Same as EAN-13", Barcode = "0036000291452" }, null, null, null));

        var none = await inventory.SaveAsync(new Item { LocationId = location.Id, Plu = "BC-5", Name = "No barcode" }, null, null, null);
        Assert.Equal(1, await inventory.AssignInStoreBarcodesAsync([first.Id, none.Id]));
        var given = (await inventory.GetAsync(none.Id))!.Barcode;
        Assert.True(Upc.IsValid(given));
        Assert.NotNull(Upc.InStoreNumber(given));
        Assert.Equal("036000291452", (await inventory.GetAsync(first.Id))!.Barcode);
        Assert.NotEqual(given, await inventory.NewInStoreBarcodeAsync());
    }
}
