using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Rfid.Commands;
using Retail25.Application.UnitTests.Carts;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Catalog;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// The Phase 4 exit criteria for bulk RFID: three hundred tags into a cart with no duplicates, a
/// sold tag rejected with a clear reason, and stray reads kept off the sale.
/// </summary>
public sealed class BulkReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The headline number from the roadmap. The wall-clock assertion here is generous because it
    /// runs against an in-memory provider on a shared build agent — what it actually guards is
    /// algorithmic: that the handler does not go quadratic or issue a query per tag, which is the
    /// failure mode that turns 300 tags from a second into a minute.
    /// </summary>
    [Fact]
    public async Task Three_hundred_tags_land_on_the_cart_with_no_duplicates()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 300);
        var cart = await harness.OpenCartAsync();

        var stopwatch = Stopwatch.StartNew();
        var result = await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        result.Value.Accepted.Should().HaveCount(300);
        result.Value.Rejected.Should().BeEmpty();

        var snapshot = await harness.CartStore.GetAsync(cart.Id);
        snapshot!.Lines.Should().HaveCount(300);
        snapshot.Lines.Select(l => l.Epc).Should().OnlyHaveUniqueItems();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "300 tags must not take a per-tag round trip");
    }

    /// <summary>
    /// A reader reports the same tag many times, and two antennas can both see one basket. The batch
    /// is deduplicated before anything is priced, so a customer is never charged twice for one shirt.
    /// </summary>
    [Fact]
    public async Task The_same_tag_reported_many_times_becomes_one_line()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);
        var cart = await harness.OpenCartAsync();

        // The same EPC five times in one batch, as an antenna sweep would report it.
        var repeated = Enumerable.Repeat(epcs[0], 5).ToList();

        var result = await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(repeated)), default);

        result.Value.Accepted.Should().ContainSingle();
        result.Value.Considered.Should().Be(1, "the batch is deduplicated before anything is priced");
    }

    [Fact]
    public async Task A_tag_already_on_the_cart_is_rejected_rather_than_added_twice()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);
        var cart = await harness.OpenCartAsync();

        await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);
        var second = await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);

        second.Value.Accepted.Should().BeEmpty();
        second.Value.Rejected.Should().ContainSingle().Which.Reason.Should().Be("epc.already_on_cart");
    }

    /// <summary>The roadmap's second exit criterion: a sold tag re-read is refused with a clear reason.</summary>
    [Fact]
    public async Task A_sold_tag_read_again_is_rejected_with_a_reason_a_cashier_can_act_on()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);

        var unit = await harness.Db.SerializedUnits.SingleAsync(u => u.Epc == epcs[0]);
        unit.ClaimForCart();
        unit.Sell();
        await harness.Db.SaveChangesAsync();

        var cart = await harness.OpenCartAsync();
        var result = await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);

        result.Value.Accepted.Should().BeEmpty();

        var rejection = result.Value.Rejected.Should().ContainSingle().Subject;
        rejection.Reason.Should().Be("epc.already_sold");
        rejection.Message.Should().Contain("already been sold");
    }

    [Fact]
    public async Task An_unmapped_tag_is_surfaced_rather_than_silently_dropped()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);
        var cart = await harness.OpenCartAsync();

        var result = await handler.Handle(
            new AddRfidBatchCommand(cart.Id, Tags(["30ABCDEF0123456789ABCDEF"])),
            default);

        result.Value.Rejected.Should().ContainSingle().Which.Reason.Should().Be("epc.unknown");
    }

    /// <summary>
    /// Anti-false-positive control 1 and 2 (doc 06 §2): the shelf behind the till reads weaker, and on
    /// a different antenna. Neither should reach the cart.
    /// </summary>
    [Fact]
    public async Task A_weak_read_on_a_non_checkout_antenna_never_reaches_the_cart()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddReaderProfileAsync();

        var handler = Handler(harness);
        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 2);
        var cart = await harness.OpenCartAsync();

        var strays = new[]
        {
            new TagRead(epcs[0], Antenna: 9, Rssi: -55, ReadCount: 5, Now, Now),   // wrong zone
            new TagRead(epcs[1], Antenna: 1, Rssi: -95, ReadCount: 5, Now, Now),   // too weak
        };

        var result = await handler.Handle(new AddRfidBatchCommand(cart.Id, strays), default);

        result.Value.Accepted.Should().BeEmpty();
        result.Value.Rejected.Should().HaveCount(2).And.OnlyContain(r => r.Reason == "epc.filtered");
    }

    /// <summary>Control 3: a tag seen only once inside the window has not earned its place yet.</summary>
    [Fact]
    public async Task A_tag_seen_fewer_times_than_the_read_floor_is_filtered_out()
    {
        using var harness = await PosTestHarness.CreateAsync();
        await harness.AddReaderProfileAsync(minimumReadCount: 3);

        var handler = Handler(harness);
        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);
        var cart = await harness.OpenCartAsync();

        var result = await handler.Handle(
            new AddRfidBatchCommand(cart.Id, [new TagRead(epcs[0], 1, -55, ReadCount: 1, Now, Now)]),
            default);

        result.Value.Rejected.Should().ContainSingle().Which.Reason.Should().Be("epc.filtered");
    }

    /// <summary>
    /// Two tills within reading distance of one basket must not both claim a tag. The loser is told
    /// which situation it is in, because "another till has it" needs a different response from
    /// "this tag is unknown".
    /// </summary>
    [Fact]
    public async Task A_tag_another_till_is_holding_is_refused()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);

        // The neighbouring till got there first.
        await harness.Debouncer.TryClaimAsync(epcs[0], Guid.NewGuid(), TimeSpan.FromMinutes(1));

        var cart = await harness.OpenCartAsync();
        var result = await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);

        result.Value.Rejected.Should().ContainSingle().Which.Reason.Should().Be("epc.claimed_by_other_station");
    }

    /// <summary>
    /// Session gating (control 4): with no sale open, reads are noise. They are still surfaced, so a
    /// cashier can see the reader is alive rather than assuming it is broken.
    /// </summary>
    [Fact]
    public async Task Reads_with_no_sale_open_are_reported_but_not_applied()
    {
        using var harness = await PosTestHarness.CreateAsync();

        var handler = new IngestTagReadsHandler(harness.CartStore, harness.Sender, harness.Notifier, harness.TagFeed);

        var result = await handler.Handle(
            new IngestTagReadsCommand(harness.Station.Id, Tags(["30ABCDEF0123456789ABCDEF"])),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cart.Should().BeNull();
        result.Value.Rejected.Should().ContainSingle().Which.Reason.Should().Be("cart.none_active");
    }

    /// <summary>Accepted tags move InStock → InCart, so a second till cannot sell the same unit.</summary>
    [Fact]
    public async Task An_accepted_tag_is_claimed_in_redis_and_moved_into_the_cart_state()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handler = Handler(harness);

        var product = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        var epcs = await CommissionAsync(harness, product, 1);
        var cart = await harness.OpenCartAsync();

        await handler.Handle(new AddRfidBatchCommand(cart.Id, Tags(epcs)), default);

        (await harness.Debouncer.GetHolderAsync(epcs[0])).Should().Be(harness.Station.Id);

        var unit = await harness.Db.SerializedUnits.SingleAsync(u => u.Epc == epcs[0]);
        unit.State.Should().Be(SerializedUnitState.InCart);
    }

    private static AddRfidBatchHandler Handler(PosTestHarness harness) => new(
        harness.CartStore,
        harness.Db,
        harness.ContextLoader,
        harness.Pricing,
        harness.Resolver,
        harness.LineFactory,
        harness.Debouncer,
        harness.Notifier,
        harness.Clock);

    private static async Task<List<string>> CommissionAsync(PosTestHarness harness, Product product, int count)
    {
        var epcs = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var epc = $"30{i:X22}";
            await harness.AddTaggedUnitAsync(product, epc);
            epcs.Add(epc);
        }

        return epcs;
    }

    private static IReadOnlyList<TagRead> Tags(IEnumerable<string> epcs)
        => epcs.Select(epc => new TagRead(epc, 1, -55, 3, Now, Now)).ToList();
}
