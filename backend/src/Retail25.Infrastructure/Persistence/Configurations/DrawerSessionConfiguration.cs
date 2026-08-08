using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class DrawerSessionConfiguration : IEntityTypeConfiguration<DrawerSession>
{
    public void Configure(EntityTypeBuilder<DrawerSession> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.OpeningFloat)
            .HasPrecision(19, 4);

        builder.Property(d => d.CountedCash)
            .HasPrecision(19, 4);

        builder.Property(d => d.ExpectedCash)
            .HasPrecision(19, 4);

        builder.Property(d => d.Variance)
            .HasPrecision(19, 4);

        builder.Property(d => d.Tax1Collected)
            .HasPrecision(19, 4);

        builder.Property(d => d.Tax2Collected)
            .HasPrecision(19, 4);

        builder.Property(d => d.CostOfGoodsSold)
            .HasPrecision(19, 4);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(d => new { d.StationId, d.Status })
            .HasFilter("[status] = 'Open'");

        builder.Ignore(d => d.DomainEvents);
    }
}
