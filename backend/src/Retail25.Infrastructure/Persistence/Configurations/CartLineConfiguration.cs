using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Sales;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity)
            .HasPrecision(18, 4);

        builder.Property(l => l.UnitPrice)
            .HasPrecision(19, 4);

        builder.Property(l => l.LineDiscountPct)
            .HasPrecision(7, 2);

        builder.Property(l => l.Source)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.PriceOrigin)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.LineType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.StockCodeSnapshot)
            .HasMaxLength(24);

        builder.Property(l => l.NameSnapshot)
            .HasMaxLength(200);

        builder.Property(l => l.UnitCostSnapshot)
            .HasPrecision(19, 3);

        builder.HasIndex(l => l.CartId);
    }
}
