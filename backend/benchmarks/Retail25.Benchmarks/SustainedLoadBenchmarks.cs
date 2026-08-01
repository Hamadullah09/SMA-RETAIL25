using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Retail25.Application.Rfid;

namespace Retail25.Benchmarks;

/// <summary>
/// The question a per-operation benchmark cannot answer: does it still work after an hour?
/// <para>
/// A debouncer keyed on EPC is the classic slow leak. Every read costs almost nothing and the
/// dictionary only ever grows, so it benchmarks beautifully and then falls over on a Saturday. These
/// measure the working set, not the speed — the number to look at is the allocation column and the
/// tag count at the end, both of which must be governed by what is in the field now rather than by
/// everything the reader has ever seen.
/// </para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class SustainedLoadBenchmarks
{
    /// <summary>
    /// A trading hour at the target rate: 5,000 reads a second for 3,600 seconds.
    /// <para>
    /// Compressed — no real waiting, the window is shortened instead — because a benchmark that takes
    /// an hour is a benchmark nobody runs. What is preserved is the ratio that matters: reads far
    /// outnumbering windows, and tags leaving the field as new ones arrive.
    /// </para>
    /// </summary>
    private const int Seconds = 3_600;
    private const int ReadsPerSecond = 5_000;

    /// <summary>
    /// How many tags are in the field at any moment, and how many pass through in total.
    /// <para>
    /// 200 at a time out of 60,000 over the hour is a shop floor: stock arrives, sells, and leaves.
    /// The gap between those two numbers is exactly the leak this measures — a debouncer that never
    /// evicts ends the hour holding 60,000 slots instead of 200.
    /// </para>
    /// </summary>
    private const int TagsInFieldAtOnce = 200;
    private const int TagsOverTheHour = 60_000;

    private string[] _epcs = [];

    [GlobalSetup]
    public void Setup()
        => _epcs = Enumerable.Range(0, TagsOverTheHour).Select(Sgtin96).ToArray();

    /// <summary>
    /// Eighteen million reads through the gate, with the field turning over as it goes.
    /// </summary>
    /// <returns>
    /// Tags still held at the end. The assertion the report makes is that this is on the order of
    /// <see cref="TagsInFieldAtOnce"/> and not of <see cref="TagsOverTheHour"/>.
    /// </returns>
    [Benchmark(Description = "A trading hour: 18M reads, field turning over")]
    public int ATradingHour()
    {
        // A short window so the compressed run still opens and closes windows the way a real hour
        // would. With a one-second window and no real time passing, every tag would sit in its first
        // window forever and nothing would ever be swept — which would measure nothing.
        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(1), capacityHint: 1_024);

        for (var second = 0; second < Seconds; second++)
        {
            // The window of tags currently in front of the antenna, sliding through the catalogue.
            var first = second * TagsOverTheHour / Seconds;

            for (var read = 0; read < ReadsPerSecond; read++)
            {
                debouncer.TryAdmit(_epcs[(first + (read % TagsInFieldAtOnce)) % TagsOverTheHour]);
            }
        }

        // What is left after the amortised sweeps have had their say.
        return debouncer.TagsInField;
    }

    /// <summary>
    /// The control: the same eighteen million reads with nothing ever leaving the field.
    /// <para>
    /// Not a scenario anyone runs, but it is the honest upper bound — the memory cost of a shop where
    /// every tag genuinely stays in range all hour. If this one is also small, the sweep is too
    /// aggressive and tags are being re-admitted; both failure directions are visible here.
    /// </para>
    /// </summary>
    [Benchmark(Description = "Control: 18M reads, nothing leaves the field")]
    public int AStaticField()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromMilliseconds(1), capacityHint: 1_024);

        for (var second = 0; second < Seconds; second++)
        {
            for (var read = 0; read < ReadsPerSecond; read++)
            {
                debouncer.TryAdmit(_epcs[read % TagsInFieldAtOnce]);
            }
        }

        return debouncer.TagsInField;
    }

    private static string Sgtin96(int serial)
    {
        var high = (0x30UL << 56) | (3UL << 53) | (5UL << 50) | (9_521_234UL << 26) | (250UL << 6);

        return high.ToString("X16", CultureInfo.InvariantCulture) + ((uint)serial).ToString("X8", CultureInfo.InvariantCulture);
    }
}
