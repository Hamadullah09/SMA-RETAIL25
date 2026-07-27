using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Inventory;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.MovementType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Quantity)
            .HasPrecision(18, 4);

        builder.Property(e => e.UnitCost)
            .HasPrecision(19, 3);

        builder.HasIndex(e => new { e.ProductId, e.LocationId, e.OccurredAt });
    }
}
