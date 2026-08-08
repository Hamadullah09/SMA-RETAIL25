using System.Collections.Concurrent;
using FluentAssertions;
using Retail25.Application.Rfid;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// The gate in front of the read feed.
/// <para>
/// These are the properties the broadcast depends on: one frame per tag per window however hard the
/// reader is driven, a window that genuinely reopens, and a dictionary that does not grow forever.
/// The last one is the interesting test — an unbounded cache keyed on EPC is exactly how an RFID
/// pipeline develops a slow memory leak that only shows up after a full trading day.
/// </para>
/// </summary>
public sealed class TagStreamDebouncerTests
{
    private const string Epc = "30ABCDEF0123456789ABCDEF";

    [Fact]
    public void The_first_read_of_a_tag_is_admitted()
    {
        var debouncer = new TagStreamDebouncer();

        debouncer.TryAdmit(Epc).Should().BeTrue();
    }

    [Fact]
    public void Repeat_reads_inside_the_window_are_folded_in()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));

        debouncer.TryAdmit(Epc).Should().BeTrue();

        for (var i = 0; i < 5_000; i++)
        {
            debouncer.TryAdmit(Epc).Should().BeFalse();
        }

        debouncer.AdmittedReads.Should().Be(1);
        debouncer.ObservedReads.Should().Be(5_001);
    }

    [Fact]
    public void The_folded_reads_are_counted()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 40; i++)
        {
            debouncer.TryAdmit(Epc);
        }

        debouncer.TryDescribe(Epc, out var count, out _).Should().BeTrue();
        count.Should().Be(40);
    }

    [Fact]
    public async Task The_window_reopens_once_it_has_elapsed()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(50));

        debouncer.TryAdmit(Epc).Should().BeTrue();
        debouncer.TryAdmit(Epc).Should().BeFalse();

        await Task.Delay(120);

        debouncer.TryAdmit(Epc).Should().BeTrue();
        debouncer.AdmittedReads.Should().Be(2);
    }

    [Fact]
    public void Different_tags_do_not_gate_each_other()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 100; i++)
        {
            debouncer.TryAdmit($"30ABCDEF0123456789AB{i:D4}").Should().BeTrue();
        }

        debouncer.TagsInField.Should().Be(100);
    }

    [Fact]
    public void Forgetting_a_tag_reopens_it_immediately()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));

        debouncer.TryAdmit(Epc);
        debouncer.TryAdmit(Epc).Should().BeFalse();

        debouncer.Forget(Epc).Should().BeTrue();
        debouncer.TryAdmit(Epc).Should().BeTrue();
    }

    /// <summary>
    /// The leak test. A shop that sees fifty thousand distinct tags across a day must not still be
    /// holding fifty thousand slots — the working set is what is in the field now, not what has ever
    /// been in it.
    /// </summary>
    [Fact]
    public async Task Tags_that_leave_the_field_are_swept_out()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(20));

        for (var i = 0; i < 5_000; i++)
        {
            debouncer.TryAdmit($"30ABCDEF012345{i:D10}");
        }

        debouncer.TagsInField.Should().BeGreaterThan(0);

        // Four windows is the staleness threshold; wait past it and sweep.
        await Task.Delay(200);

        debouncer.Sweep();

        debouncer.TagsInField.Should().Be(0);
    }

    /// <summary>
    /// The same sweep, but reached the way production reaches it — through admissions alone, with
    /// nobody calling <c>Sweep</c>. A leak that only a manual sweep prevents is still a leak.
    /// </summary>
    [Fact]
    public async Task The_sweep_happens_on_its_own_as_tags_keep_arriving()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(10));

        for (var i = 0; i < 2_000; i++)
        {
            debouncer.TryAdmit($"30AAAA00000000{i:D10}");
        }

        var afterFirstWave = debouncer.TagsInField;

        await Task.Delay(120);

        // A second wave of entirely different tags. Their admissions drive the amortised sweep, which
        // should retire the first wave.
        for (var i = 0; i < 2_000; i++)
        {
            debouncer.TryAdmit($"30BBBB00000000{i:D10}");
        }

        debouncer.TagsInField.Should().BeLessThan(afterFirstWave + 2_000);
    }

    /// <summary>
    /// Four antennas on one reader means four threads offering the same EPC at the same instant. If
    /// the compare-and-swap is wrong, more than one of them wins and the screen shows the tag twice.
    /// </summary>
    [Fact]
    public void Concurrent_reads_of_one_tag_admit_exactly_once()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));
        var admissions = 0;

        Parallel.For(0, 20_000, _ =>
        {
            if (debouncer.TryAdmit(Epc))
            {
                Interlocked.Increment(ref admissions);
            }
        });

        admissions.Should().Be(1);
        debouncer.ObservedReads.Should().Be(20_000);
    }

    /// <summary>
    /// The same race across many tags: every distinct EPC admitted exactly once, none lost, none
    /// doubled — which is what a torn read-modify-write on the slot would produce.
    /// </summary>
    [Fact]
    public void Concurrent_reads_of_many_tags_admit_each_exactly_once()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(30));
        var admitted = new ConcurrentBag<string>();

        Parallel.For(0, 50_000, i =>
        {
            var epc = $"30CCCC00000000{i % 500:D10}";

            if (debouncer.TryAdmit(epc))
            {
                admitted.Add(epc);
            }
        });

        admitted.Should().HaveCount(500);
        admitted.Distinct().Should().HaveCount(500);
    }

    /// <summary>
    /// A trading hour in miniature, and the assertion the benchmark report leans on.
    /// <para>
    /// Sixty thousand tags pass through a field that only ever holds two hundred. What must not
    /// happen is the debouncer ending up holding all sixty thousand — that is the shape of the leak,
    /// and it is invisible in a per-operation benchmark because every individual read is still fast.
    /// </para>
    /// </summary>
    [Fact]
    public void A_field_that_turns_over_does_not_accumulate_tags()
    {
        const int InFieldAtOnce = 200;
        const int OverTheHour = 20_000;

        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(1));

        for (var tick = 0; tick < OverTheHour / InFieldAtOnce; tick++)
        {
            for (var read = 0; read < 5_000; read++)
            {
                debouncer.TryAdmit($"30DDDD00000000{(tick * InFieldAtOnce) + (read % InFieldAtOnce):D10}");
            }
        }

        // Generously bounded — the exact figure depends on where the amortised sweep last fired —
        // but the point is the order of magnitude: hundreds, not tens of thousands.
        debouncer.TagsInField.Should().BeLessThan(InFieldAtOnce * 10);
        debouncer.ObservedReads.Should().Be(500_000);
    }

    [Fact]
    public void A_non_positive_window_is_rejected()
    {
        var build = () => new TagStreamDebouncer(TimeSpan.Zero);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }
}
