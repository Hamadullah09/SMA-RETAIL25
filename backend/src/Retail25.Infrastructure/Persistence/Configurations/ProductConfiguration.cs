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
        //
        // `[is_deleted] = 0` rather than `NOT is_deleted`: SQL Server's filtered indexes take only
        // simple comparisons, and a negation is rejected at CREATE INDEX rather than at any point
        // that would name this line.
        builder.HasIndex(p => new { p.LocationId, p.StockCode })
            .IsUnique()
            .HasFilter("[is_deleted] = 0");

        // The barcode, which had no index at all.
        //
        // `IdentifierResolver` resolves a scan with `StockCode == code || Upc == code`, so every
        // barcode scanned at a till was a full scan of the products table. It is invisible against
        // the 201 rows this deployment holds and it is the hottest path in the shop: a cashier
        // scanning, once per item, on every terminal at once. A real catalogue turns that into a
        // table scan per beep.
        //
        // Unique for the same reason the stock code is: a barcode identifies one product, and
        // "barcode already assigned to X" is a message the application can only give reliably if
        // the database is the one enforcing it. Verified against live data before adding — zero
        // duplicate (location, upc) groups — because migrations run at startup here, so an index
        // that cannot be built is an application that cannot boot.
        //
        // Filtered on NULL and empty as well as deleted: most products carry no barcode, and
        // without the filter every one of them would collide with every other. `<>` is a simple
        // comparison and is accepted in a filtered predicate, unlike NOT or OR.
        builder.HasIndex(p => new { p.LocationId, p.Upc })
            .IsUnique()
            .HasDatabaseName("ix_products_location_id_upc")
            .HasFilter("[upc] IS NOT NULL AND [upc] <> '' AND [is_deleted] = 0");

        builder.Ignore(p => p.DomainEvents);
    }
}
