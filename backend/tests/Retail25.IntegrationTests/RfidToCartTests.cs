using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Carts.Queries;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// A tag waved at the reader becomes a line on the sale, by itself.
/// <para>
/// This is the whole point of RFID at a till: the cashier does not scan, type or pick. So the chain
/// under test is the one the hardware actually drives — <c>IngestTagReadsCommand</c>, exactly as the
/// terminal agent sends it — through EPC resolution, the claim, the state machine and cart pricing,
/// ending with a priced line and a tag that can no longer be sold at another till.
/// </para>
/// <para>
/// Driven by command rather than by radio, so it runs on a build server with no reader attached and
/// gives the same answer every time. The physical half is recorded separately in
/// <c>docs/runbooks/hardware-matrix.md</c>, verified against a D2184 on 2026-08-01.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class RfidToCartTests
{
    private readonly CommerceApiFixture _api;

    public RfidToCartTests(CommerceApiFixture api) => _api = api;

    /// <summary>A 96-bit SGTIN, unique per call, so a shared database cannot collide runs.</summary>
    private static string Epc() => Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();

    private static TagRead Read(string epc, int antenna = 1) =>
        new(epc, antenna, Rssi: -55, ReadCount: 3, FirstSeen: DateTimeOffset.UtcNow, LastSeen: DateTimeOffset.UtcNow);

    // ---------------------------------------------------------------------------------------------

    [RequiresDockerFact]
    public async Task A_tag_read_at_an_open_till_becomes_a_priced_line_on_the_sale()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, station) = await Context(db);

        // An item worth £45, and one physical unit of it wearing a tag.
        var stockCode = $"TAG-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, "Tagged jacket", null, ProductType.Serialized, null, null, null, null),
            RegularPrice: 45.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epc = Epc();

        await Ok(sender.Send(new CommissionTagCommand(epc, product.Id, location)));

        // The tag is now a row in the database, in stock and sellable. That is the "stored in the
        // database" half — the cart half follows.
        var unit = await db.SerializedUnits.AsNoTracking().FirstAsync(u => u.Epc == epc);
        unit.State.Should().Be(SerializedUnitState.InStock);
        unit.ProductId.Should().Be(product.Id);

        // A sale is open at this till.
        var cartId = await OpenEmptyCart(sender, station);

        // The reader sees the tag. This is byte-for-byte what the agent publishes.
        var batch = await Ok(sender.Send(new IngestTagReadsCommand(station, [Read(epc)])));

        // ---- the line went on by itself -----------------------------------------------------
        // The reasons are in the message: "expected one, found none" sends you reading handlers,
        // where "found none, refused as epc.unknown" sends you to the one line that matters.
        batch.Accepted.Should().ContainSingle(
            "one tag in the field is one line on the sale. Refusals: " + Describe(batch));

        batch.Rejected.Should().BeEmpty();

        var line = batch.Accepted[0];
        line.Name.Should().Be("Tagged jacket");
        line.UnitPrice.Should().Be(45.00m, "the tag resolved to the item and the item priced itself");
        line.Quantity.Should().Be(1m, "one tag is one physical thing");

        // ---- and the cart agrees ------------------------------------------------------------
        var reloaded = await Ok(sender.Send(new GetCartQuery(cartId)));

        // Scoped to this scenario's own product. The till is shared across the suite and holds one
        // open sale at a time, so asserting on the cart's grand total would measure whatever else
        // the suite has rung up — which is how five £20 shirts first came to £225.
        reloaded.Lines.Count(l => l.ProductId == product.Id).Should().Be(1);
        reloaded.Lines.Where(l => l.ProductId == product.Id).Sum(l => l.ExtendedNet).Should().Be(45.00m);

        // ---- the unit is now spoken for -----------------------------------------------------
        var claimed = await db.SerializedUnits.AsNoTracking().FirstAsync(u => u.Epc == epc);
        claimed.State.Should().Be(SerializedUnitState.InCart, "a unit on a cart is not available to another till");
    }

    /// <summary>
    /// Several tags at once — a basket set on the counter rather than items presented one by one.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_basket_of_tags_lands_as_a_basket_of_lines()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, station) = await Context(db);

        var stockCode = $"BSK-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, "Tagged shirt", null, ProductType.Serialized, null, null, null, null),
            RegularPrice: 20.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epcs = Enumerable.Range(0, 5).Select(_ => Epc()).ToList();

        foreach (var epc in epcs)
        {
            await Ok(sender.Send(new CommissionTagCommand(epc, product.Id, location)));
        }

        var cartId = await OpenEmptyCart(sender, station);

        var batch = await Ok(sender.Send(new IngestTagReadsCommand(
            station,
            epcs.Select(e => Read(e)).ToList())));

        batch.Accepted.Should().HaveCount(5);

        var reloaded = await Ok(sender.Send(new GetCartQuery(cartId)));

        reloaded.Lines.Where(l => l.ProductId == product.Id).Sum(l => l.ExtendedNet)
            .Should().Be(100.00m, "five tagged shirts at £20");
    }

    /// <summary>
    /// The same tag read forty times in a second is one item, not forty.
    /// <para>
    /// A reader running fast polling re-reads a tag sitting in the field constantly. Without the
    /// debounce this is the failure a customer notices: one jacket, forty lines, and a cashier
    /// deleting thirty-nine of them.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task A_tag_read_repeatedly_produces_one_line_not_many()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, station) = await Context(db);

        var stockCode = $"RPT-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, "Tagged coat", null, ProductType.Serialized, null, null, null, null),
            RegularPrice: 80.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epc = Epc();
        await Ok(sender.Send(new CommissionTagCommand(epc, product.Id, location)));

        var cartId = await OpenEmptyCart(sender, station);

        // Forty reads of one tag, as forty separate publishes from the agent.
        for (var i = 0; i < 40; i++)
        {
            await Ok(sender.Send(new IngestTagReadsCommand(station, [Read(epc)])));
        }

        var reloaded = await Ok(sender.Send(new GetCartQuery(cartId)));

        reloaded.Lines.Count(l => l.ProductId == product.Id)
            .Should().Be(1, "forty reads of one coat is one coat");

        reloaded.Lines.Where(l => l.ProductId == product.Id).Sum(l => l.ExtendedNet).Should().Be(80.00m);
    }

    /// <summary>
    /// A tag already sold cannot be sold again, and the refusal says why.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_tag_that_is_not_in_stock_is_refused_with_a_reason()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, station) = await Context(db);

        var stockCode = $"SLD-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, "Already sold", null, ProductType.Serialized, null, null, null, null),
            RegularPrice: 10.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epc = Epc();
        await Ok(sender.Send(new CommissionTagCommand(epc, product.Id, location)));

        // Move it out of stock behind the system's back, standing in for "sold an hour ago".
        var unit = await db.SerializedUnits.FirstAsync(u => u.Epc == epc);
        unit.GetType().GetProperty(nameof(unit.State))!.SetValue(unit, SerializedUnitState.Sold);
        await db.SaveChangesAsync();

        await OpenEmptyCart(sender, station);

        var batch = await Ok(sender.Send(new IngestTagReadsCommand(station, [Read(epc)])));

        batch.Accepted.Should().BeEmpty();
        batch.Rejected.Should().ContainSingle()
            .Which.Reason.Should().NotBeNullOrWhiteSpace("a refusal a cashier cannot explain is a refusal they override");
    }

    /// <summary>
    /// An unknown tag is refused rather than guessed at — and named, so a supervisor can commission
    /// it from the live feed.
    /// </summary>
    [RequiresDockerFact]
    public async Task An_uncommissioned_tag_is_refused_rather_than_guessed_at()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (_, station) = await Context(db);

        await OpenEmptyCart(sender, station);

        var stranger = Epc();
        var batch = await Ok(sender.Send(new IngestTagReadsCommand(station, [Read(stranger)])));

        batch.Accepted.Should().BeEmpty();
        batch.Rejected.Should().ContainSingle().Which.Epc.Should().Be(stranger);
    }

    /// <summary>
    /// With no sale open, reads are reported but nothing is applied — the session gate
    /// (doc 06 §2 control 4). A till that quietly opened a sale because someone walked past with a
    /// tagged coat would ring up the coat.
    /// </summary>
    [RequiresDockerFact]
    public async Task Reads_with_no_sale_open_are_reported_but_not_applied()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (location, station) = await Context(db);

        var stockCode = $"GAT-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var product = await Ok(sender.Send(new CreateProductCommand(
            location,
            new ProductGeneralSection(stockCode, "Passing coat", null, ProductType.Serialized, null, null, null, null),
            RegularPrice: 60.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var epc = Epc();
        await Ok(sender.Send(new CommissionTagCommand(epc, product.Id, location)));

        // Any cart left open by an earlier test is closed first, or this asserts nothing.
        await CloseAnyOpenCart(sender, station);

        var batch = await Ok(sender.Send(new IngestTagReadsCommand(station, [Read(epc)])));

        batch.Cart.Should().BeNull();
        batch.Rejected.Should().ContainSingle().Which.Reason.Should().Be("cart.none_active");

        // And crucially the tag is untouched — still in stock, still sellable.
        var untouched = await db.SerializedUnits.AsNoTracking().FirstAsync(u => u.Epc == epc);
        untouched.State.Should().Be(SerializedUnitState.InStock);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<(long Location, long Station)> Context(ApplicationDbContext db)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        return (location.Id, station.Id);
    }

    /// <summary>
    /// Voids whatever sale is open at the till, so a gating test starts from a closed till however
    /// the suite happened to be ordered.
    /// </summary>
    private static async Task CloseAnyOpenCart(ISender sender, long stationId)
    {
        var existing = await sender.Send(new GetStationCartQuery(stationId));

        if (existing.IsSuccess && existing.Value is { } cart)
        {
            await sender.Send(new SuspendCartCommand(cart.Id, "Closed for a gating test"));
        }
    }

    /// <summary>
    /// The station's sale, emptied.
    /// <para>
    /// A till holds one open sale at a time, so <c>CreateCartCommand</c> hands back the existing one
    /// rather than starting a second — correct behaviour, and the reason these tests first saw five
    /// £20 shirts total £225: the jacket and the coat from earlier tests were still on the counter.
    /// </para>
    /// </summary>
    private static async Task<long> OpenEmptyCart(ISender sender, long stationId)
    {
        var cart = await Ok(sender.Send(new CreateCartCommand(stationId)));

        // Cleared unconditionally rather than only when the returned DTO shows lines. The create
        // command's response does not necessarily carry them, so testing the count first skipped the
        // clear on a cart that did in fact have items — which is how five £20 shirts came to £225.
        var emptied = await Ok(sender.Send(new ClearCartCommand(cart.Id)));

        emptied.Lines.Should().BeEmpty("the till must start this scenario with nothing on it");

        return cart.Id;
    }

    /// <summary>Why each tag was turned away, for an assertion message that explains itself.</summary>
    private static string Describe(RfidBatchResult batch)
        => batch.Rejected.Count == 0
            ? "none"
            : string.Join(", ", batch.Rejected.Select(r => $"{r.Epc} -> {r.Reason}"));

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;

        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
