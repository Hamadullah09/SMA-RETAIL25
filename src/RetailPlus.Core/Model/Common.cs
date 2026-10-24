namespace RetailPlus.Core.Model;

/// <summary>Rows that can be "trashed" and brought back with Undelete, as in the original.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Rows with the flag column of the browse grids. The flag is stored on the row, so every workstation
/// sees the same flags, as with the original's PRTFLAG field.
/// </summary>
public interface IFlaggable
{
    bool IsFlagged { get; set; }
}

/// <summary>Rows protected by optimistic concurrency (SQL Server rowversion).</summary>
public interface IVersioned
{
    byte[] RowVersion { get; set; }
}
