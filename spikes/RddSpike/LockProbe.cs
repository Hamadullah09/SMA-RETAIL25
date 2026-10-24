using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RddSpike;

/// <summary>
/// Finds byte ranges of a file that are locked through other file handles (another process, or another
/// handle in this process), using LockFileEx with LOCKFILE_FAIL_IMMEDIATELY. Each test lock is released at once.
/// </summary>
internal sealed class LockProbe : IDisposable
{
    private const uint LOCKFILE_FAIL_IMMEDIATELY = 0x1;
    private const uint LOCKFILE_EXCLUSIVE_LOCK = 0x2;
    private readonly FileStream _fs;

    public LockProbe(string path) =>
        _fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

    public sealed class LockedRange(ulong start, ulong length, bool exclusive)
    {
        public ulong Start { get; } = start;
        public ulong Length { get; } = length;
        public bool Exclusive { get; } = exclusive;

        public override string ToString() =>
            $"0x{Start:X9}..0x{Start + Length - 1:X9}  ({Length:N0} byte{(Length == 1 ? "" : "s")}, {(Exclusive ? "exclusive" : "shared")})";
    }

    /// <summary>True when [offset, offset+length) can be locked exclusively, i.e. nobody else holds any part of it.</summary>
    public bool IsFree(ulong offset, ulong length) => TryLock(offset, length, exclusive: true);

    private bool TryLock(ulong offset, ulong length, bool exclusive)
    {
        var ov = new NativeOverlapped { OffsetLow = (int)(uint)offset, OffsetHigh = (int)(uint)(offset >> 32) };
        uint flags = LOCKFILE_FAIL_IMMEDIATELY | (exclusive ? LOCKFILE_EXCLUSIVE_LOCK : 0);
        if (!LockFileEx(_fs.SafeFileHandle, flags, 0, (uint)length, (uint)(length >> 32), ref ov))
        {
            int err = Marshal.GetLastWin32Error();
            if (err is 33 /* ERROR_LOCK_VIOLATION */ or 158 /* ERROR_NOT_LOCKED */) return false;
            throw new Win32Exception(err);
        }
        ov = new NativeOverlapped { OffsetLow = (int)(uint)offset, OffsetHigh = (int)(uint)(offset >> 32) };
        UnlockFileEx(_fs.SafeFileHandle, 0, (uint)length, (uint)(length >> 32), ref ov);
        return true;
    }

    /// <summary>Lists locked runs inside [from, to). Assumes each lock is one contiguous run, which is how DBF RDDs lock.</summary>
    public List<LockedRange> Scan(ulong from, ulong to)
    {
        var found = new List<LockedRange>();
        ulong pos = from;
        while (pos < to && !IsFree(pos, to - pos))
        {
            ulong first = FirstLocked(pos, to);
            ulong end = EndOfRun(first, to);
            bool exclusive = !TryLock(first, 1, exclusive: false);
            found.Add(new LockedRange(first, end - first, exclusive));
            pos = end;
        }
        return found;
    }

    /// <summary>
    /// Re-tests a range found by <see cref="Scan"/>: both ends must still be locked and the bytes just outside free.
    /// Locks that come and go during a scan otherwise produce bogus ranges.
    /// </summary>
    public bool StillHeld(LockedRange r) =>
        !IsFree(r.Start, 1) && !IsFree(r.Start + r.Length - 1, 1) &&
        (r.Start == 0 || IsFree(r.Start - 1, 1)) && IsFree(r.Start + r.Length, 1);

    /// <summary>Names a lock range using the schemes known from the X# source (VO ANSI DBF locks, Comix CDX locks).</summary>
    public static string Classify(string file, LockedRange r)
    {
        bool cdx = file.EndsWith(".CDX", StringComparison.OrdinalIgnoreCase);
        ulong s = r.Start, e = r.Start + r.Length - 1;
        if (!cdx)
        {
            if (s == 0x8000_0000 && r.Length == 1) return "DBF header/append lock";
            if (s == 0x8000_0001 && e == 0xFFFF_FFFF) return "DBF file lock (FLock)";
            if (s > 0x8000_0000 && r.Length == 1 && s < 0xFFFF_FFFF) return $"DBF record lock, recno {s - 0x8000_0000}";
            if (s >= 1_000_000_000 && s < 2_000_000_000 && r.Length == 1) return $"Clipper-style record lock, recno {s - 1_000_000_000}";
            if (s >= 0x4000_0000 && s < 0x8000_0000) return "FoxPro-style lock region";
            return "unrecognised DBF lock";
        }
        if (s == 0xFFFE_FFFF && e == 0xFFFF_FFFF) return "CDX exclusive lock (Comix, whole region)";
        if (s == 0xFFFE_FFFF && r.Length == 1) return "CDX exclusive gate byte (Comix)";
        if (s >= 0xFFFF_0000 && e <= 0xFFFF_FFFF && r.Length == 1) return "CDX shared lock (Comix, random byte)";
        if (s == 0x7FFF_FFFE && r.Length == 1) return "CDX FoxPro-style lock";
        return "unrecognised CDX lock";
    }

    private ulong FirstLocked(ulong lo, ulong hi)
    {
        while (hi - lo > 1)
        {
            ulong mid = lo + (hi - lo) / 2;
            if (IsFree(lo, mid - lo)) lo = mid; else hi = mid;
        }
        return lo;
    }

    private ulong EndOfRun(ulong first, ulong limit)
    {
        ulong step = 1;
        ulong lastLocked = first;
        while (first + step < limit && !IsFree(first + step, 1)) { lastLocked = first + step; step *= 2; }
        ulong lo = lastLocked, hi = Math.Min(first + step, limit);   // lo locked, hi free (or limit)
        while (hi - lo > 1)
        {
            ulong mid = lo + (hi - lo) / 2;
            if (!IsFree(mid, 1)) lo = mid; else hi = mid;
        }
        return lo + 1;
    }

    public void Dispose() => _fs.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool LockFileEx(SafeFileHandle hFile, uint dwFlags, uint reserved, uint nBytesLow, uint nBytesHigh, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnlockFileEx(SafeFileHandle hFile, uint reserved, uint nBytesLow, uint nBytesHigh, ref NativeOverlapped overlapped);
}
