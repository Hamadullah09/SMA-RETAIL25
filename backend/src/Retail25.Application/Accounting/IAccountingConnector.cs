namespace Retail25.Application.Accounting;

/// <summary>What a sync attempt did. Never throws for a business-level failure — a bookkeeping
/// outage must not be able to stop the shop selling (doc 09 §1, "failure isolation").</summary>
public sealed record SyncResult(bool Success, int RecordCount, string? Error = null, string? Output = null)
{
    public static SyncResult Ok(int recordCount, string? output = null) => new(true, recordCount, null, output);

    public static SyncResult Failed(string error) => new(false, 0, error);
}

/// <summary>Which slice of data to push. A null range means everything not yet synced.</summary>
public sealed record SyncScope(Guid LocationId, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// The port the accounting system sits behind (doc 09 §1), replacing the legacy QB-XML link that
/// required the company file open on the same machine.
/// <para>
/// Adapters are chosen per deployment. The CSV adapter is always available and always the fallback:
/// a store whose provider integration is down can still hand its bookkeeper a file.
/// </para>
/// </summary>
public interface IAccountingConnector
{
    /// <summary>"csv", "quickbooks-online", "xero", …</summary>
    string Provider { get; }

    Task<SyncResult> PushCustomersAsync(SyncScope scope, CancellationToken ct);

    Task<SyncResult> PushItemsAsync(SyncScope scope, CancellationToken ct);

    Task<SyncResult> PushVendorsAsync(SyncScope scope, CancellationToken ct);

    /// <summary>Open AR invoices only. Nothing is ever deleted locally — an invoice pushed onward is
    /// still payable at the till, and its payments sync back (doc 09, contra the legacy behaviour).</summary>
    Task<SyncResult> PushInvoicesAsync(SyncScope scope, CancellationToken ct);

    /// <summary>
    /// A day's takings as one journal per location, built from closed drawer sessions (guide p.111).
    /// The source rows are marked as posted rather than cleared, so the day can always be re-read.
    /// </summary>
    Task<SyncResult> PostPosRevenueAsync(Guid locationId, DateOnly businessDate, CancellationToken ct);

    /// <summary>A received purchase order as an A/P bill, due 30 days out by default (guide p.112–113).</summary>
    Task<SyncResult> PostBillAsync(Guid purchaseOrderId, DateOnly dueOn, CancellationToken ct);

    Task<SyncResult> PullCustomersAsync(Guid locationId, CancellationToken ct);

    Task<SyncResult> PullItemsAsync(Guid locationId, CancellationToken ct);

    Task<SyncResult> PullVendorsAsync(Guid locationId, CancellationToken ct);
}
