using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Catalog;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Product pictures, one per item.
/// <para>
/// The unique index on <see cref="ProductImage.ProductId"/> is the constraint that makes "one per
/// item" true in the database rather than only in the handler — two clerks uploading a photo of the
/// same item at once is a race, and the second one should replace the first rather than leave two
/// rows and an arbitrary winner at render time.
/// </para>
/// </summary>
public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => i.ProductId).IsUnique();

        builder.Property(i => i.ContentType)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(i => i.ETag)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.Content)
            .IsRequired();

        // Deleting a product takes its picture with it. Nothing else refers to the row, and an
        // orphaned two-megabyte blob per deleted item is a slow way to fill a disk.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
