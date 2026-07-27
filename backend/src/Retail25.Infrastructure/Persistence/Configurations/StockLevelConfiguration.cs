using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Inventory;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OnHand)
            .HasPrecision(18, 4);

        builder.Property(s => s.OnOrder)
            .HasPrecision(18, 4);

        builder.Property(s => s.Committed)
            .HasPrecision(18, 4);

        builder.HasIndex(s => new { s.ProductId, s.VariantId, s.LocationId })
            .IsUnique();
    }
}
