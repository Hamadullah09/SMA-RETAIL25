namespace Retail25.Domain.Accounting;

public enum SyncDirection
{
    Push = 0,
    Pull = 1,
}

public enum SyncStatus
{
    Success = 0,
    Failed = 1,
}

/// <summary>
/// Every attempt to talk to the accounting system, with what was sent and what came back.
/// <para>
/// This exists because the legacy integration failed silently and its manual devotes a whole
/// troubleshooting section to "Last QB Request / Last QB Response" (guide p.111). Keeping the
/// payloads means a bookkeeper's "it didn't come through" is answerable from the screen instead of
/// from a support call.
/// </para>
/// </summary>
public sealed class SyncLog : Common.Entity
{
    public SyncLog()
    {
    }

    /// <summary>Which adapter ran — "csv", "quickbooks-online", and so on.</summary>
    public string Provider { get; set; } = string.Empty;

    public SyncDirection Direction { get; set; }

    /// <summary>What was being synced: Customers, Items, Vendors, Invoices, PosRevenue, Bill.</summary>
    public string Entity { get; set; } = string.Empty;

    public string? RequestPayload { get; set; }

    public string? ResponsePayload { get; set; }

    public SyncStatus Status { get; set; }

    public string? ErrorMessage { get; set; }

    public int RecordCount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public long DurationMs { get; set; }
}

/// <summary>
/// What a local record is called on the other side.
/// <para>
/// The legacy integration matched by name and stock code, which is why two customers called
/// "J. Smith" quietly became one (guide p.110–111). Mapping by identity instead means a rename is
/// a rename rather than a merge, and re-running a push is a no-op rather than a duplicate.
/// </para>
/// </summary>
public sealed class ExternalEntityMap : Common.Entity
{
    public ExternalEntityMap()
    {
    }

    public string Provider { get; set; } = string.Empty;

    /// <summary>Customer, Item, Vendor, Invoice, TenderType, TaxRate, Account, DiscountItem.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Null for mappings that point at a remote concept rather than a local row —
    /// the bank account a day's takings post to, for instance.</summary>
    public long? LocalId { get; set; }

    /// <summary>A stable local key for mappings with no row behind them, e.g. "BankAccount".</summary>
    public string? LocalKey { get; set; }

    public string RemoteId { get; set; } = string.Empty;

    public string? RemoteName { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>What was last pushed, so an unchanged record can be skipped rather than re-sent.</summary>
    public string? ContentHash { get; set; }
}
