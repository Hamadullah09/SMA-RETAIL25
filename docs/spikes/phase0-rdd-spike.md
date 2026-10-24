# Phase 0 spike: X# DBFCDX against Retail Plus data

2026-10-24. Code: [spikes/RddSpike](../../spikes/RddSpike). Independent checkers: [tools/dbf](../../tools/dbf). All tests ran on copies of `C:\Retail Plus 52` under `spike-data\` (git-ignored); the installed copy was never written to.

Runtimes under test:

- The official X# runtime on NuGet, `XSharp.Core` + `XSharp.RT` **2.24.0.2** (Apache-2.0; .NET Framework 4.6 builds only; DLL file version 2.23.0.2), run on **.NET 10.0.13** and on **.NET Framework 4.8**.
- **X# 3.0.1.0** (the June 2026 release), built from the public source for .NET 8 and run on .NET 10, both unpatched and with two local patches. See section 7 and [spikes/xsharp3](../../spikes/xsharp3).

## Verdict

**The file formats are compatible. No X# version tested is safe with more than one writer.**

- Reads are correct: X# reads the VO-written tables, memos and indexes exactly as VO wrote them.
- Writes are clean: its edits change only the bytes they should, and every index key it writes matches what VO would write, including VO's `DESCEND(date)` encoding.
- Blocker 1, fixed: on .NET 6+ X# couldn't open a VO index in shared mode. The cause is an 11-line bug in X#'s Windows file stream, present in 3.0.1 too, and a local patch fixes it (section 7).
- Blocker 2, still open: when two processes edit the same table at once, X# leaves old index keys behind within seconds, in 2.24 and 3.0.1 alike. One writer with any number of readers is fine.

Whether `Plus50.exe` accepts files written by X#, and which lock offsets VO uses, still needs a hands-on check in the original program (section 6).

## Results

| Test | .NET 10 | .NET Framework 4.8 |
|---|---|---|
| Load the X# 2.24 runtime (`net46` DLLs) | Loads; needs the code-pages encoding provider for CP1252 | Loads |
| Open VO table + production CDX, **exclusive** | All 6 tags correct | All 6 tags correct |
| Open VO table + production CDX, **shared** | **Fails**: only 1 of 6 tags loads, with the wrong name/expression; X# hits a debug assertion | All 6 tags correct |
| Walk every tag, every record visited once, keys in order | not run (blocked by the shared-open failure) | Pass |
| Scripted edits: rename, re-key, memo rewrite, delete, append | not run | Pass. Seeks find new keys and no longer find old ones |
| Independent CDX check after edits (keys rebuilt from record data) | not run | Pass, all 31 tags in `POSINV`, `CLIENTS`, `SUPPLIER`, `INVOICES` |
| Byte diff against the original files | not run | Only the edited fields, record count, last-update date and memo growth change. Header reserved bytes, field descriptors and EOF marker are untouched |
| Record locks between two handles / two processes | not run | Pass. Second writer is refused while the first holds the lock |
| **Two or three processes editing the same table at once** | not run | **Fails**: the re-keyed tag gains duplicate and stale keys (17 records → 40–43 keys in 12 s). Turning on `StrictRead`, `HardCommit`, or locking before positioning doesn't help |
| Same edits taken in turn by two work areas in one process | not run | Pass. The bug needs real concurrency |

## Details

### 1. Shared open fails on .NET 10

In shared mode, opening each tag takes a shared index lock. The lock first checks whether the index changed on disk, and that check re-reads the tag list page. On .NET 10 the re-read returns the wrong bytes: X# asserts that the page isn't a leaf, ends up with zero keys, and the tag loop stops after the first tag. Exclusive opens skip the check and work. The same DLLs on .NET Framework 4.8 work in both modes, and so does an index that X# rebuilt itself. So this is the 2.24 .NET Framework build misbehaving on .NET (Core), not a VO format problem. .NET 6 changed how `FileStream` tracks its position, which is a likely cause. X# 3.x has a dedicated .NET 8 build of the RDD (`XSharp.RDD.Next.xsproj`) that may not have the problem; that is untested.

Also, the NuGet build keeps `Debug.Assert` calls. On .NET (Core) a failed assert kills the process, so the spike routes asserts to a logging `TraceListener` (see `Rdd.cs`).

### 2. Concurrent writers corrupt the index (both runtimes)

Repro: `RddSpike churn <dbf> <seconds> [lockfirst]` run in two or three processes at once against the same copy, then `python tools/dbf/cdx_check.py <dbf>`. Each writer picks a random record, locks it, changes `PROD` (re-keying the `<LOC>INV` tag) and `ONHAND`, commits and unlocks. Typical result:

```
tag POSINV  UPPER(PROD)  keys=43  51 PROBLEM(S)
    recno 19: key b'CHURN 000028' != expected b'CHURN 000217'
    recno 19 appears twice
```

Old keys are left behind when the other process changed the record in between, so later seeks find records under names they no longer have. The evidence copy is in `spike-data\evidence\concurrent-edits-corrupted-POS`. X# fixed a similar report for appends in 2021 ([issue #585](https://github.com/X-Sharp/XSharpPublic/issues/585)); concurrent key changes still fail in 2.24.

For a multi-till store this is disqualifying on its own. Every sale updates `YTDTOTAL`, which is an index key of the inventory, so two tills selling at the same time re-key the index just as this test does.

### 3. Index key encoding (VO behaviour, confirmed on VO-written files)

- Character keys are compared byte by byte (ordinal). Every VO-written tag is in plain byte order.
- Numeric and date keys use the FoxPro encoding: big-endian IEEE double with the sign bit flipped (or all bits inverted for negatives); dates are the Julian day number.
- `DESCEND(date)` = 5231808 − Julian day; an empty date is stored as 5231808. X# matches this.
- Header last-update year is written as two digits (2026 → 26), by VO and X# alike.

### 4. Lock map observed from X# (ANSI mode, which VO apps use)

| Lock | Byte range | Notes |
|---|---|---|
| Record *n* | 1 byte at `0x80000000 + n`, exclusive | |
| Append / header | 1 byte at `0x80000000`, exclusive | held briefly while appending |
| File (FLock) | `0x80000001`–`0xFFFFFFFF`, exclusive | |
| CDX exclusive (while updating) | `0xFFFEFFFF`–`0xFFFFFFFF` (65,537 bytes) | Comix scheme |
| CDX shared (while reading) | 1 random byte in `0xFFFF0000`–`0xFFFFFFFF` | Comix scheme |

The X# source says this scheme was researched against the VO/Vulcan drivers. It still has to be confirmed against `Plus50.exe` itself (section 6).

## 5. What this means for the plan

The data-compatibility approach holds: the formats are now pinned down and verified both ways. What fails is X#'s handling of several writers. X# 3.0.1 has now been tested (section 7) and fails the same way, so the choice comes down to whether the new app has to **write alongside `Plus50.exe`** during rollout (decision 2 in the plan):

1. **Side-by-side writing required → write our own DBF/FPT/CDX engine in C#.** Limit it to what Retail Plus uses: compact CDX, simple key expressions (`UPPER()`, `DESCEND()`, `+`, plain fields), Comix-compatible locking. Design it so nothing cached survives across a lock: every update re-reads the header and pages it touches after taking the lock. The reader and the independent checker already exist and are calibrated on VO files, and the formats are pinned down. Rough effort 3–5 weeks, including the concurrency torture test from this spike as the acceptance test. Runs on .NET 10. Any engine has to cope with VO writing at the same time, and this is the only option where we control that.
2. **No side-by-side writing → one-time migration to SQLite or SQL Server.** Import the DBF data once per store at cutover, and keep DBF export only for the store-to-store files. Cheapest and cleanest; loses the instant fallback to the old program.
3. **In between: keep patched X# 3.0.1 behind a single writer.** One "data service" process per store does all writes; tills and back office talk to it. The spike shows one writer plus readers stays consistent. `Plus50.exe` would then have to stop writing (read-only, or retired) at cutover, so this gives little over option 2 while keeping the X# dependency.

Whatever is chosen: **report both X# defects upstream**, with the spike as the repro and patch 0001 as a fix. That's public, so it needs your OK.

Recommendation: option 1 if running the two programs side by side matters to you, otherwise option 2. Until this is settled, Phase 1 shouldn't start on the data layer. The app shell, Core business rules and parity tooling don't depend on it.

## 6. Still to check with `Plus50.exe` (needs you at the keyboard)

These need the original program's user interface. Each launch counts against the demo session limit of the copy it runs from (`V2.INI` in that folder).

1. **Does VO accept files written by X#?** Start `spike-data\plus50-check\Plus50.exe`. If it opens the original inventory, use *File / Inventory / Select* and pick `spike-data\plus50-check\POS\POSINV.DBF`. Then check:
   - Find item `SPK001` by stock code, and `GARMIN VISTA GPS (SPIKE)` by name.
   - Stock code `0310` is gone and `0310X` (Kelty Zen Tent) exists.
   - Item 10 (deleted by X#) doesn't show.
   - The Clients list shows *Ann Spike*, the Invoices list shows invoices 1001 and 1002, and suppliers include *SPIKE SUPPLY CO*.
   - Run *Tools / Backup / Rebuild*. Afterwards I diff and check the index VO rebuilds against the one X# wrote.
2. **Which locks does VO take?** While I run `RddSpike watch` on that folder, edit and save an item, add an item, and delete one. The watcher records every lock range VO takes, to compare with the table in section 4. The watcher briefly takes test locks, so VO may occasionally report a record as in use; on this copy that's harmless.

Selecting the inventory changes `C:\POS_SETTINGS\Plus50.INI`, which the installed copy also uses. A backup taken before the test is at `spike-data\Plus50.INI.backup`; copy it back afterwards.

## 7. X# 3.0.1, built from source

**How it was obtained.** The X# 3 installers on xsharp.eu need a registered account, and the free public edition leaves out the VO dialect. Neither matters here, because the spike only calls the runtime from C#. The four runtime DLLs were built from the Apache-2.0 source tag `v3.0.1.0` (commit `4c6ac4bb`) with a private .NET 9 SDK: no Visual Studio, no account, nothing installed system-wide. Recipe: [spikes/xsharp3/build-runtime.sh](../../spikes/xsharp3/build-runtime.sh). The compiler reports itself as `X# Compiler version 3.0.1.0 (Release)` and the DLLs carry file version 3.0.1.0.

**What changed since 2.24 in the DBF/CDX code:** mostly type widening (files over 2 GB) and named constants. Nothing touched cross-process locking or caching.

| Test (same suite for every row) | 2.24 on .NET Fx 4.8 | 3.0.1 on .NET 10 | 3.0.1 + patch 0001 | 3.0.1 + patches 0001+0002 |
|---|---|---|---|---|
| Shared open, all 6 tags | Pass | **Fail**: 1 tag, wrong name; next step hangs | Pass | Pass |
| Walk, edits, seeks, 31-tag independent check, byte diff, lock self-test | Pass | not reached | Pass | Pass |
| 2 writers × 2 trials | 10, 12 problems | not reached | 12, 6 problems | 8, 4 problems |
| 3 writers × 2 trials | 22, 26 problems | not reached | 30, 30 problems | 16, 26 problems |

Further runs on 3.0.1 + patches 0001+0002 (two trials each unless noted):

- 1 writer + 1 reader doing 220,000+ walks and seeks: index correct.
- 2 writers, edits only, no appends: 47–52 keys for 17 records.
- 2 writers with per-process unique key values: 39–41 keys for 17 records. 34–41 on 2.24.
- Debug build of the runtime, which validates the whole tree after every update in shared mode (one trial): no assertion, yet 52 keys for 17 records. The tree stays well-formed; old keys just aren't deleted.
- On 2.24 only: a single process gives 17 records the same key, then renames them all. The index stays correct, so this isn't a duplicate-key bug.

**Patch 0001, cause of blocker 1 (verified fix).** In shared mode on Windows, X# opens files through `XsWin32FileStream`, which overrides `Seek`, `Read` and `Write` to call `SetFilePointerEx`/`ReadFile`/`WriteFile` directly. X#'s page reader moves through a file by setting `Position`. On .NET Framework that setter calls the virtual `Seek`, so the OS file pointer moves. Since .NET 6 it only updates the stream's cached position, so `ReadFile` reads from wherever the OS pointer happened to be. Adding a `Position` override that goes through `Seek` fixes shared opens on .NET 10.

**Patch 0002, a real but partial fix for blocker 2.** Each tag copies its root page number at open, and only updates it when this process moves the root. The tag header is re-read when another process changed the index, but the tag kept navigating from its stale copy. Patch 0002 refreshes it after every index lock. Corruption drops somewhat but doesn't go away, so there is at least one more cross-process stale-state bug. Its cause was not found within this spike.

## How to rerun

```
dotnet build spikes/RddSpike
spikes\RddSpike\bin\Debug\net48\RddSpike.exe info   <copy>\POS\POSINV.DBF
spikes\RddSpike\bin\Debug\net48\RddSpike.exe edit   <copy>\POS\POSINV.DBF
python tools\dbf\cdx_check.py <copy>\POS\POSINV.DBF
python tools\dbf\dbf_diff.py  spike-data\baseline\POS\POSINV.DBF <copy>\POS\POSINV.DBF
```

The .NET 10 build is in `bin\Debug\net10.0-windows`. It needs `DOTNET_ROOT` pointing at the SDK folder when the SDK is installed per user.

Against a locally built X# runtime (section 7):

```
spikes/xsharp3/build-runtime.sh --patched
dotnet build spikes/RddSpike -p:XSharpRuntimeDir=%USERPROFILE%\xsb\runtime-3.0.1-patched
spikes\RddSpike\bin\runtime-3.0.1-patched\Debug\net10.0-windows\RddSpike.exe churn <copy>\POS\POSINV.DBF 12 lockfirst
```

Run two `churn` commands at once against the same copy to reproduce blocker 2. `RDDSPIKE_NOAPPEND=1` gives edits only; `RDDSPIKE_UNIQUE=1` gives per-process unique key values. Full suite outputs are in `spike-data\evidence\xsharp3\`.
