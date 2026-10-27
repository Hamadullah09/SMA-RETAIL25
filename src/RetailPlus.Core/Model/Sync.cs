namespace RetailPlus.Core.Model;

/// <summary>
/// Rows that stay in the database they were written in: the synchronisation never sends them (its own bookkeeping, and
/// the server's secrets). Deleting one leaves no <see cref="Tombstone"/>.
/// </summary>
public interface ILocalOnly
{
}

/// <summary>
/// A row deleted from this database, so the deletion reaches the other databases: the next package carries it and they
/// delete the row too. Written by the DbContext in the same save as the deletion.
/// </summary>
public class Tombstone : Entity, ILocalOnly
{
    /// <summary>The CLR name of the deleted row's entity type, e.g. "ItemSupplier".</summary>
    public string EntityName { get; set; } = "";
    /// <summary>The deleted row's key.</summary>
    public Guid RowId { get; set; }
    public DateTime DeletedAtUtc { get; set; }
    /// <summary>The store of the deleted row when it belonged to one; null when it was shared by every store.</summary>
    public Guid? StoreId { get; set; }
    /// <summary>
    /// Set when the row was not deleted but merged into another one with the same code (the same stock code made at two
    /// tills): rows that pointed at it point at this one instead.
    /// </summary>
    public Guid? MergedIntoId { get; set; }
}

/// <summary>Which way a package travels.</summary>
public enum SyncDirection
{
    /// <summary>From a till to the server.</summary>
    Upload,
    /// <summary>From the server to a till.</summary>
    Download,
}

public enum SyncBatchStatus
{
    /// <summary>Every row of the package is in this database.</summary>
    Applied,
    /// <summary>The package was applied before; nothing was done.</summary>
    Duplicate,
    /// <summary>The package was refused (damaged, wrong key, or a row that could not be applied); nothing was kept.</summary>
    Failed,
    /// <summary>On a till: the package was written to the outbox and is waiting to be sent.</summary>
    Waiting,
    /// <summary>On a till: the server accepted the package.</summary>
    Sent,
}

/// <summary>
/// One sync package that reached this database, or (on a till) one it wrote: what it held and what became of it. Every
/// arrival is recorded, also refused and duplicate ones, so the server can show which tills are behind or failing.
/// </summary>
public class SyncBatch : Entity, ILocalOnly
{
    /// <summary>The package's own id (a version 7 GUID made where the package was written).</summary>
    public Guid BatchId { get; set; }
    public Guid? StoreId { get; set; }
    public int StationNumber { get; set; }
    public SyncDirection Direction { get; set; }
    public string? FileName { get; set; }
    /// <summary>SHA-256 of the whole package file, hexadecimal.</summary>
    public string? FileSha256 { get; set; }
    /// <summary>Rows and deletions in the package.</summary>
    public int Rows { get; set; }
    public string? AppVersion { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public SyncBatchStatus Status { get; set; }
    /// <summary>Why the package was refused.</summary>
    public string? Error { get; set; }
    /// <summary>What applying it decided on its own, such as two rows with the same stock code merged into one.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// A till's progress with the server (one row): what it has already packaged, what it has received, and how the last
/// attempt went.
/// </summary>
public class SyncState : Entity, ILocalOnly
{
    /// <summary>Rows changed after this time (UTC) are not in a package yet.</summary>
    public DateTime PackagedThroughUtc { get; set; }
    /// <summary>Rows changed up to this time (UTC) are on the server: the newest package it accepted went this far.</summary>
    public DateTime SentThroughUtc { get; set; }
    /// <summary>The server's change number this till has received everything up to.</summary>
    public long DownloadCursor { get; set; }
    public DateTime? LastSentAtUtc { get; set; }
    public DateTime? LastReceivedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    /// <summary>Why the last attempt failed; null when it worked.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// The server's side of a till: its signing key, the enrolment code that joins it, and how its synchronisation goes.
/// Kept on the server only; it never travels to a till.
/// </summary>
public class StationLink : Entity, ILocalOnly
{
    public Guid StationId { get; set; }
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    /// <summary>The station's key, encrypted with the server's data-protection keys; null until the till joins.</summary>
    public string? ProtectedKey { get; set; }
    /// <summary>SHA-256 of the one-time enrolment code, hexadecimal; null once used.</summary>
    public string? EnrollmentCodeHash { get; set; }
    public DateTime? EnrollmentExpiresAtUtc { get; set; }
    public DateTime? EnrolledAtUtc { get; set; }
    /// <summary>A retired till's key is no longer accepted.</summary>
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime? LastUploadAtUtc { get; set; }
    public DateTime? LastDownloadAtUtc { get; set; }
    /// <summary>The change number the till last said it had received.</summary>
    public long DownloadCursor { get; set; }
    public string? AppVersion { get; set; }
}
