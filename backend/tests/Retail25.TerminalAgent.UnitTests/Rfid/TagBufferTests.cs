using FluentAssertions;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// The agent-side coalescing window (doc 06 §2). Its whole job is to turn the reader's firehose into
/// something worth a round trip, so the properties under test are: one entry per tag, counts that
/// accumulate, and nothing lost when the batch is capped.
/// </summary>
public sealed class TagBufferTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_tag_read_many_times_becomes_one_entry()
    {
        var buffer = new TagBuffer();

        // A reader reports a tag around twenty times a second. Twenty entries would be twenty lines.
        for (var i = 0; i < 20; i++)
        {
            buffer.Offer(Read("EPC001", rssi: -60, readCount: 1, offsetMs: i * 50));
        }

        var drained = buffer.Drain(50);

        drained.Should().ContainSingle();
        drained[0].ReadCount.Should().Be(20, "the reads accumulate rather than being discarded");
    }

    /// <summary>
    /// The strongest read wins, because it is the one most likely to have come from the basket rather
    /// than from the shelf behind the till — and the antenna follows the signal, so zoning is judged
    /// on the read that actually mattered.
    /// </summary>
    [Fact]
    public void The_strongest_read_supplies_the_signal_and_the_antenna()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read("EPC001", rssi: -80, readCount: 1, antenna: 3));
        buffer.Offer(Read("EPC001", rssi: -45, readCount: 1, antenna: 1));
        buffer.Offer(Read("EPC001", rssi: -70, readCount: 1, antenna: 2));

        var tag = buffer.Drain(10).Single();

        tag.Rssi.Should().Be(-45);
        tag.Antenna.Should().Be(1);
    }

    [Fact]
    public void First_and_last_seen_span_the_whole_window()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read("EPC001", offsetMs: 100));
        buffer.Offer(Read("EPC001", offsetMs: 0));
        buffer.Offer(Read("EPC001", offsetMs: 250));

        var tag = buffer.Drain(10).Single();

        tag.FirstSeen.Should().Be(Now);
        tag.LastSeen.Should().Be(Now.AddMilliseconds(250));
    }

    [Fact]
    public void Draining_empties_what_it_took_and_leaves_the_rest()
    {
        var buffer = new TagBuffer();

        for (var i = 0; i < 10; i++)
        {
            buffer.Offer(Read($"EPC{i:D3}"));
        }

        var first = buffer.Drain(4);
        var second = buffer.Drain(100);

        first.Should().HaveCount(4);
        second.Should().HaveCount(6);
        buffer.Count.Should().Be(0);

        // Nothing is duplicated across the two batches, and nothing is lost.
        first.Concat(second).Select(t => t.Epc).Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    /// <summary>
    /// A capped batch takes the oldest tags. Otherwise a tag that arrived first could sit in the
    /// window indefinitely while newer reads keep jumping ahead of it.
    /// </summary>
    [Fact]
    public void A_capped_batch_takes_the_oldest_tags_first()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read("OLDEST", offsetMs: 0));
        buffer.Offer(Read("MIDDLE", offsetMs: 100));
        buffer.Offer(Read("NEWEST", offsetMs: 200));

        buffer.Drain(1).Single().Epc.Should().Be("OLDEST");
    }

    [Fact]
    public void Tags_are_normalised_so_case_never_splits_one_tag_into_two()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read("abc123"));
        buffer.Offer(Read("ABC123"));

        var drained = buffer.Drain(10);

        drained.Should().ContainSingle();
        drained[0].Epc.Should().Be("ABC123");
    }

    [Fact]
    public void The_read_rate_resets_when_it_is_sampled()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read("EPC001"));
        buffer.Offer(Read("EPC001"));
        buffer.Offer(Read("EPC002"));

        buffer.ResetRate().Should().Be(3, "the rate counts raw reads, not distinct tags");
        buffer.ResetRate().Should().Be(0);
    }

    /// <summary>
    /// The reader pump and the flush timer run on different threads, so concurrent offers and drains
    /// must not lose a tag or double-count one.
    /// </summary>
    [Fact]
    public async Task Concurrent_offers_and_drains_neither_lose_nor_duplicate_tags()
    {
        var buffer = new TagBuffer();
        var drained = new System.Collections.Concurrent.ConcurrentBag<string>();

        var producers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                buffer.Offer(Read($"EPC{worker:D2}{i:D4}"));
            }
        }));

        var draining = true;
        var consumer = Task.Run(async () =>
        {
            while (Volatile.Read(ref draining))
            {
                foreach (var tag in buffer.Drain(37))
                {
                    drained.Add(tag.Epc);
                }

                await Task.Delay(1);
            }
        });

        await Task.WhenAll(producers);
        Volatile.Write(ref draining, false);
        await consumer;

        foreach (var tag in buffer.Drain(int.MaxValue))
        {
            drained.Add(tag.Epc);
        }

        drained.Should().HaveCount(1000);
        drained.Should().OnlyHaveUniqueItems();
    }

    private static TagRead Read(
        string epc,
        int rssi = -55,
        int readCount = 1,
        int antenna = 1,
        int offsetMs = 0)
        => new(epc, antenna, rssi, readCount, Now.AddMilliseconds(offsetMs), Now.AddMilliseconds(offsetMs));
}
