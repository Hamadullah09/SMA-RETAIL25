using FluentAssertions;
using Microsoft.Extensions.Options;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys;
using Retail25.Application.Trolleys.Services;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Trolleys;

/// <summary>
/// Handing a customer a self-checkout counter without them choosing one.
/// <para>
/// The property under test throughout is that the 300 block is the whole of what a shopper can be
/// given. A staffed till is not a counter a customer may be issued, however few self-checkouts are
/// free — the alternative to "no counter available" is never "here is the front counter".
/// </para>
/// </summary>
public sealed class SelfCheckoutAllocationTests
{
    private const long ShopperA = 9001L;
    private const long ShopperB = 9002L;

    [Fact]
    public async Task The_lowest_free_self_checkout_counter_is_issued_and_the_front_counter_never_is()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var allocator = Allocator(harness);

        // Deliberately added out of order: the answer must be the lowest code, not the first row.
        await AddCounterAsync(harness, "302");
        await AddCounterAsync(harness, "301");

        var issued = await allocator.IssueNextFreeAsync(ShopperA, harness.Location.Id, default);

        issued.IsSuccess.Should().BeTrue();
        issued.Value.TrolleyCode.Should().Be("301");

        // The seeded station is "001", the front counter. A customer must never land on it.
        issued.Value.TrolleyCode.Should().NotBe(harness.Station.StationCode);
    }

    [Fact]
    public async Task Two_shoppers_are_never_issued_the_same_counter()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var allocator = Allocator(harness);

        await AddCounterAsync(harness, "301");
        await AddCounterAsync(harness, "302");

        var first = await allocator.IssueNextFreeAsync(ShopperA, harness.Location.Id, default);
        var second = await allocator.IssueNextFreeAsync(ShopperB, harness.Location.Id, default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        second.Value.TrolleyCode.Should().NotBe(
            first.Value.TrolleyCode,
            "two customers sharing a counter would be watching each other's shopping");

        second.Value.Cart!.Id.Should().NotBe(first.Value.Cart!.Id);
    }

    /// <summary>
    /// The app calls this on every launch, so it has to be safe to call twice. Issuing a second
    /// counter to somebody mid-shop would strand a full basket on the first one.
    /// </summary>
    [Fact]
    public async Task A_shopper_already_shopping_is_given_the_same_trip_back()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var allocator = Allocator(harness);

        await AddCounterAsync(harness, "301");
        await AddCounterAsync(harness, "302");

        var first = await allocator.IssueNextFreeAsync(ShopperA, harness.Location.Id, default);
        var again = await allocator.IssueNextFreeAsync(ShopperA, harness.Location.Id, default);

        again.IsSuccess.Should().BeTrue();
        again.Value.SessionId.Should().Be(first.Value.SessionId);
        again.Value.TrolleyCode.Should().Be(first.Value.TrolleyCode);
    }

    /// <summary>
    /// Typing the front counter's code is refused for the same reason it is never issued: the range
    /// is what separates a customer's counter from a cashier's till.
    /// </summary>
    [Fact]
    public async Task The_front_counter_cannot_be_claimed_by_typing_its_code()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var allocator = Allocator(harness);

        var claimed = await allocator.ClaimAsync(ShopperA, "001", harness.Location.Id, default);

        claimed.IsFailure.Should().BeTrue();
        claimed.Error.Code.Should().Be("trolley.not_a_shopper_station");
    }

    private static TrolleyAllocator Allocator(PosTestHarness harness)
    {
        var opener = new CartOpener(
            harness.CartStore,
            harness.ContextLoader,
            harness.Pricing,
            harness.Clock,
            harness.Db);

        return new TrolleyAllocator(
            harness.Db,
            opener,
            harness.CartStore,
            harness.Clock,
            Options.Create(new TrolleyOptions()));
    }

    private static async Task AddCounterAsync(PosTestHarness harness, string code)
    {
        var station = Station.Create(harness.Location.Id, code, $"Counter {code}").Value;

        harness.Db.Stations.Add(station);
        await harness.Db.SaveChangesAsync(default);
    }
}
