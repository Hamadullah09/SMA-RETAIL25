using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Business identity as it appears on invoices and receipts (guide p.76). One row per location, so a
/// multi-store business can print the right registration number on each store's documents.
/// </summary>
public sealed class BusinessProfileConfiguration : IEntityTypeConfiguration<BusinessProfile>
{
    public void Configure(EntityTypeBuilder<BusinessProfile> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BusinessName).IsRequired().HasMaxLength(200);
        builder.Property(b => b.LicenceNumber).HasMaxLength(60);
        builder.Property(b => b.TaxRegistrationNumber).HasMaxLength(40);

        builder.OwnsAddress(b => b.Address);
        builder.OwnsContact(b => b.Contact);

        builder.HasIndex(b => b.LocationId).IsUnique();

        builder.Ignore(b => b.DomainEvents);
    }
}
