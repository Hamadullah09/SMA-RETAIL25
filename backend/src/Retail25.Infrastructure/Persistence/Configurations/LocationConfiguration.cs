using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.LegacyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(l => l.TimeZoneId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(l => l.BaseCurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.OwnsAddress(l => l.Address);
        builder.OwnsContact(l => l.Contact);

        builder.HasIndex(l => l.LegacyCode)
            .IsUnique();

        builder.Ignore(l => l.DomainEvents);
    }
}
