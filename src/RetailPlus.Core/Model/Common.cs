namespace RetailPlus.Core.Model;

/// <summary>When a row was added and last saved, in UTC. The DbContext sets both on every save.</summary>
public interface ITracked
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Base of every stored row. The id is a version 7 GUID made where the row is first saved (a till or the server), so
/// a row keeps the same id on every till and on the server, and synchronising never has to renumber anything.
/// </summary>
public abstract class Entity : ITracked
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Rows that belong to one store. The DbContext fills in the store it works for when it is left empty.</summary>
public interface IStoreScoped
{
    Guid StoreId { get; set; }
}

/// <summary>
/// Rows made at a till: the store and the station that made them travel with the row, so the server can tell stores and
/// tills apart. The DbContext fills in its own station when <see cref="StationNumber"/> is left at 0.
/// </summary>
public interface IStationStamped : IStoreScoped
{
    int StationNumber { get; set; }
}

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

/// <summary>
/// Rows protected by optimistic concurrency: saving fails when someone else saved the row after it was read. The
/// DbContext gives the row a new version on every save (SQLite has no rowversion, so the token is kept by the app).
/// </summary>
public interface IVersioned
{
    Guid Version { get; set; }
}
