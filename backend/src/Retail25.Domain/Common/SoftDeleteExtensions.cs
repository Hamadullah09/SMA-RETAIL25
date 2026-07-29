namespace Retail25.Domain.Common;

/// <summary>
/// The legacy "Undelete Items" command (user guide p.24), expressed once for every soft-deletable
/// entity rather than re-implemented on each one.
/// <para>
/// Deletion in this system marks; it does not destroy. The interceptor sets the three columns when
/// an entity is removed, and this clears them. They move together on purpose: an entity that was
/// visible again but still carried a <c>DeletedAt</c> would be missing from every "recently deleted"
/// list while sitting in the catalogue, which is the worst of both states.
/// </para>
/// </summary>
public static class SoftDeleteExtensions
{
    public static void Restore(this ISoftDeletable entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;
    }

    public static void MarkDeleted(this ISoftDeletable entity, DateTimeOffset now, Guid? actor)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.IsDeleted = true;
        entity.DeletedAt = now;
        entity.DeletedBy = actor;
    }
}
