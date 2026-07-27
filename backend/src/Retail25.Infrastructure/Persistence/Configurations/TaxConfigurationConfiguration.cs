using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class TaxConfigurationConfiguration : IEntityTypeConfiguration<TaxConfiguration>
{
    public void Configure(EntityTypeBuilder<TaxConfiguration> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Tax1Name)
            .HasMaxLength(50);

        builder.Property(t => t.Tax1Rate)
            .HasPrecision(7, 4);

        builder.Property(t => t.Tax2Name)
            .HasMaxLength(50);

        builder.Property(t => t.Tax2Rate)
            .HasPrecision(7, 4);

        builder.Property(t => t.AddOnChargeName)
            .HasMaxLength(50);

        builder.Property(t => t.AddOnChargeRate)
            .HasPrecision(7, 4);

        builder.Property(t => t.TaxationType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.RegistrationNumber)
            .HasMaxLength(50);

        builder.HasIndex(t => new { t.LocationId, t.EffectiveFrom })
            .IsDescending();

        builder.Ignore(t => t.DomainEvents);
    }
}
