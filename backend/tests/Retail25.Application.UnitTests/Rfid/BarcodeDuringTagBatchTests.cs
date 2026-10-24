using FluentAssertions;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.UnitTests.Carts;
using Retail25.Contracts.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// A barcode scanned while the reader is running must reach the sale.
/// <para>
/// A shop reported it did not: "the reader works but the barcode does not". The agent's tag batches
/// and the browser's barcode scans both read the cart, change their copy and save it, and the tag path
/// holds its copy across a claim, a lookup and a line build for every tag. A barcode saved inside that
/// window was overwritten by the batch's save a moment later, and because both bumped the revision to
/// the same number the till discarded one of the two pushes as already seen.
/// </para>
/// <para>
/// The in-memory cart store shares one object between readers, so these tests cannot reproduce the
/// serialised overwrite itself — SQL and Redis copy on every read, and that is where it happened. What
/// they pin instead is the property that prevents it: while a tag batch holds the cart, a barcode for
/// the same cart waits rather than proceeding.
/// </para>
/// </summary>
public sealed class BarcodeDuringTagBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_barcode_queues_behind_a_tag_batch_in_flight_and_both_lines_land()
    {
        using var harness = await PosTestHarness.CreateAsync();

        var shirt = await harness.AddProductAsync("SHIRT", "Cotton shirt", 20.00m);
        await harness.AddProductAsync("MUG", "Enamel mug", 6.50m);
        await harness.AddTaggedUnitAsync(shirt, "30AAAAAAAAAAAAAAAAAAAAAA");
        var cart = await harness.OpenCartAsync();

        var gate = new GatedDebouncer(harness.Debouncer);
        var checkout = new Retail25.Application.Rfid.Services.RfidCheckout(
            harness.CartStore,
            harness.Db,
            harness.ContextLoader,
            harness.Pricing,
            harness.Resolver,
            harness.LineFactory,
            gate,
            harness.Notifier,
            harness.Clock,
            harness.Locks);

        var batch = checkout.AddBatchAsync(
            cart.Id,
            [new TagRead("30AAAAAAAAAAAAAAAAAAAAAA", 1, -55, 3, Now, Now)],
            CancellationToken.None);

        // The batch has read the cart and is part-way through it.
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var scan = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory)
            .Handle(new AddCartLineByIdentifierCommand(cart.Id, "MUG"), CancellationToken.None);

        // Generous, and it cannot be flaky in the passing direction: under the lock the scan is
        // unable to finish while the gate is shut, however long it is given.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        scan.IsCompleted.Should().BeFalse(
            "a barcode that finishes while a tag batch holds its copy of the cart is the barcode that batch overwrites");

        gate.Open();

        (await batch).IsSuccess.Should().BeTrue();
        (await scan).IsSuccess.Should().BeTrue();

        var saved = await harness.CartStore.GetAsync(cart.Id);
        saved!.Lines.Select(l => l.StockCodeSnapshot).Should().BeEquivalentTo(["SHIRT", "MUG"]);
    }

    /// <summary>
    /// Holds the first claim until told to continue, so a test can stand in the middle of a batch.
    /// </summary>
    private sealed class GatedDebouncer : ITagDebouncer
    {
        private readonly ITagDebouncer _inner;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedDebouncer(ITagDebouncer inner) => _inner = inner;

        public Task Entered => _entered.Task;

        public void Open() => _open.TrySetResult();

        public async Task<bool> TryClaimAsync(string epc, long stationId, TimeSpan window, CancellationToken ct = default)
        {
            _entered.TrySetResult();
            await _open.Task.WaitAsync(ct);
            return await _inner.TryClaimAsync(epc, stationId, window, ct);
        }

        public Task ReleaseAsync(string epc, long stationId, CancellationToken ct = default)
            => _inner.ReleaseAsync(epc, stationId, ct);

        public Task<long?> GetHolderAsync(string epc, CancellationToken ct = default)
            => _inner.GetHolderAsync(epc, ct);
    }
}

public sealed class CartLocksTests
{
    [Fact]
    public async Task A_second_writer_waits_for_the_first()
    {
        var locks = new CartLocks();

        var first = await locks.AcquireAsync(1, CancellationToken.None);
        var second = locks.AcquireAsync(1, CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        second.IsCompleted.Should().BeFalse();

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
    }

    [Fact]
    public async Task Different_carts_do_not_wait_on_each_other()
    {
        var locks = new CartLocks();

        using var one = await locks.AcquireAsync(1, CancellationToken.None);
        var two = locks.AcquireAsync(2, CancellationToken.None);

        two.IsCompleted.Should().BeTrue("a till must never queue behind somebody else's sale");
        (await two).Dispose();
    }

    /// <summary>
    /// One entry per cart that is being written right now, not one per cart ever seen. A day of sales
    /// is thousands of carts, and a lock that kept a semaphore for each would be a slow leak.
    /// </summary>
    [Fact]
    public async Task Entries_are_dropped_once_nobody_holds_or_waits()
    {
        var locks = new CartLocks();

        for (var cart = 1; cart <= 50; cart++)
        {
            (await locks.AcquireAsync(cart, CancellationToken.None)).Dispose();
        }

        locks.TrackedCount.Should().Be(0);
    }

    /// <summary>
    /// A request abandoned while queued never held the cart, so it must not release it — which would
    /// let a third writer in alongside the one that does — and must stop counting as a user.
    /// </summary>
    [Fact]
    public async Task A_waiter_that_is_cancelled_neither_releases_nor_leaks()
    {
        var locks = new CartLocks();
        using var cancel = new CancellationTokenSource();

        var holder = await locks.AcquireAsync(7, CancellationToken.None);
        var abandoned = locks.AcquireAsync(7, cancel.Token);

        cancel.Cancel();
        var act = async () => await abandoned;
        await act.Should().ThrowAsync<OperationCanceledException>();

        var third = locks.AcquireAsync(7, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        third.IsCompleted.Should().BeFalse("the holder still has the cart");

        holder.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();

        locks.TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task Releasing_twice_does_not_release_somebody_else()
    {
        var locks = new CartLocks();

        var first = await locks.AcquireAsync(3, CancellationToken.None);
        first.Dispose();

        var second = await locks.AcquireAsync(3, CancellationToken.None);

        // A stray second dispose of the first handle must not hand the cart to a third writer while
        // the second still holds it.
        first.Dispose();

        var third = locks.AcquireAsync(3, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        third.IsCompleted.Should().BeFalse();

        second.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
    }
}
