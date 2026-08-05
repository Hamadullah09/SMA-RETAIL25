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

    /// <summary>
    /// A column, and <b>not currently a concurrency token</b>.
    /// <para>
    /// It was written to be PostgreSQL's <c>xmin</c> system column, and the mapping that would have
    /// made that true was never added — no <c>UseXminAsConcurrencyToken</c>, no
    /// <c>IsConcurrencyToken</c>. It is a plain <c>bigint</c> that nothing writes and nothing checks,
    /// so two clerks editing one product both save and neither is told. The comment claiming
    /// otherwise outlived the engine it named; this one says what is there.
    /// </para>
    /// <para>
    /// On SQL Server the fix is a <c>byte[]</c> with <c>IsRowVersion()</c>, which the engine
    /// maintains itself. Tracked separately — do not rely on this field until it is done.
    /// </para>
    /// </summary>
    public uint RowVersion { get; protected set; }

    /// <summary>
    /// Same row, or literally the same object.
    /// <para>
    /// The reference check is not a nicety. Two <em>different</em> unsaved entities both have id 0
    /// and must not compare equal — otherwise a list of new sale lines collapses into one. But an
    /// unsaved entity must still equal itself, because that is what <c>List.Remove</c>,
    /// <c>Contains</c> and <c>Distinct</c> ask. Without it, removing a line from a cart that has not
    /// been saved silently does nothing: the list is asked to find an item that does not equal
    /// itself, finds nothing, and reports success.
    /// </para>
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not Entity other || other.GetType() != GetType())
        {
            return false;
        }

        return ReferenceEquals(this, other) || (Id != 0 && other.Id == Id);
    }

    /// <summary>
    /// Hashed on the id, which means an unsaved entity's hash <b>changes when it is saved</b>.
    /// <para>
    /// Unavoidable — the id is the identity, and the database assigns it — but it has one sharp edge
    /// worth stating: <b>never key a dictionary or a set by an entity across a SaveChanges</b>. The
    /// entry goes in hashed as 0 and is looked up hashed as its real id, so it is simply not found.
    /// Carry what you need in a list or key by something stable, such as the stock code.
    /// </para>
    /// <para>
    /// Unsaved entities also all hash alike. That part is harmless: equality still tells them apart,
    /// and they only share a bucket.
    /// </para>
    /// </summary>
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
