using FluentAssertions;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// Keeping two readers' observations apart.
/// <para>
/// The window used to be keyed on the tag alone, which was correct while a machine drove one reader.
/// It stops being correct the moment one machine watches several doorways: the same garment carried
/// past two of them is two observations of two different places, and folding them into one entry
/// loses whichever reader was second — the read then goes to the wrong station, or to none.
/// </para>
/// </summary>
public sealed class TagBufferMultiReaderTests
{
    private const string Epc = "E28011700000020A7A6B6AE1";

    private static TagRead Read(int antenna = 1, int rssi = -50, int count = 1)
    {
        var now = DateTimeOffset.UtcNow;
        return new TagRead(Epc, antenna, rssi, count, now, now);
    }

    [Fact]
    public void The_same_tag_seen_by_two_readers_stays_two_observations()
    {
        var buffer = new TagBuffer();

        buffer.Offer(1, Read());
        buffer.Offer(2, Read());

        buffer.Count.Should().Be(2, "one garment past two doorways is two facts, not one");

        var batches = buffer.DrainByReader(int.MaxValue);

        batches.Should().HaveCount(2);
        batches.Select(b => b.ReaderId).Should().BeEquivalentTo(new long[] { 1, 2 });
        batches.Should().OnlyContain(b => b.Tags.Count == 1);
    }

    /// <summary>The same reader seeing a tag twice still folds, which is the whole point of the window.</summary>
    [Fact]
    public void One_reader_seeing_a_tag_twice_still_folds_it()
    {
        var buffer = new TagBuffer();

        buffer.Offer(1, Read(count: 3));
        buffer.Offer(1, Read(count: 4));

        buffer.Count.Should().Be(1);

        var batch = buffer.DrainByReader(int.MaxValue).Single();

        batch.Tags.Single().ReadCount.Should().Be(7, "read counts accumulate across the window");
    }

    /// <summary>
    /// An agent still running on the per-station profile reports reader 0, and those batches go out
    /// by station as they always did. That is what lets an estate be upgraded one till at a time.
    /// </summary>
    [Fact]
    public void An_unregistered_reader_is_reported_as_zero()
    {
        var buffer = new TagBuffer();

        buffer.Offer(Read());

        buffer.DrainByReader(int.MaxValue).Single().ReaderId.Should().Be(0);
    }

    /// <summary>
    /// The cap covers the drain as a whole. It exists to stop one request growing large enough to
    /// time out, and three readers each sending a capped batch would defeat that.
    /// </summary>
    [Fact]
    public void The_batch_cap_applies_across_readers_not_per_reader()
    {
        var buffer = new TagBuffer();

        for (var i = 0; i < 5; i++)
        {
            buffer.Offer(1, Read() with { Epc = $"E28011700000020A7A6B6A{i:D2}" });
            buffer.Offer(2, Read() with { Epc = $"E28011700000020A7A6B6B{i:D2}" });
        }

        var batches = buffer.DrainByReader(4);

        batches.Sum(b => b.Tags.Count).Should().Be(4);
        buffer.Count.Should().Be(6, "the remainder waits for the next tick rather than being dropped");
    }

    /// <summary>Draining one reader's tags must not take another's with it.</summary>
    [Fact]
    public void Draining_removes_only_what_it_returned()
    {
        var buffer = new TagBuffer();

        buffer.Offer(1, Read());
        buffer.Offer(2, Read());

        var first = buffer.DrainByReader(1);

        first.Should().ContainSingle();
        buffer.Count.Should().Be(1);

        var second = buffer.DrainByReader(int.MaxValue);

        second.Should().ContainSingle();
        second[0].ReaderId.Should().NotBe(first[0].ReaderId);
        buffer.Count.Should().Be(0);
    }
}
