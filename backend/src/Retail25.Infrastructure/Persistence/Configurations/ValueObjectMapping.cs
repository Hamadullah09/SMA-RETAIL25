using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared mapping for the address and contact value objects.
/// <para>
/// They are stored as columns on the owning table rather than as separate rows: an address has no
/// life of its own, is never queried without its owner, and joining for it on every customer lookup
/// would cost more than it saves. Column lengths follow the legacy import layouts (guide p.48, p.61)
/// so migrated data fits without truncation.
/// </para>
/// </summary>
internal static class ValueObjectMapping
{
    public static OwnedNavigationBuilder<TOwner, Address> MapAddress<TOwner>(
        this OwnedNavigationBuilder<TOwner, Address> builder)
        where TOwner : class
    {
        builder.Property(a => a.Line1).HasMaxLength(200);
        builder.Property(a => a.Line2).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(100);
        builder.Property(a => a.StateOrProvince).HasMaxLength(100);
        builder.Property(a => a.PostalCode).HasMaxLength(20);
        builder.Property(a => a.Country).HasMaxLength(100);
        return builder;
    }

    public static OwnedNavigationBuilder<TOwner, ContactDetails> MapContact<TOwner>(
        this OwnedNavigationBuilder<TOwner, ContactDetails> builder)
        where TOwner : class
    {
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Extension).HasMaxLength(10);
        builder.Property(c => c.Mobile).HasMaxLength(30);
        builder.Property(c => c.Fax).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Website).HasMaxLength(200);
        return builder;
    }
}
