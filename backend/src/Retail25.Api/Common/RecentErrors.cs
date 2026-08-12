using System.Collections.Concurrent;

namespace Retail25.Api.Common;

/// <summary>One unhandled failure, kept long enough for somebody to ask what happened.</summary>
public sealed record RecordedError(
    DateTimeOffset OccurredAt,
    string TraceId,
    string Method,
    string Path,
    string ExceptionType,
    string Message,
    string Detail);

/// <summary>
/// The last few unhandled exceptions, in memory, readable by an administrator.
/// <para>
/// A hosted shop cannot read a log file. On this deployment the API runs under a pool whose folder
/// the shopkeeper reaches through a control panel, if at all, and the answer to "the till said
/// something went wrong" was otherwise nothing at all — the response carries no detail on purpose,
/// and the console it was written to belongs to IIS. A sale that cannot be completed and cannot be
/// diagnosed is the same as a sale that cannot be completed.
/// </para>
/// <para>
/// Bounded and in memory by design. It holds <see cref="Capacity"/> entries and is emptied by a pool
/// recycle, which makes it a window onto what is going wrong now rather than an audit trail — the
/// audit trail is the database, and it survives. Nothing here is load-bearing, so a failure to
/// record must never disturb the request that failed.
/// </para>
/// </summary>
public sealed class RecentErrors
{
    /// <summary>
    /// Enough to cover a till failing repeatedly for a few minutes without the first occurrence —
    /// usually the informative one — falling out before anyone looks.
    /// </summary>
    public const int Capacity = 100;

    /// <summary>
    /// A stack trace is long and the point is to read it, not to hold a heap. Truncated rather than
    /// dropped: the top of a stack is where the answer is.
    /// </summary>
    private const int MaxDetail = 8_000;

    private readonly ConcurrentQueue<RecordedError> _entries = new();

    public void Record(DateTimeOffset now, string traceId, string method, string path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detail = exception.ToString();

        _entries.Enqueue(new RecordedError(
            now,
            traceId,
            method,
            path,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            detail.Length > MaxDetail ? detail[..MaxDetail] : detail));

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    /// <summary>Most recent first, optionally narrowed to the trace id a client was handed.</summary>
    public IReadOnlyList<RecordedError> Take(int count, string? traceId = null)
    {
        IEnumerable<RecordedError> entries = _entries.Reverse();

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            entries = entries.Where(e => string.Equals(e.TraceId, traceId, StringComparison.Ordinal));
        }

        return entries.Take(Math.Clamp(count, 1, Capacity)).ToList();
    }
}
