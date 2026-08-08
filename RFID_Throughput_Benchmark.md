# RFID throughput benchmark

**Target:** sustain 5,000 raw tag reads per second from a UHF RFID D2184 without degrading the
SignalR broadcast or leaking memory.

**Verdict:** met, with roughly three orders of magnitude of headroom on the CPU side and no
retention. One second of reader traffic is processed end to end — socket bytes through the wire
codec, the agent's coalescing buffer, and the server's broadcast gate — in **0.95–1.15 ms**. The
broadcast gate's allocation is governed by how many *distinct* tags are in the field, not by how many
*reads* arrive, which is the property the 5,000/sec target actually depends on.

---

## What was measured

Three stages, benchmarked separately because they fail in different ways, plus the two composites.

| Stage | Component | Failure mode it is watched for |
|---|---|---|
| Wire | `UhfSerialCodec.FrameReassembler` + `InventoryFrameParser` | Allocation per frame; resync cost when frames straddle packet boundaries |
| Agent | `TagBuffer` | Coalescing that costs per read instead of per tag |
| Server | `TagStreamDebouncer` | Unbounded growth; lock contention across four antennas |

The debouncer is the component built for this brief. It is a lock-free `ConcurrentDictionary` keyed
on EPC with a one-second window, sitting between tag ingestion and the `/hubs/rfid` broadcast. Its
job is to turn 5,000 reads a second into one frame per tag per second.

### Test conditions

- **Machine:** Intel Core i5-4590 @ 3.30 GHz, 4 cores / 4 threads, Windows 10 Pro 19045
- **Runtime:** .NET 8.0, Release, `RyuJIT AVX2`, workstation GC
- **Harness:** BenchmarkDotNet 0.14.0, `MemoryDiagnoser` on throughout
- **Source:** `backend/benchmarks/Retail25.Benchmarks/`

Reads are **interleaved, not grouped** — consecutive reads are different tags, because a reader
sweeps its field rather than dwelling on one tag. That is the access pattern that actually exercises
the dictionary. The wire stream is built as genuine `0xA0`-framed real-time-inventory responses
(`FreqAnt` / `PC` / 96-bit `EPC` / `RSSI` / checksum) and fed in 1,460-byte chunks, one Ethernet
payload at a time, so frames straddle chunk boundaries exactly as they do over TCP.

---

## Results: one second of traffic (5,000 reads)

### 120 tags in the field — a rail of garments, ~42 reads per tag per second

| Stage | Mean | Allocated |
|---|---:|---:|
| Wire: parse 5,000 tag frames | 329.1 µs | 951.77 KB |
| Agent: buffer and drain 5,000 reads | 462.6 µs | 375.59 KB |
| **Server: debounce 5,000 reads (1 s window)** | **309.6 µs** | **19.26 KB** |
| Server: debounce across 4 antennas (parallel) | 430.1 µs | 21.45 KB |
| End to end: wire → agent buffer → debounce | 954.4 µs | 1,698.22 KB |

### 500 tags in the field — a stockroom sweep

| Stage | Mean | Allocated |
|---|---:|---:|
| Wire: parse 5,000 tag frames | 330.0 µs | 951.77 KB |
| Agent: buffer and drain 5,000 reads | 509.4 µs | 455.65 KB |
| **Server: debounce 5,000 reads (1 s window)** | **331.1 µs** | **51.91 KB** |
| Server: debounce across 4 antennas (parallel) | 455.7 µs | 54.11 KB |
| End to end: wire → agent buffer → debounce | 1,151.4 µs | 1,810.94 KB |

### Reading these numbers

**The headroom is the end-to-end row.** One second of reader output costs about **one millisecond**
of one core. The pipeline is running at roughly **0.1% duty cycle** at the target rate — around 870×
headroom before the reader could outpace it. A D2184 physically cannot produce enough reads to
saturate this.

**The debounce allocation is the important column.** At 120 distinct tags it allocates 19.26 KB per
5,000 reads; at 500 tags, 51.91 KB. That is a **4.16× rise in allocation for a 4.17× rise in tag
count** — dead-on proportional to distinct tags, and flat in the number of reads. Concretely: the
first read of a tag allocates a slot (~105 bytes including the dictionary node and the interned EPC
reference); every subsequent read of that tag in the same window allocates **nothing at all**, and is
a dictionary lookup plus two interlocked writes.

That is what makes the target reachable. If the gate allocated per read rather than per tag, 5,000
reads a second would be ~500 KB/s of garbage on the hot path — survivable for a minute, and the
reason a busy shop floor gets slower through the afternoon.

**Four antennas cost 39% more wall-clock than one thread, not 4× less.** The parallel row is *slower*
than the single-threaded one, and that is the expected and correct result at this scale: 5,000
operations is far too little work to amortise thread dispatch. What matters is that it is only 39%
slower and allocates only 11% more — there is no lock convoy. A `lock`-based gate would show the four
antennas serialising, and it does not. The compare-and-swap on the window is doing its job.

**The wire codec is the most expensive stage by allocation** (951 KB/s), and it is the one with real
optimisation headroom left — `FrameReassembler.Push` copies its buffer on every call. It is not worth
doing yet: 951 KB/s is comfortably inside the Gen0 budget and the stage is only a third of a
millisecond. Noted rather than fixed.

---

## Memory: a trading hour

The per-operation figures above cannot answer the question that actually matters for an RFID gate —
*does it still work after an hour?* A dictionary keyed on EPC is the textbook slow leak: every read
is fast, and the working set only ever grows.

Two runs of **18 million reads** (5,000/sec × 3,600 sec), time-compressed:

| Scenario | Mean | Allocated |
|---|---:|---:|
| A trading hour: 60,000 tags pass through a field holding 200 | 1.133 s | 5,211.34 KB |
| Control: 18M reads, the same 200 tags never leave | 1.071 s | 53.65 KB |

**The control is the zero-allocation proof.** Eighteen million reads against a static field of 200
tags allocate **53.65 KB in total** — that is the 200 slots, once, and then nothing. 17,999,800
repeat reads allocated nothing whatsoever.

**The trading hour is the eviction proof.** Sixty thousand different tags pass through, and total
allocation is 5.2 MB — about 87 bytes per distinct tag ever seen. Those allocations are *transient*:
they are swept and collected as tags leave the field, not retained. The regression guard for this is
a unit test rather than a benchmark, since it asserts on state rather than time:

> `TagStreamDebouncerTests.A_field_that_turns_over_does_not_accumulate_tags` — 20,000 tags through a
> field of 200, asserting the debouncer holds fewer than 2,000 at the end.

Both directions of failure are visible in the pair. A debouncer that never evicted would show the
trading-hour row retaining 60,000 slots. One that evicted too aggressively would show the *control*
allocating far more than 53 KB, because tags would be re-admitted inside their own window. Neither
happens.

---

## Correctness under load

Throughput is worthless if the gate lets the same tag through twice, so the concurrency properties
are asserted rather than benchmarked. From `TagStreamDebouncerTests`:

| Property | Test |
|---|---|
| 20,000 concurrent reads of one EPC admit **exactly once** | `Concurrent_reads_of_one_tag_admit_exactly_once` |
| 50,000 concurrent reads over 500 EPCs admit each exactly once — none lost, none doubled | `Concurrent_reads_of_many_tags_admit_each_exactly_once` |
| The window genuinely reopens once elapsed | `The_window_reopens_once_it_has_elapsed` |
| Stale tags are swept without a manual call | `The_sweep_happens_on_its_own_as_tags_keep_arriving` |

The second of these is the one that would catch a torn read-modify-write on a slot — the defect a
`struct` value in a `ConcurrentDictionary` would have produced, where interlocked increments land on
a copy and silently vanish.

Timing throughout comes from `Stopwatch.GetTimestamp()`, not the wall clock. A monotonic source is
not optional here: an NTP correction or a daylight-saving step against `DateTimeOffset.UtcNow` would
either open the gate for every tag at once or wedge it shut for an hour.

---

## Effect on the SignalR broadcast

This is the point of the exercise. Frames actually sent to `/hubs/rfid` per second of reader traffic:

| | Without the gate | With the gate |
|---|---:|---:|
| 120 tags in the field | 5,000 | 120 |
| 500 tags in the field | 5,000 | 500 |

A **42× reduction** in the 120-tag case and 10× at 500. Each surviving observation carries the folded
`ReadCount`, so nothing is lost — the screen still knows a tag was seen 42 times, it is simply told
once.

The same gate now also governs the rejection path. `IngestTagReadsHandler` previously emitted one
`CartLineRejected` notification per raw read when no sale was open, which meant a reader pointed at a
full rail with an idle till produced its own 5,000-frame-per-second flood in a different pipe. That
now runs over the debounced list. It was found by writing this benchmark, not by the benchmark
itself, but it is the same defect class and worth recording.

---

## Reproducing

```bash
dotnet run -c Release --project backend/benchmarks/Retail25.Benchmarks -- --filter "*RfidThroughput*"
```

```bash
dotnet run -c Release --project backend/benchmarks/Retail25.Benchmarks -- --filter "*Sustained*"
```

Wall-clock: about 2.5 minutes and 45 seconds respectively.

---

## What this does not prove

Stated plainly, because a benchmark report that only lists its successes is not evidence.

- **No physical D2184 was attached.** There is no reader at `192.168.0.178:4001` on this machine. The
  wire stage is driven by a synthesised byte stream built to the R2000-family protocol
  (`UHF RFID Reader Serial Interface Protocol` v3.1, §2.2.8), fed through the real
  `FrameReassembler` and the real `InventoryFrameParser`. That proves the codec handles the framing,
  the checksums and the packet-boundary resync. It does not prove the reader emits what the
  specification says, that four-antenna fast polling produces the read rate assumed here, or that the
  TCP session survives a working day. **Hardware-in-the-loop verification requires the device.**
- **The SignalR fan-out itself is not measured.** What is measured is how many frames the hub is
  *asked* to send. The cost of the hub delivering them to N connected clients is a separate question,
  and belongs with the load test in Phase 8 rather than here.
- **One machine, one topology.** These are single-process figures on a four-core desktop. Multi-till
  contention on the shared Redis debouncer — the *other* debouncer, which arbitrates which till may
  sell a tag — is a network-bound question this suite does not touch.
- **The hour is compressed.** The 18-million-read runs shorten the window rather than waiting an
  hour. That preserves the ratio being tested (reads per window, tags entering and leaving) but not
  the absolute elapsed time, so it says nothing about, say, a slow leak driven by wall-clock rather
  than by traffic.
