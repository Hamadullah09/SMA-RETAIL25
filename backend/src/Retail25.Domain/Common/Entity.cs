namespace Retail25.Domain.Common;

/// <summary>
/// Base class for every persisted object. Identity is a GUID assigned in the domain, never by the
/// database, so an aggregate can raise events and be wired to children before it is ever saved.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; internal set; } = Guid.NewGuid();

    /// <summary>PostgreSQL <c>xmin</c> system column, mapped for optimistic concurrency.</summary>
    public uint RowVersion { get; protected set; }

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Audit columns applied by the persistence interceptor. Present on every entity so that
/// "who changed this and when" is never a question the system cannot answer.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? ModifiedAt { get; set; }
    Guid? ModifiedBy { get; set; }
}

/// <summary>
/// Marks an entity that is hidden rather than destroyed. Implements the legacy
/// "Undelete Items" behaviour (user guide p.24) as a first-class, auditable state.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
