using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Catalog;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// The catalogue's natural key, enforced where it actually matters.
/// <para>
/// <see cref="ProductCommandHandlers"/> checks for a duplicate stock code before inserting, but that
/// check and the insert are two round trips — two stations creating the same code in the same instant
/// both pass the check and both insert. Only a unique index closes the gap; the application-level
/// check exists to give a clean, immediate error in the ordinary case, not to be the only guard.
/// </para>
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.StockCode)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Upc)
            .HasMaxLength(30);

        builder.Property(p => p.BinLocation)
            .HasMaxLength(50);

        // Soft-deleted rows keep their code — an item can be deleted and its code reused by a new
        // one — so the index has to exclude them or a restore, or the second item, could never exist.
        builder.HasIndex(p => new { p.LocationId, p.StockCode })
            .IsUnique()
            .HasFilter("NOT is_deleted");

        builder.Ignore(p => p.DomainEvents);
    }
}
