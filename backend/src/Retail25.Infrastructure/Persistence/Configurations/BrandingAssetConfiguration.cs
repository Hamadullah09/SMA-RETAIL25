using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Branding images, one per slot per location.
/// <para>
/// The unique index on (location, slot) is what makes "one per slot" true in the database rather
/// than only in the handler. Two administrators uploading a logo at the same moment is a race, and
/// the second upload should replace the first rather than leave two rows and an arbitrary winner
/// at render time — a shop that sees its old logo on one till and its new one on another has no way
/// to tell which row will win next.
/// </para>
/// </summary>
public class BrandingAssetConfiguration : IEntityTypeConfiguration<BrandingAsset>
{
    public void Configure(EntityTypeBuilder<BrandingAsset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.LocationId, a.Slot }).IsUnique();

        builder.Property(a => a.Slot)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(a => a.ETag)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(a => a.Content)
            .IsRequired();

        // Deleting a location takes its branding with it. Nothing else refers to the row, and the
        // images are meaningless without the shop they belong to.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
