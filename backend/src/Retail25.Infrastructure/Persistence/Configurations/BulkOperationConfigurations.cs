using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Inventory;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Transfers and stock counts. Computed properties (<c>Outstanding</c>, <c>Variance</c>) are ignored
/// — they are arithmetic over columns that are already stored, and persisting them is one more thing
/// that can disagree with itself.
/// </summary>
public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.FromLocationId, e.Status });
        builder.HasIndex(e => new { e.ToLocationId, e.Status });

        // The number is what people say out loud on the phone about a box that has not arrived, so
        // it has to be unique per originating store.
        builder.HasIndex(e => new { e.FromLocationId, e.TransferNumber }).IsUnique();
    }
}

public sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StockCode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.QuantityReceived).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(19, 3);

        builder.Ignore(e => e.Outstanding);

        builder.HasIndex(e => e.StockTransferId);

        // One line per item: two lines for the same product would let a partial receipt be booked
        // against either of them, and the outstanding figure would depend on which one was picked.
        builder.HasIndex(e => new { e.StockTransferId, e.ProductId, e.VariantId }).IsUnique();
    }
}

public sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.LocationId, e.Status });
        builder.HasIndex(e => new { e.LocationId, e.CountNumber }).IsUnique();
    }
}

public sealed class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StockCode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CountedQty).HasPrecision(18, 4);
        builder.Property(e => e.SystemQtyAtCount).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(19, 3);
        builder.Property(e => e.Notes).HasMaxLength(200);

        builder.Ignore(e => e.Variance);
        builder.Ignore(e => e.VarianceValue);

        builder.HasIndex(e => e.StockCountId);

        // Counting the same item twice is a correction to the first figure, so the row is unique and
        // the importer updates it rather than appending.
        builder.HasIndex(e => new { e.StockCountId, e.ProductId, e.VariantId }).IsUnique();
    }
}
