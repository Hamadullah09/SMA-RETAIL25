using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Column mapping for the two value objects that appear on several aggregates.
/// <para>
/// EF will not infer an owned type's shape, and an unconfigured one fails model validation rather
/// than defaulting to something harmless. Sharing one definition here means a client address, a
/// supplier address and a location address all get the same column widths — which matters when the
/// legacy importer maps a fixed-width field into any of them.
/// </para>
/// </summary>
internal static class OwnedValueObjectConventions
{
    public static EntityTypeBuilder<TEntity> OwnsAddress<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Address?>> navigation)
        where TEntity : class
    {
        builder.OwnsOne(navigation, address =>
        {
            address.Property(a => a.Line1).HasMaxLength(200);
            address.Property(a => a.Line2).HasMaxLength(200);
            address.Property(a => a.City).HasMaxLength(100);
            address.Property(a => a.StateOrProvince).HasMaxLength(100);
            address.Property(a => a.PostalCode).HasMaxLength(20);
            address.Property(a => a.Country).HasMaxLength(100);
        });

        return builder;
    }

    public static EntityTypeBuilder<TEntity> OwnsContact<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, ContactDetails?>> navigation)
        where TEntity : class
    {
        builder.OwnsOne(navigation, contact =>
        {
            contact.Property(c => c.Phone).HasMaxLength(30);
            contact.Property(c => c.Extension).HasMaxLength(10);
            contact.Property(c => c.Mobile).HasMaxLength(30);
            contact.Property(c => c.Fax).HasMaxLength(30);
            contact.Property(c => c.Email).HasMaxLength(200);
            contact.Property(c => c.Website).HasMaxLength(200);
        });

        return builder;
    }
}
