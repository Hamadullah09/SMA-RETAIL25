using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Sales;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class SalesTransactionConfiguration : IEntityTypeConfiguration<SalesTransaction>
{
    public void Configure(EntityTypeBuilder<SalesTransaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Subtotal)
            .HasPrecision(19, 4);

        builder.Property(t => t.DiscountTotal)
            .HasPrecision(19, 4);

        builder.Property(t => t.AddOnChargeTotal)
            .HasPrecision(19, 4);

        builder.Property(t => t.Tax1Total)
            .HasPrecision(19, 4);

        builder.Property(t => t.Tax2Total)
            .HasPrecision(19, 4);

        builder.Property(t => t.GrandTotal)
            .HasPrecision(19, 4);

        builder.Property(t => t.CostOfGoodsSold)
            .HasPrecision(19, 4);

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(t => new { t.LocationId, t.CompletedAt });

        builder.HasIndex(t => t.TransactionNumber);

        builder.Ignore(t => t.DomainEvents);
    }
}
