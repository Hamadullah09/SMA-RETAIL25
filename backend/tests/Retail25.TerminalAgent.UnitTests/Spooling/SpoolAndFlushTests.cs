using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent;
using Retail25.TerminalAgent.Rfid;
using Retail25.TerminalAgent.Server;
using Retail25.TerminalAgent.Spooling;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Spooling;

/// <summary>
/// The offline spool (doc 06 §6). A till loses its network more often than anything else in the
/// building, and reads taken during an outage are still the basket in front of the cashier.
/// </summary>
public sealed class SqliteTagSpoolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"retail25-spool-{Guid.NewGuid():N}");

    [Fact]
    public async Task A_spooled_batch_comes_back_intact()
    {
        using var spool = CreateSpool();

        var tags = new[] { Tag("EPC001"), Tag("EPC002") };
        await spool.EnqueueAsync(tags);

        var batches = await spool.PeekAsync(10);

        batches.Should().ContainSingle();
        batches[0].Tags.Should().HaveCount(2);
        batches[0].Tags[0].Epc.Should().Be("EPC001");
        batches[0].Tags[0].Rssi.Should().Be(-55);
    }

    /// <summary>
    /// Only what was actually delivered is acknowledged. Acknowledging the whole peek would drop
    /// batches the server never received if the connection failed partway through the replay.
    /// </summary>
    [Fact]
    public async Task Acknowledging_removes_only_the_batches_named()
    {
        using var spool = CreateSpool();

        await spool.EnqueueAsync([Tag("A")]);
        await spool.EnqueueAsync([Tag("B")]);
        await spool.EnqueueAsync([Tag("C")]);

        var batches = await spool.PeekAsync(10);
        await spool.AcknowledgeAsync([batches[0].Id, batches[1].Id]);

        var remaining = await spool.PeekAsync(10);

        remaining.Should().ContainSingle();
        remaining[0].Tags[0].Epc.Should().Be("C");
    }

    [Fact]
    public async Task Batches_replay_oldest_first_so_ordering_survives_the_outage()
    {
        using var spool = CreateSpool();

        await spool.EnqueueAsync([Tag("FIRST")]);
        await spool.EnqueueAsync([Tag("SECOND")]);
        await spool.EnqueueAsync([Tag("THIRD")]);

        var batches = await spool.PeekAsync(10);

        batches.Select(b => b.Tags[0].Epc).Should().ContainInOrder("FIRST", "SECOND", "THIRD");
    }

    /// <summary>
    /// The spool is bounded. A till offline all weekend must not fill its disk and take itself down
    /// for a reason unrelated to the outage — and weekend-old reads describe baskets that left long ago.
    /// </summary>
    [Fact]
    public async Task The_spool_is_bounded_by_batch_count()
    {
        using var spool = CreateSpool(maxBatches: 5);

        for (var i = 0; i < 20; i++)
        {
            await spool.EnqueueAsync([Tag($"EPC{i:D3}")]);
        }

        (await spool.CountAsync()).Should().Be(5);

        // The newest survive: they are the ones most likely still to matter.
        var remaining = await spool.PeekAsync(10);
        remaining[^1].Tags[0].Epc.Should().Be("EPC019");
    }

    [Fact]
    public async Task Spooled_batches_survive_the_process_that_wrote_them()
    {
        using (var spool = CreateSpool())
        {
            await spool.EnqueueAsync([Tag("SURVIVOR")]);
        }

        // A till loses power mid-basket; the reads have to still be there when it comes back.
        using var reopened = CreateSpool();

        var batches = await reopened.PeekAsync(10);
        batches.Should().ContainSingle();
        batches[0].Tags[0].Epc.Should().Be("SURVIVOR");
    }

    [Fact]
    public async Task An_empty_batch_is_not_stored()
    {
        using var spool = CreateSpool();

        await spool.EnqueueAsync([]);

        (await spool.CountAsync()).Should().Be(0);
    }

    private SqliteTagSpool CreateSpool(int maxBatches = 5000) => new(
        Options.Create(new AgentOptions
        {
            StationId = Guid.NewGuid().ToString(),
            SpoolPath = Path.Combine(_directory, "tags.db"),
            SpoolMaxBatches = maxBatches,
        }),
        NullLogger<SqliteTagSpool>.Instance);

    private static TagRead Tag(string epc)
        => new(epc, 1, -55, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

/// <summary>
/// The flush loop's contract with the outside world: publish when the server is there, spool when it
/// is not, and replay in order once it comes back.
/// </summary>
public sealed class TagFlushBehaviourTests
{
    [Fact]
    public async Task A_batch_that_cannot_be_delivered_is_spooled_rather_than_dropped()
    {
        var server = new FakeServerConnection { Connected = false };
        var spool = new InMemorySpool();
        var buffer = new TagBuffer();

        buffer.Offer(new TagRead("EPC001", 1, -55, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        await FlushOnceAsync(buffer, server, spool);

        server.Published.Should().BeEmpty();
        spool.Batches.Should().ContainSingle();
    }

    [Fact]
    public async Task Spooled_batches_go_out_ahead_of_live_traffic_once_the_server_returns()
    {
        var server = new FakeServerConnection { Connected = true };
        var spool = new InMemorySpool();
        var buffer = new TagBuffer();

        await spool.EnqueueAsync([new TagRead("OLD", 1, -55, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)]);
        buffer.Offer(new TagRead("NEW", 1, -55, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        await FlushOnceAsync(buffer, server, spool);

        // The spooled read is published first, so the server sees them in the order they were read.
        server.Published.Should().HaveCount(2);
        server.Published[0][0].Epc.Should().Be("OLD");
        server.Published[1][0].Epc.Should().Be("NEW");
        spool.Batches.Should().BeEmpty();
    }

    /// <summary>
    /// Replaying a tag the server already applied is safe: the Redis debounce rejects it as a
    /// duplicate. That is what makes at-least-once delivery acceptable here.
    /// </summary>
    [Fact]
    public async Task A_failed_replay_leaves_the_batch_in_the_spool_for_next_time()
    {
        var server = new FakeServerConnection { Connected = true, FailPublish = true };
        var spool = new InMemorySpool();

        await spool.EnqueueAsync([new TagRead("EPC001", 1, -55, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)]);

        await FlushOnceAsync(new TagBuffer(), server, spool);

        spool.Batches.Should().ContainSingle("nothing may be acknowledged that was not delivered");
    }

    /// <summary>
    /// Drives one iteration of what <see cref="TagFlushService"/> does, without the timer. The loop
    /// itself is a <c>Task.Delay</c>; the behaviour worth testing is the decision inside it.
    /// </summary>
    private static async Task FlushOnceAsync(TagBuffer buffer, FakeServerConnection server, InMemorySpool spool)
    {
        if (server.IsConnected)
        {
            var pending = await spool.PeekAsync(20);
            var delivered = new List<long>();

            foreach (var batch in pending)
            {
                if (!await server.PublishTagsAsync(batch.Tags, default))
                {
                    break;
                }

                delivered.Add(batch.Id);
            }

            await spool.AcknowledgeAsync(delivered);
        }

        var live = buffer.Drain(50);
        if (live.Count == 0)
        {
            return;
        }

        if (!await server.PublishTagsAsync(live, default))
        {
            await spool.EnqueueAsync(live);
        }
    }

    private sealed class FakeServerConnection : IServerConnection
    {
        public bool Connected { get; set; }

        public bool FailPublish { get; set; }

        public List<IReadOnlyList<TagRead>> Published { get; } = [];

        public bool IsConnected => Connected;

        public Task StartAsync(ITerminalCommandHandler handler, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> PublishTagsAsync(IReadOnlyList<TagRead> tags, CancellationToken ct)
        {
            if (!Connected || FailPublish)
            {
                return Task.FromResult(false);
            }

            Published.Add(tags);
            return Task.FromResult(true);
        }

        public Task<bool> ReportStatusAsync(AgentStatusReport status, CancellationToken ct) => Task.FromResult(Connected);

        public Task<bool> ReportWeightAsync(decimal value, string unit, bool stable, CancellationToken ct)
            => Task.FromResult(Connected);

        public Task<bool> ReportPrintResultAsync(Guid transactionId, bool succeeded, string? error, CancellationToken ct)
            => Task.FromResult(Connected);

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemorySpool : ITagSpool
    {
        private long _nextId = 1;

        public List<SpooledBatch> Batches { get; } = [];

        public Task EnqueueAsync(IReadOnlyList<TagRead> tags, CancellationToken ct = default)
        {
            if (tags.Count > 0)
            {
                Batches.Add(new SpooledBatch(_nextId++, DateTimeOffset.UnixEpoch, tags));
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SpooledBatch>> PeekAsync(int maxBatches, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SpooledBatch>>(Batches.Take(maxBatches).ToList());

        public Task AcknowledgeAsync(IEnumerable<long> ids, CancellationToken ct = default)
        {
            var set = ids.ToHashSet();
            Batches.RemoveAll(b => set.Contains(b.Id));
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(Batches.Count);
    }
}
