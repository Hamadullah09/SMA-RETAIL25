using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Caching;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The SQL-backed stores that stand in for Redis where there is no Redis to have.
/// <para>
/// These are not tests of "does a row round-trip". The Redis originals lean on operations the
/// database has to provide atomically — <c>SET NX</c> for a tag claim, <c>GETDEL</c> for a ticket —
/// and the whole risk of the rewrite is that the SQL equivalents are only <em>nearly</em> atomic.
/// A claim that two tills can both win sells one garment twice; a ticket that redeems twice hands
/// out a second socket. Neither shows up in a round-trip test, so most of what is below is about
/// contention and expiry rather than storage.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlCacheStoreTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;
    private ApplicationDbContextScope _scope = null!;

    public SqlCacheStoreTests(SqlServerFixture sqlServer) => _sqlServer = sqlServer;

    public async Task InitializeAsync() => _scope = await ApplicationDbContextScope.CreateAsync(_sqlServer, "sqlcache");

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private SqlTagDebouncer Debouncer() => new(_scope.Db);

    private SqlHubTicketStore Tickets() => new(_scope.Db, new CacheSweeper());

    private SqlIdempotencyStore Idempotency() => new(_scope.Db, new CacheSweeper());

    private SqlCartStore Carts() => new(_scope.Db, new CacheSweeper(), NullLogger<SqlCartStore>.Instance);

    // --- Tag arbitration -------------------------------------------------------------------

    [RequiresIsolatedDatabaseFact]
    public async Task A_tag_claimed_by_one_till_is_refused_to_another()
    {
        var debouncer = Debouncer();
        const string epc = "E280116060000205C1FA0001";

        (await debouncer.TryClaimAsync(epc, stationId: 1, TimeSpan.FromMinutes(5))).Should().BeTrue();

        // The whole reason this store exists. Two tills reading the same basket must not both be
        // told the garment is theirs.
        (await debouncer.TryClaimAsync(epc, stationId: 2, TimeSpan.FromMinutes(5))).Should().BeFalse();

        (await debouncer.GetHolderAsync(epc)).Should().Be(1);
    }

    [RequiresIsolatedDatabaseFact]
    public async Task The_holder_reclaiming_refreshes_rather_than_conflicts()
    {
        var debouncer = Debouncer();
        const string epc = "E280116060000205C1FA0002";

        await debouncer.TryClaimAsync(epc, stationId: 7, TimeSpan.FromMinutes(5));

        // A reader reports the same tag many times a second. Treating that as a conflict would make
        // the till refuse an item it is already holding.
        (await debouncer.TryClaimAsync(epc, stationId: 7, TimeSpan.FromMinutes(5))).Should().BeTrue();
        (await debouncer.GetHolderAsync(epc)).Should().Be(7);
    }

    [RequiresIsolatedDatabaseFact]
    public async Task An_expired_claim_frees_the_tag_for_someone_else()
    {
        var debouncer = Debouncer();
        const string epc = "E280116060000205C1FA0003";

        // A till that crashed mid-sale leaves its claim behind. Without expiry the garment would be
        // permanently unsellable, which is worse than the double-sale the claim prevents.
        await debouncer.TryClaimAsync(epc, stationId: 1, TimeSpan.FromMilliseconds(200));
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        (await debouncer.GetHolderAsync(epc)).Should().BeNull("the window has passed");
        (await debouncer.TryClaimAsync(epc, stationId: 2, TimeSpan.FromMinutes(5))).Should().BeTrue();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Only_the_holder_can_release_a_tag()
    {
        var debouncer = Debouncer();
        const string epc = "E280116060000205C1FA0004";

        await debouncer.TryClaimAsync(epc, stationId: 1, TimeSpan.FromMinutes(5));

        // A till that could release another's claim could also take the item off their screen.
        await debouncer.ReleaseAsync(epc, stationId: 2);
        (await debouncer.GetHolderAsync(epc)).Should().Be(1);

        await debouncer.ReleaseAsync(epc, stationId: 1);
        (await debouncer.GetHolderAsync(epc)).Should().BeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Tag_case_does_not_create_a_second_claim()
    {
        var debouncer = Debouncer();

        await debouncer.TryClaimAsync("e280116060000205c1fa0005", stationId: 1, TimeSpan.FromMinutes(5));

        // Two readers reporting the same tag in different case are reporting the same garment.
        (await debouncer.TryClaimAsync("E280116060000205C1FA0005", stationId: 2, TimeSpan.FromMinutes(5)))
            .Should().BeFalse();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Concurrent_claims_on_one_tag_produce_exactly_one_winner()
    {
        const string epc = "E280116060000205C1FA0006";
        var connectionString = _scope.Db.Database.GetConnectionString();

        // The real shape of the risk: not two sequential calls, but twenty at once. Each gets its
        // own context, because a single DbContext serialises them and would prove nothing.
        var results = await Task.WhenAll(Enumerable.Range(1, 20).Select(async station =>
        {
            await using var db = _sqlServer.CreateContext(connectionString);
            return await new SqlTagDebouncer(db).TryClaimAsync(epc, station, TimeSpan.FromMinutes(5));
        }));

        results.Count(won => won).Should().Be(1, "MERGE under HOLDLOCK must settle this, not the application");
    }

    // --- Hub tickets -----------------------------------------------------------------------

    /// <summary>
    /// A ticket opens one connection, and one connection costs two exchanges: the negotiate POST,
    /// then the transport connection after it. The SignalR client calls its accessTokenFactory once
    /// per attempt and presents the same token to both.
    /// <para>
    /// This asserted a single redemption, which was the wrong count rather than the wrong idea, and
    /// it was load-bearing: negotiate spent the ticket, the WebSocket upgrade arrived with one that
    /// no longer existed, and every hub in the product silently fell back to long polling. The beta
    /// audit recorded that as a shared-hosting limitation. The host forwards the upgrade perfectly —
    /// a 101 against the live site settles it.
    /// </para>
    /// </summary>
    [RequiresIsolatedDatabaseFact]
    public async Task A_hub_ticket_opens_one_connection_and_no_more()
    {
        var tickets = Tickets();
        var issued = await tickets.IssueAsync(SampleTicket(), TimeSpan.FromMinutes(1));

        // Negotiate.
        var first = await tickets.RedeemAsync(issued);
        first.Should().NotBeNull();
        first!.UserId.Should().Be(42);

        // The transport connection that negotiate authorised.
        var second = await tickets.RedeemAsync(issued);
        second.Should().NotBeNull("the WebSocket upgrade presents the same ticket negotiate used");
        second!.UserId.Should().Be(42);

        // And nothing beyond it. This is what makes handing a ticket to the browser safe: a leaked
        // one cannot be replayed into a connection of somebody else's.
        (await tickets.RedeemAsync(issued)).Should().BeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task An_expired_hub_ticket_is_not_redeemable()
    {
        var tickets = Tickets();
        var issued = await tickets.IssueAsync(SampleTicket(), TimeSpan.FromMilliseconds(200));

        await Task.Delay(TimeSpan.FromMilliseconds(400));

        // Expiry is in the WHERE clause rather than a sweep, so this holds even though nothing has
        // deleted the row yet.
        (await tickets.RedeemAsync(issued)).Should().BeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Concurrent_redemptions_of_one_ticket_produce_exactly_one_winner()
    {
        var connectionString = _scope.Db.Database.GetConnectionString();
        var issued = await Tickets().IssueAsync(SampleTicket(), TimeSpan.FromMinutes(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = _sqlServer.CreateContext(connectionString);
            return await new SqlHubTicketStore(db, new CacheSweeper()).RedeemAsync(issued);
        }));

        // Two, because a connection is two exchanges — and exactly two out of ten, which is the
        // point. The decrement takes an exclusive row lock, so ten racing callers serialise and the
        // counter is what decides, not who arrived first.
        results.Count(t => t is not null).Should().Be(
            2, "the counter is decremented under an exclusive row lock, so the race cannot overdraw it");
    }

    [RequiresIsolatedDatabaseFact]
    public async Task An_unknown_ticket_is_simply_null()
    {
        (await Tickets().RedeemAsync("not-a-ticket")).Should().BeNull();
        (await Tickets().RedeemAsync("")).Should().BeNull();
    }

    // --- Idempotency -----------------------------------------------------------------------

    [RequiresIsolatedDatabaseFact]
    public async Task A_stored_response_is_replayed_for_the_same_key()
    {
        var store = Idempotency();

        await store.StoreResponseAsync("key-1", new SampleResponse(99, "paid"));

        var replayed = await store.GetResponseAsync<SampleResponse>("key-1");
        replayed.Should().NotBeNull();
        replayed!.Id.Should().Be(99);

        // A cashier pressing Pay twice must not take the money twice; a different sale must not be
        // handed the first one's receipt.
        (await store.GetResponseAsync<SampleResponse>("key-2")).Should().BeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Storing_the_same_key_twice_replaces_rather_than_throws()
    {
        var store = Idempotency();

        await store.StoreResponseAsync("key-3", new SampleResponse(1, "first"));
        await store.StoreResponseAsync("key-3", new SampleResponse(2, "second"));

        (await store.GetResponseAsync<SampleResponse>("key-3"))!.Id.Should().Be(2);
    }

    // --- Carts -----------------------------------------------------------------------------

    [RequiresIsolatedDatabaseFact]
    public async Task An_active_cart_is_found_by_its_station_and_a_finished_one_is_not()
    {
        var carts = Carts();
        var snapshot = SampleCart(cartId: 500, stationId: 9, CartStatus.Active);

        await carts.SaveAsync(snapshot);

        (await carts.GetAsync(500)).Should().NotBeNull();
        (await carts.GetByStationAsync(9)).Should().NotBeNull();

        // Completing the sale has to release the till, or the next customer cannot start one.
        snapshot.Cart.Status = CartStatus.Completed;
        await carts.SaveAsync(snapshot);

        (await carts.GetByStationAsync(9)).Should().BeNull("a completed cart no longer owns its station");
        (await carts.GetAsync(500)).Should().NotBeNull("but the snapshot is still addressable by id");
    }

    [RequiresIsolatedDatabaseFact]
    public async Task Removing_a_cart_forgets_it_and_frees_the_station()
    {
        var carts = Carts();
        await carts.SaveAsync(SampleCart(cartId: 501, stationId: 11, CartStatus.Active));

        await carts.RemoveAsync(501, 11);

        (await carts.GetAsync(501)).Should().BeNull();
        (await carts.GetByStationAsync(11)).Should().BeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task A_saved_cart_comes_back_with_its_lines_intact()
    {
        var carts = Carts();
        var snapshot = SampleCart(cartId: 502, stationId: 13, CartStatus.Active);
        snapshot.Lines.Add(new CartLine
        {
            Id = 1,
            CartId = 502,
            ProductId = 77,
            Quantity = 3m,
            UnitPrice = 12.50m,
            ExtendedNet = 37.50m,
            Sequence = 1,
            NameSnapshot = "Sample",
        });

        await carts.SaveAsync(snapshot);

        var restored = await carts.GetAsync(502);
        restored.Should().NotBeNull();
        restored!.Lines.Should().ContainSingle();

        // Identifiers surviving the round trip is the thing PersistedCart exists to guarantee —
        // Entity.Id has an internal setter a serializer would silently skip.
        restored.Lines[0].Id.Should().Be(1);
        restored.Lines[0].ProductId.Should().Be(77);
        restored.Lines[0].Quantity.Should().Be(3m);
        restored.Lines[0].UnitPrice.Should().Be(12.50m);
    }

    private static HubTicket SampleTicket() => new(42, 7, 3, 1, ["pos.sell"]);

    private static CartSnapshot SampleCart(long cartId, long stationId, CartStatus status)
        => new(new Cart
        {
            Id = cartId,
            StationId = stationId,
            LocationId = 1,
            StaffId = 1,
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero),
        });

    private sealed record SampleResponse(int Id, string Status);
}
