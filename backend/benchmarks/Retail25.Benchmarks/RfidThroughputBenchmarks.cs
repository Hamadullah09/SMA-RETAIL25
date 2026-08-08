using System.Buffers.Binary;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Retail25.Application.Rfid;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Rfid;

namespace Retail25.Benchmarks;

/// <summary>
/// What happens to the tag pipeline when a D2184 is driven flat out.
/// <para>
/// The target is 5,000 raw reads a second. That is not an arbitrary number: four antennas in fast
/// polling, a rail of tagged garments in the field, and each tag re-read tens of times a second per
/// antenna. The question these benchmarks answer is not "is it fast" — almost anything is fast at
/// 5,000 operations a second — but <em>where the work is proportional to</em>. Anything on the hot
/// path that scales with the raw read count instead of the distinct tag count is what turns a busy
/// shop floor into a stalled till.
/// </para>
/// <para>
/// Three stages are measured separately because they fail differently:
/// </para>
/// <list type="number">
///   <item>the wire codec, which turns bytes into frames — allocation-sensitive;</item>
///   <item>the agent's buffer, which coalesces per EPC before anything leaves the machine;</item>
///   <item>the server's debouncer, which decides what reaches the SignalR broadcast.</item>
/// </list>
/// <para>
/// <see cref="MemoryDiagnoser"/> is on throughout. A leak here would not announce itself — it would
/// look like a shop that gets slower over a trading day, which is the hardest kind of fault to
/// attribute after the fact.
/// </para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
public class RfidThroughputBenchmarks
{
    /// <summary>One second of a reader at the target rate.</summary>
    public const int ReadsPerSecond = 5_000;

    /// <summary>
    /// How many different tags are in the field.
    /// <para>
    /// 5,000 reads spread over 120 tags is roughly 42 reads per tag per second, which is what four
    /// antennas sweeping a full rail actually produces. The ratio is the whole point: a pipeline that
    /// costs the same at 120 distinct tags as at 5,000 is one that is not coalescing.
    /// </para>
    /// </summary>
    [Params(120, 500)]
    public int DistinctTags { get; set; }

    private string[] _epcs = [];
    private TagRead[] _reads = [];
    private byte[] _wireBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _epcs = Enumerable.Range(0, DistinctTags).Select(Sgtin96).ToArray();

        // Interleaved rather than grouped. A reader does not deliver all of tag A then all of tag B;
        // it sweeps the field, so consecutive reads are almost always different tags — which is the
        // access pattern that actually exercises the dictionary.
        _reads = Enumerable.Range(0, ReadsPerSecond)
            .Select(i => new TagRead(
                _epcs[i % DistinctTags],
                Antenna: (i / DistinctTags) % 4 + 1,
                Rssi: -40 - (i % 25),
                ReadCount: 1,
                FirstSeen: DateTimeOffset.UnixEpoch,
                LastSeen: DateTimeOffset.UnixEpoch))
            .ToArray();

        _wireBytes = BuildWireStream(_reads);
    }

    // ---------------------------------------------------------------------------------------------
    // 1. The wire
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A full second of reader output, from raw bytes to parsed tags.
    /// <para>
    /// Fed as one large buffer rather than frame by frame, because that is what a socket delivers —
    /// the reassembler's job is precisely to cope with reads that split and coalesce frames wherever
    /// the network felt like it.
    /// </para>
    /// </summary>
    [Benchmark(Description = "Wire: parse 5,000 tag frames")]
    public int ParseWireStream()
    {
        var reassembler = new UhfSerialCodec.FrameReassembler();
        var parsed = 0;

        // Chunked at 1,460 bytes — one Ethernet payload, so frames straddle chunk boundaries exactly
        // as they do in production. Handing it the whole array at once would skip the resync path.
        for (var offset = 0; offset < _wireBytes.Length; offset += 1_460)
        {
            var length = Math.Min(1_460, _wireBytes.Length - offset);

            foreach (var frame in reassembler.Push(_wireBytes.AsSpan(offset, length)))
            {
                if (InventoryFrameParser.Classify(frame) == InventoryFrameKind.Tag)
                {
                    _ = InventoryFrameParser.ParseTag(frame);
                    parsed++;
                }
            }
        }

        return parsed;
    }

    // ---------------------------------------------------------------------------------------------
    // 2. The agent's buffer
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Coalescing on the till itself, before anything is sent.
    /// <para>
    /// This is the first place the 5,000 collapses. Whatever comes out of here is what crosses the
    /// network, so its cost has to track the tag count, not the read count.
    /// </para>
    /// </summary>
    [Benchmark(Description = "Agent: buffer and drain 5,000 reads")]
    public int BufferAndDrain()
    {
        var buffer = new TagBuffer();

        foreach (var read in _reads)
        {
            buffer.Offer(read);
        }

        return buffer.Drain(maxBatchSize: 1_000).Count;
    }

    // ---------------------------------------------------------------------------------------------
    // 3. The server's broadcast gate
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The gate in front of SignalR, single-threaded.
    /// <para>
    /// The number that matters is the allocation column. Every read after the first for a given tag
    /// must cost zero bytes — otherwise the broadcast gate is itself the garbage the till spends its
    /// afternoon collecting.
    /// </para>
    /// </summary>
    [Benchmark(Description = "Server: debounce 5,000 reads (1s window)", Baseline = true)]
    public int Debounce()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(1), capacityHint: 1_024);
        var admitted = 0;

        foreach (var read in _reads)
        {
            if (debouncer.TryAdmit(read.Epc))
            {
                admitted++;
            }
        }

        return admitted;
    }

    /// <summary>
    /// The same second of traffic, arriving on four threads.
    /// <para>
    /// This is the realistic shape — four antennas, four readers, one gate — and it is where a
    /// lock-based implementation would show its contention. Comparing it against the single-threaded
    /// case above is the whole reason the compare-and-swap is written the way it is.
    /// </para>
    /// </summary>
    [Benchmark(Description = "Server: debounce 5,000 reads across 4 antennas")]
    public int DebounceConcurrently()
    {
        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(1), capacityHint: 1_024);
        var admitted = 0;

        Parallel.For(0, 4, antenna =>
        {
            var local = 0;

            for (var i = antenna; i < _reads.Length; i += 4)
            {
                if (debouncer.TryAdmit(_reads[i].Epc))
                {
                    local++;
                }
            }

            Interlocked.Add(ref admitted, local);
        });

        return admitted;
    }

    /// <summary>
    /// A full second end to end: bytes off the socket, through the agent's buffer, through the
    /// server's gate. What is left is what SignalR is asked to send.
    /// </summary>
    [Benchmark(Description = "End to end: wire → agent buffer → debounce")]
    public int EndToEnd()
    {
        var reassembler = new UhfSerialCodec.FrameReassembler();
        var buffer = new TagBuffer();

        for (var offset = 0; offset < _wireBytes.Length; offset += 1_460)
        {
            var length = Math.Min(1_460, _wireBytes.Length - offset);

            foreach (var frame in reassembler.Push(_wireBytes.AsSpan(offset, length)))
            {
                if (InventoryFrameParser.Classify(frame) != InventoryFrameKind.Tag)
                {
                    continue;
                }

                var tag = InventoryFrameParser.ParseTag(frame);

                buffer.Offer(new TagRead(
                    tag.Epc,
                    tag.RawAntenna,
                    tag.RssiDbm,
                    ReadCount: 1,
                    FirstSeen: DateTimeOffset.UnixEpoch,
                    LastSeen: DateTimeOffset.UnixEpoch));
            }
        }

        var debouncer = new TagStreamDebouncer(TimeSpan.FromSeconds(1), capacityHint: 1_024);
        var broadcast = 0;

        foreach (var read in buffer.Drain(maxBatchSize: 5_000))
        {
            if (debouncer.TryAdmit(read.Epc))
            {
                broadcast++;
            }
        }

        return broadcast;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the byte stream a reader would emit for these reads.
    /// <para>
    /// A real <c>0x89</c> real-time inventory frame: <c>A0 Len Addr Cmd FreqAnt PC[2] EPC[12] RSSI
    /// Checksum</c>. Built for real rather than approximated, so the benchmark measures the codec
    /// rather than a simplification of it.
    /// </para>
    /// </summary>
    private static byte[] BuildWireStream(TagRead[] reads)
    {
        const byte FrameHead = 0xA0;
        const byte Address = 0x01;
        const byte RealTimeInventory = 0x89;

        var stream = new List<byte>(reads.Length * 24);

        foreach (var read in reads)
        {
            var epc = Convert.FromHexString(read.Epc);

            // Data is FreqAnt(1) + PC(2) + EPC(n) + RSSI(1); Len counts Addr, Cmd, Data and Checksum.
            var data = new byte[1 + 2 + epc.Length + 1];

            // Frequency in the high five bits, antenna in the low three — the reader's own packing.
            data[0] = (byte)(((read.Antenna - 1) & 0x03) | (0x08 << 3));

            // PC word for a 96-bit EPC: length 6 words in the top five bits.
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1, 2), 6 << 11);

            epc.CopyTo(data.AsSpan(3));
            data[^1] = (byte)(read.Rssi + 129);

            var length = (byte)(data.Length + 3);
            var frame = new byte[length + 2];

            frame[0] = FrameHead;
            frame[1] = length;
            frame[2] = Address;
            frame[3] = RealTimeInventory;
            data.CopyTo(frame.AsSpan(4));
            frame[^1] = UhfSerialCodec.Checksum(frame.AsSpan(0, frame.Length - 1));

            stream.AddRange(frame);
        }

        return stream.ToArray();
    }

    /// <summary>A well-formed 96-bit SGTIN, so the codec sees the EPC length it expects.</summary>
    private static string Sgtin96(int serial)
    {
        var high = (0x30UL << 56) | (3UL << 53) | (5UL << 50) | (9_521_234UL << 26) | (250UL << 6);

        return high.ToString("X16", CultureInfo.InvariantCulture) + ((uint)serial).ToString("X8", CultureInfo.InvariantCulture);
    }
}
