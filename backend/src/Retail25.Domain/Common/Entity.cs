namespace Retail25.Domain.Common;

/// <summary>
/// Base class for every persisted object.
/// <para>
/// Identity is a 64-bit integer assigned by the database, because the records this system holds are
/// exchanged with an external system that addresses them by number. That is a requirement, and it is
/// worth being explicit about what it costs, since the cost is paid at every layer above.
/// </para>
/// <para>
/// An unsaved entity has <see cref="Id"/> = 0. Nothing can reference it until it has been saved, so
/// an aggregate and its children are no longer wireable in memory before the first
/// <c>SaveChanges</c> — the parent has to be saved to get its number before the children can point
/// at it. Handlers that used to build a graph and save once now save twice, inside one transaction.
/// </para>
/// <para>
/// Sequential numbers are also guessable, which a GUID is not. Every endpoint taking an id is now
/// enumerable by anyone who can call it, so authorisation carries weight it did not have to before:
/// a handler that only checked "is this a valid id" was previously protected by the id being
/// unguessable, and is now protected by nothing.
/// </para>
/// </summary>
public abstract class Entity
{
    /// <summary>Zero until the row is inserted. Assigned by the database, not by the domain.</summary>
    public long Id { get; internal set; }

    /// <summary>PostgreSQL <c>xmin</c> system column, mapped for optimistic concurrency.</summary>
    public uint RowVersion { get; protected set; }

    // Two unsaved entities are never equal, even to themselves by value: they both have Id 0, and
    // treating that as "the same record" would collapse a list of new lines into one.
    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != 0;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Audit columns applied by the persistence interceptor. Present on every entity so that
/// "who changed this and when" is never a question the system cannot answer.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    long? CreatedBy { get; set; }
    DateTimeOffset? ModifiedAt { get; set; }
    long? ModifiedBy { get; set; }
}

/// <summary>
/// Marks an entity that is hidden rather than destroyed. Implements the legacy
/// "Undelete Items" behaviour (user guide p.24) as a first-class, auditable state.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    long? DeletedBy { get; set; }
}
