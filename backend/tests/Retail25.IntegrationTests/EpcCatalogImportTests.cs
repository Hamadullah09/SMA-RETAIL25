using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The tag export, imported, and then a tag off it waved at a till.
/// <para>
/// The file under test is the real one, byte for byte — the annotation row, the half-renamed name
/// column, the eleven EPCs that are not hexadecimal. The parser is covered by unit tests; what this
/// covers is the part unit tests cannot reach: that items get database-assigned ids before the tags
/// that reference them are built, that a re-import adds nothing, and that a tag which went in
/// through the importer sells through the same path as one commissioned at goods-in.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class EpcCatalogImportTests
{
    /// <summary>Counted from the file: 213 rows, 11 of them carrying a leading G where an E belongs.</summary>
    private const int ValidTags = 202;

    private const int StockCodes = 33;

    private readonly CommerceApiFixture _api;

    public EpcCatalogImportTests(CommerceApiFixture api) => _api = api;

    private static string Export() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "epc-catalog-export.csv"));

    [RequiresDockerFact]
    public async Task The_export_lands_as_items_and_tags_and_a_second_run_adds_nothing()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await Location(db);
        var csv = Export();

        // A dry run first, because that is how anyone sane approaches a file somebody has been
        // editing by hand. It must report the same numbers and write none of them.
        var before = await db.SerializedUnits.CountAsync();

        var rehearsal = await Ok(sender.Send(new ImportEpcCatalogCommand(location, csv, DryRun: true)));

        rehearsal.RowsRead.Should().Be(213);
        rehearsal.TagsCreated.Should().Be(ValidTags);
        rehearsal.ProductsCreated.Should().Be(StockCodes);

        var afterRehearsal = await db.SerializedUnits.CountAsync();
        afterRehearsal.Should().Be(before, "a dry run writes nothing");

        var imported = await Ok(sender.Send(new ImportEpcCatalogCommand(location, csv)));

        imported.TagsCreated.Should().Be(ValidTags);
        imported.ProductsCreated.Should().Be(StockCodes);
        imported.StockCodes.Should().Contain(["RF-KEYB", "RF-MOUS", "11111", "11130"]);

        // The eleven rejected rows, each one reported with the value that was wrong with it.
        imported.Problems.Where(p => p.RowDropped).Should().HaveCount(11)
            .And.OnlyContain(p => p.Reason == "epc.invalid_characters");

        // Nothing points at item zero. This is the failure the integer-key conversion kept
        // producing: children built from a parent's id before the database had assigned one.
        var productIds = await db.Products.Where(p => p.LocationId == location).Select(p => p.Id).ToListAsync();
        var orphans = await db.SerializedUnits
            .CountAsync(u => u.LocationId == location && !productIds.Contains(u.ProductId));

        orphans.Should().Be(0);

        // Every tag arrives sellable rather than in the state the export happened to be left in.
        var states = await db.SerializedUnits.Where(u => u.LocationId == location)
            .GroupBy(u => u.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        states.Single(s => s.Key == SerializedUnitState.InStock).Count.Should().BeGreaterThanOrEqualTo(ValidTags);

        // Run it again. A file that gets imported twice — by two people, or by one person unsure
        // whether the first attempt took — must not double the shop's stock.
        var again = await Ok(sender.Send(new ImportEpcCatalogCommand(location, csv)));

        again.TagsCreated.Should().Be(0);
        again.ProductsCreated.Should().Be(0);
        again.TagsAlreadyMapped.Should().Be(ValidTags);
    }

    /// <summary>
    /// The point of the whole exercise: a tag from the spreadsheet, waved at a reader, prices itself
    /// onto the sale under the name the spreadsheet gave it.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_tag_from_the_export_sells_at_the_till()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await Location(db);
        var station = await db.Stations.AsNoTracking().FirstAsync();
        _api.ActingUser.StationId = station.Id;

        await sender.Send(new ImportEpcCatalogCommand(location, Export()));

        // The keyboard, renamed by hand in column B and priced at 34.99 in the product half.
        var keyboard = await db.Products.AsNoTracking()
            .FirstAsync(p => p.LocationId == location && p.StockCode == "RF-KEYB");

        keyboard.Name.Should().Be("SOYA SUPREME BANASPATI GHEE 1 LIT",
            "column B was edited by hand and the annotation row says that edit is the product name");

        keyboard.RegularPrice.Should().Be(34.99m);
        keyboard.Type.Should().Be(ProductType.Serialized);

        var epc = await db.SerializedUnits.AsNoTracking()
            .Where(u => u.ProductId == keyboard.Id && u.State == SerializedUnitState.InStock)
            .Select(u => u.Epc!)
            .FirstAsync();

        var cart = await Ok(sender.Send(new CreateCartCommand(station.Id)));
        await Ok(sender.Send(new ClearCartCommand(cart.Id)));

        var read = new TagRead(epc, Antenna: 1, Rssi: -55, ReadCount: 3,
            FirstSeen: DateTimeOffset.UtcNow, LastSeen: DateTimeOffset.UtcNow);

        var batch = await Ok(sender.Send(new AddRfidBatchCommand(cart.Id, [read])));

        batch.Rejected.Should().BeEmpty();

        var line = batch.Accepted.Should().ContainSingle().Subject;
        line.Name.Should().Be("SOYA SUPREME BANASPATI GHEE 1 LIT");
        line.UnitPrice.Should().Be(34.99m);

        await sender.Send(new SuspendCartCommand(cart.Id, "Done with the import scenario"));
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<long> Location(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        _api.ActingUser.LocationId = location.Id;
        return location.Id;
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
