using System.Diagnostics;
using XSharp;
using XSharp.RDD.Enums;
using RddSpike;

// Phase 0 spike: can the X# DBFCDX RDD read and write Retail Plus 5.2 tables, and does it lock like VO 2.8?
// Usage:
//   RddSpike info <dbf>                      structure, settings and a walk of every tag
//   RddSpike edit <dbf>                      scripted edits (rename, re-key, memo rewrite, delete, append)
//   RddSpike verify-edit <dbf>               seeks that must succeed after 'edit'
//   RddSpike lock <dbf> <recno|FILE> <secs>  hold a record or file lock for a while
//   RddSpike probe <file> [hex-from hex-to]  list byte ranges locked by other handles/processes
//   RddSpike selftest-locks <dbf>            X#-to-X# lock collisions plus probe validation

if (args.Length < 2) { Console.WriteLine("see Program.cs header for usage"); return 2; }
string cmd = args[0], path = Path.GetFullPath(args[1]);

if (cmd == "probe")
{
    ulong from = args.Length > 2 ? Convert.ToUInt64(args[2], 16) : 0;
    ulong to = args.Length > 3 ? Convert.ToUInt64(args[3], 16) : 0x2_0000_0000;
    using var probe = new LockProbe(path);
    var ranges = probe.Scan(from, to);
    Console.WriteLine($"{Path.GetFileName(path)}: {(ranges.Count == 0 ? "no locks held by others" : $"{ranges.Count} locked range(s)")}");
    foreach (var r in ranges) Console.WriteLine("  " + r);
    return 0;
}

if (cmd == "watch")
{
    // Polls for locks held by other processes for N seconds and tallies every distinct range seen.
    // Index (CDX) locks only last a few milliseconds while pages are written, so they are caught by sampling.
    int seconds = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 30;
    var files = args[1].Split(';').Select(Path.GetFullPath).ToArray();
    var probes = files.Select(f => new LockProbe(f)).ToArray();
    var seen = new Dictionary<string, int>();
    var stop = Stopwatch.StartNew();
    long samples = 0, transient = 0;
    Console.WriteLine($"watching {string.Join(", ", files.Select(Path.GetFileName))} for {seconds}s ...");
    while (stop.Elapsed.TotalSeconds < seconds)
    {
        for (int i = 0; i < probes.Length; i++)
        {
            samples++;
            foreach (var r in probes[i].Scan(0, 0x2_0000_0000))
            {
                if (!probes[i].StillHeld(r)) { transient++; continue; }
                string label = LockProbe.Classify(files[i], r);
                // Record locks are tallied per kind, not per record number, to keep the summary short.
                string kind = label.StartsWith("DBF record lock", StringComparison.Ordinal) ? "DBF record lock (0x80000000 + recno)" : label;
                string key = $"{Path.GetFileName(files[i]),-12} {kind,-42} {(kind == label ? r.ToString() : "")}";
                if (!seen.ContainsKey(key)) Console.WriteLine($"  [{stop.Elapsed.TotalSeconds,6:F2}s] {Path.GetFileName(files[i]),-12} {label,-42} {r}");
                seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        }
    }
    Console.WriteLine($"{samples:N0} samples, {transient:N0} ranges discarded as changing mid-scan; confirmed lock kinds:");
    foreach (var kv in seen.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Value,6}x  {kv.Key}");
    foreach (var p in probes) p.Dispose();
    return 0;
}

Rdd.Init();
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, X# {System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(CoreDb).Assembly.Location).FileVersion}");

switch (cmd)
{
    case "info":
        Rdd.Open(path);
        Info();
        WalkTags();
        break;
    case "tags":
    case "tags-excl":
        Rdd.Open(path, shared: cmd == "tags");
        Console.WriteLine($"  bags={Rdd.OrderInfo(DbOrder_Info.DBOI_BAGCOUNT)} orders={Rdd.OrderInfo(DbOrder_Info.DBOI_ORDERCOUNT)} current='{Rdd.OrderInfo(DbOrder_Info.DBOI_NAME)}'");
        for (int i = 1; i <= 7; i++)
            Console.WriteLine($"  order #{i}: name='{Rdd.OrderInfo(DbOrder_Info.DBOI_NAME, i)}' expr='{Rdd.OrderInfo(DbOrder_Info.DBOI_EXPRESSION, i)}' bag='{Rdd.OrderInfo(DbOrder_Info.DBOI_BAGNAME, i)}'");
        foreach (var name in new[] { "DEPT", "PLU", "POSINV", "SUPPCODE", "SUPPLIER", "YTDTOTAL" })
        {
            bool ok = CoreDb.OrdSetFocus(null!, name);
            Console.WriteLine($"  focus {name,-9} -> {ok}, now '{Rdd.OrderInfo(DbOrder_Info.DBOI_NAME)}' expr='{Rdd.OrderInfo(DbOrder_Info.DBOI_EXPRESSION)}' keys={Rdd.OrderInfo(DbOrder_Info.DBOI_KEYCOUNT)}");
        }
        break;
    case "append":
        Rdd.Open(path);
        Append(args.Skip(2));
        break;
    case "churn":
        // Repeated edits and appends for N seconds, to give 'watch' something to observe.
        Rdd.Open(path);
        Churn(int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), lockFirst: args.Length > 3 && args[3] == "lockfirst");
        break;
    case "race-repro":
        RaceRepro(args.Length > 2 ? args[2] : "goto-then-lock");
        break;
    case "readloop":
        // Reader only: repeatedly seeks every record's PROD key through the POSINV tag for N seconds.
        Rdd.Open(path);
        ReadLoop(int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        break;
    case "dup-repro":
        // Single process, no concurrency: give many records the same key value, then rename them one by one.
        Rdd.Open(path);
        DupRepro();
        break;
    case "reindex":
        Rdd.Open(path, shared: false);
        Console.WriteLine($"  OrdListRebuild (exclusive): {CoreDb.OrdListRebuild()}");
        break;
    case "edit":
        Rdd.Open(path);
        Edit();
        break;
    case "verify-edit":
        Rdd.Open(path);
        VerifyEdit();
        break;
    case "lock":
        Rdd.Open(path);
        HoldLock(args[2], int.Parse(args[3]));
        break;
    case "selftest-locks":
        SelfTestLocks();
        break;
    default:
        Console.WriteLine($"unknown command {cmd}");
        return 2;
}
CoreDb.CloseAll();
Console.WriteLine($"X# debug assertions hit: {Rdd.AssertCount}");
return 0;

void Info()
{
    Console.WriteLine($"Table {Path.GetFileName(path)}: {Rdd.LastRec()} records, {CoreDb.FCount()} fields");
    foreach (var (label, what) in new[] { ("ansi", DbInfo.DBI_ISANSI), ("codepage", DbInfo.DBI_CODEPAGE), ("lock scheme", DbInfo.DBI_LOCKSCHEME),
                 ("lock offset", DbInfo.DBI_LOCKOFFSET), ("memo block", DbInfo.DBI_MEMOBLOCKSIZE), ("memo ext", DbInfo.DBI_MEMOEXT), ("header", DbInfo.DBI_GETHEADERSIZE),
                 ("record size", DbInfo.DBI_GETRECSIZE), ("last update", DbInfo.DBI_LASTUPDATE) })
        Console.WriteLine($"  {label,-12} {Rdd.Info(what)}");
}

void WalkTags()
{
    int lastRec = Rdd.LastRec();
    foreach (var (no, name, expr) in Rdd.Tags().ToList())
    {
        CoreDb.OrdSetFocus(null!, name);
        var keyCount = Rdd.OrderInfo(DbOrder_Info.DBOI_KEYCOUNT);
        var seen = new HashSet<uint>();
        var keys = new List<string>();
        CoreDb.GoTop();
        while (!CoreDb.Eof())
        {
            seen.Add(CoreDb.Recno());
            keys.Add(Convert.ToString(Rdd.OrderInfo(DbOrder_Info.DBOI_KEYVAL)) ?? "");
            CoreDb.Skip(1);
        }
        bool sorted = keys.Zip(keys.Skip(1), (a, b) => string.CompareOrdinal(a, b) <= 0).All(ok => ok);
        string verdict = seen.Count == lastRec && Convert.ToInt32(keyCount) == lastRec ? "OK" : "MISMATCH";
        Console.WriteLine($"  tag {no} {name,-10} {expr,-28} keys={keyCount} visited={seen.Count}/{lastRec} ordinal-sorted={sorted} {verdict}");
        Console.WriteLine($"      first keys: {string.Join(" | ", keys.Take(4).Select(k => k.TrimEnd()))}");
    }
    CoreDb.OrdSetFocus(null!, 0);
}

void Edit()
{
    CoreDb.OrdSetFocus(null!, 0);
    Step("rename record 3 (re-keys the PROD tag)", 3, () => Rdd.Put("PROD", "GARMIN VISTA GPS (SPIKE)"));
    Step("change stock code of record 5 (re-keys PLU, DEPT, SUPPLIER)", 5, () => Rdd.Put("PLU", "0310X"));
    Step("rewrite NOTES memo of record 2 with a longer text", 2, () => Rdd.Put("NOTES", string.Concat(Enumerable.Repeat("Spike memo line written by X#.\r\n", 40))));
    Step("price and on-hand change on record 4", 4, () => { Rdd.Put("UNITPRICE1", 36.5m); Rdd.Put("ONHAND", 7m); });
    Step("delete record 10", 10, () => CoreDb.Delete());

    if (!CoreDb.Append(true)) throw new InvalidOperationException("Append failed");
    uint newRec = CoreDb.Recno();
    Rdd.Put("PROD", "SPIKE TEST ITEM");
    Rdd.Put("PLU", "SPK001");
    Rdd.Put("DEPT", "ACCESSORIES");
    Rdd.Put("ITEMTYPE", "STANDARD");
    Rdd.Put("UNITPRICE1", 12.34m);
    Rdd.Put("ONHAND", 5m);
    Rdd.Put("TX1", true);
    Rdd.Put("LASTUPD", new DateTime(2026, 10, 24));
    Rdd.Put("SUPPLIER", "ROBINSON & HEATH");
    Rdd.Put("SUPLIST", "|ROBINSON & HEATH,SPK-1,6.000,1.00|");
    Rdd.Put("NOTES", "Appended by the X# spike.");
    CoreDb.Commit();
    CoreDb.Unlock(null!);
    Console.WriteLine($"  appended record {newRec}");
    Console.WriteLine($"  now {Rdd.LastRec()} records");
}

void Append(IEnumerable<string> assignments)
{
    // FIELD=VALUE pairs; the value is converted according to the DBF field type (D = yyyyMMdd, empty = blank date).
    if (!CoreDb.Append(true)) throw new InvalidOperationException("Append failed");
    foreach (var a in assignments)
    {
        int eq = a.IndexOf('=');
        string field = a.Substring(0, eq), text = a.Substring(eq + 1);
        object? type = null;
        CoreDb.FieldInfo((uint)DbFieldInfo.DBS_TYPE, Rdd.FieldPos(field), ref type);
        object value = (type as string) switch
        {
            "D" => text.Length == 0 ? (object)DateTime.MinValue : DateTime.ParseExact(text, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
            "N" => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            "L" => text is "T" or "true" or "TRUE",
            _ => text,
        };
        if (text.Length > 0 || (type as string) != "D") Rdd.Put(field, value);
    }
    CoreDb.Commit();
    Console.WriteLine($"  appended record {CoreDb.Recno()} to {Path.GetFileName(path)}");
    CoreDb.Unlock(null!);
}

void Churn(int seconds, bool lockFirst)
{
    // lockFirst = take the record lock before positioning, so the record (and its current index keys) are read
    // after the lock is held. Otherwise: position, then lock, the usual xBase pattern.
    var sw = Stopwatch.StartNew();
    int edits = 0, appends = 0;
    var rnd = new Random(Environment.TickCount);
    while (sw.Elapsed.TotalSeconds < seconds)
    {
        uint rec = (uint)rnd.Next(1, Rdd.LastRec() + 1);
        if (!lockFirst) Rdd.Goto(rec);
        if (CoreDb.RLock(rec))
        {
            if (lockFirst) Rdd.Goto(rec);
            Rdd.Put("PROD", Environment.GetEnvironmentVariable("RDDSPIKE_UNIQUE") == "1" ? $"CHURN P{Process.GetCurrentProcess().Id} {edits:D6}" : $"CHURN {edits:D6}");
            Rdd.Put("ONHAND", (decimal)rnd.Next(0, 500));
            CoreDb.Commit();
            CoreDb.Unlock(rec);
            edits++;
        }
        if (edits % 10 == 0 && Environment.GetEnvironmentVariable("RDDSPIKE_NOAPPEND") != "1" && CoreDb.Append(true))
        {
            Rdd.Put("PROD", $"CHURN NEW {appends:D5}");
            Rdd.Put("PLU", $"CH{appends:D5}");
            CoreDb.Commit();
            CoreDb.Unlock(null!);
            appends++;
        }
        Thread.Sleep(20);
    }
    Console.WriteLine($"  churn: {edits} edits, {appends} appends in {seconds}s, {Rdd.LastRec()} records");
}

void DupRepro()
{
    int n = Rdd.LastRec();
    for (uint rec = 1; rec <= n; rec++) Write1(rec, "DUPLICATE NAME");
    Console.WriteLine($"  step 1: all {n} records renamed to the same PROD value");
    for (uint rec = 1; rec <= n; rec++) Write1(rec, $"UNIQUE {rec:D3}");
    Console.WriteLine($"  step 2: each record renamed to a unique PROD value");

    static void Write1(uint rec, string prod)
    {
        CoreDb.RLock(rec);
        Rdd.Goto(rec);
        Rdd.Put("PROD", prod);
        CoreDb.Commit();
        CoreDb.Unlock(rec);
    }
}

void ReadLoop(int seconds)
{
    var sw = Stopwatch.StartNew();
    int seeks = 0, walks = 0;
    string tag = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
    while (sw.Elapsed.TotalSeconds < seconds)
    {
        CoreDb.OrdSetFocus(null!, tag);
        CoreDb.GoTop();
        while (!CoreDb.Eof()) { CoreDb.Skip(1); }
        walks++;
        CoreDb.OrdSetFocus(null!, "PLU");
        CoreDb.Seek("0443", false, false);
        seeks++;
    }
    Console.WriteLine($"  readloop: {walks} tag walks, {seeks} seeks in {seconds}s");
}

void RaceRepro(string pattern)
{
    // Deterministic version of the multi-writer test: two work areas (two file handles, like two users) take turns
    // re-keying the same record. Every write holds the record lock, so a correct RDD must keep the index exact.
    Rdd.Open(path, alias: "A");
    uint a = CoreDb.GetSelect();
    Rdd.Open(path, alias: "B");
    uint b = CoreDb.GetSelect();
    void Write(uint area, uint rec, string prod)
    {
        CoreDb.Select(area, out _);
        if (pattern == "lock-then-goto") { CoreDb.RLock(rec); Rdd.Goto(rec); }
        else { Rdd.Goto(rec); CoreDb.RLock(rec); }
        Rdd.Put("PROD", prod);
        CoreDb.Commit();
        CoreDb.Unlock(rec);
        Console.WriteLine($"  area {(area == a ? "A" : "B")}: rec {rec} PROD := {prod}");
    }
    CoreDb.Select(a, out _);
    Rdd.Goto(5u);   // A reads record 5 and leaves the cursor there
    Write(b, 5, "STEP ONE BY B");
    Write(a, 5, "STEP TWO BY A");
    Write(b, 5, "STEP THREE BY B");
    Write(a, 6, "OTHER RECORD BY A");
    Write(a, 5, "STEP FOUR BY A");
}

void Step(string what, uint recno, Action change)
{
    Rdd.Goto(recno);
    if (!CoreDb.RLock(recno)) throw new InvalidOperationException($"RLock {recno} failed");
    change();
    CoreDb.Commit();
    CoreDb.Unlock(recno);
    Console.WriteLine($"  {what}: done");
}

void VerifyEdit()
{
    Check("PLU", "SPK001", "SPIKE TEST ITEM");
    Check("PLU", "0310X", "KELTY ZEN TENT");
    CheckMissing("PLU", "0310 ");
    Check("POSINV", "GARMIN VISTA GPS (SPIKE)", "GARMIN VISTA GPS (SPIKE)");
    CheckMissing("POSINV", "GARMIN VISTA GPS              ");
    Rdd.Goto(10);
    Console.WriteLine($"  record 10 deleted flag: {CoreDb.Deleted()}");
    Rdd.Goto(2);
    Console.WriteLine($"  record 2 NOTES length: {Rdd.Str("NOTES").Length}");
    WalkTags();
}

void Check(string tag, string key, string expectProd)
{
    CoreDb.OrdSetFocus(null!, tag);
    bool found = CoreDb.Seek(key, false, false) && (bool)Rdd.Info(DbInfo.DBI_FOUND)!;
    string prod = found ? Rdd.Str("PROD") : "";
    Console.WriteLine($"  seek {tag} '{key}': {(found ? $"found rec {CoreDb.Recno()} '{prod}'" : "NOT FOUND")} {(found && prod == expectProd ? "OK" : "FAIL")}");
}

void CheckMissing(string tag, string key)
{
    CoreDb.OrdSetFocus(null!, tag);
    bool found = CoreDb.Seek(key, false, false) && (bool)Rdd.Info(DbInfo.DBI_FOUND)!;
    Console.WriteLine($"  seek {tag} '{key.TrimEnd()}' (old key, exact): {(found ? $"found rec {CoreDb.Recno()} FAIL" : "not found OK")}");
}

void HoldLock(string what, int seconds)
{
    bool ok = what.Equals("FILE", StringComparison.OrdinalIgnoreCase) ? CoreDb.Flock() : CoreDb.RLock(uint.Parse(what));
    Console.WriteLine($"  {(what == "FILE" ? "FLock" : $"RLock({what})")} -> {ok}; holding {seconds}s (pid {Process.GetCurrentProcess().Id})");
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    CoreDb.Unlock(null!);
}

void SelfTestLocks()
{
    // Two work areas on the same table use two file handles, which Windows treats like two users.
    Rdd.Open(path, alias: "A");
    uint areaA = CoreDb.GetSelect();
    Rdd.Open(path, alias: "B");
    uint areaB = CoreDb.GetSelect();
    using var probe = new LockProbe(path);

    CoreDb.Select(areaA, out _);
    Report("A: RLock(3)", CoreDb.RLock(3u));
    Show(probe);
    CoreDb.Select(areaB, out _);
    Report("B: RLock(3) while A holds it (expect False)", CoreDb.RLock(3u));
    Report("B: RLock(4) (expect True)", CoreDb.RLock(4u));
    CoreDb.Unlock(null!);
    Report("B: FLock while A holds record 3 (expect False)", CoreDb.Flock());
    CoreDb.Select(areaA, out _);
    CoreDb.Unlock(null!);
    Report("A: FLock", CoreDb.Flock());
    Show(probe);
    CoreDb.Select(areaB, out _);
    Report("B: RLock(5) while A holds the file lock (expect False)", CoreDb.RLock(5u));
    CoreDb.Select(areaA, out _);
    CoreDb.Unlock(null!);
    Show(probe);

    var sw = Stopwatch.StartNew();
    using (var cdxProbe = new LockProbe(Path.ChangeExtension(path, ".CDX")))
        Console.WriteLine($"  CDX locks while idle: {cdxProbe.Scan(0, 0x2_0000_0000).Count} (probe took {sw.ElapsedMilliseconds} ms)");
}

static void Report(string what, bool result) => Console.WriteLine($"  {what}: {result}");

static void Show(LockProbe probe)
{
    var ranges = probe.Scan(0, 0x2_0000_0000);
    Console.WriteLine($"    probe sees: {(ranges.Count == 0 ? "nothing" : string.Join("; ", ranges))}");
}
