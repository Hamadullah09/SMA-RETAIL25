using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.StartedBy).HasMaxLength(50);
        e.Property(x => x.PostedBy).HasMaxLength(50);
        e.Property(x => x.CancelledBy).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StationNumber, x.Number });
        e.HasIndex(x => new { x.LocationId, x.Status });
    }
}

internal sealed class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Expected).Quantity();
        e.Property(x => x.Counted).Quantity();
        e.Property(x => x.Variance).Quantity();
        e.Property(x => x.UnitCost).UnitCost();
        e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StockCountId, x.ItemId, x.VariantId });
    }
}

internal sealed class StockCountTagConfiguration : IEntityTypeConfiguration<StockCountTag>
{
    public void Configure(EntityTypeBuilder<StockCountTag> e)
    {
        e.Property(x => x.Epc).HasMaxLength(128).IsUnicode(false);
        e.HasOne<StockCount>().WithMany().HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StockCountId, x.Epc }).IsUnique();
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.ShippedBy).HasMaxLength(50);
        e.Property(x => x.ReceivedBy).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.ToStoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StationNumber, x.Number });
        e.HasIndex(x => new { x.ToStoreId, x.Status });
        e.ToTable(t => t.HasCheckConstraint("CK_StockTransfer_Locations", "[FromLocationId] <> [ToLocationId]"));
    }
}

internal sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Barcode).HasMaxLength(14).IsUnicode(false);
        e.Property(x => x.Size).HasMaxLength(6);
        e.Property(x => x.Department).HasMaxLength(15);
        e.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.UnitCost).UnitCost();
        e.Property(x => x.QuantityShipped).Quantity();
        e.Property(x => x.QuantityReceived).Quantity();
        e.HasIndex(x => new { x.StockTransferId, x.LineNumber }).IsUnique();
    }
}

internal sealed class GiftCardConfiguration : IEntityTypeConfiguration<GiftCard>
{
    public void Configure(EntityTypeBuilder<GiftCard> e)
    {
        e.Property(x => x.Number).HasMaxLength(30);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasMany(x => x.Transactions).WithOne().HasForeignKey(x => x.GiftCardId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.Number).IsUnique();
    }
}

internal sealed class GiftCardTransactionConfiguration : IEntityTypeConfiguration<GiftCardTransaction>
{
    public void Configure(EntityTypeBuilder<GiftCardTransaction> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.User).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasIndex(x => new { x.GiftCardId, x.At });
        e.HasIndex(x => x.SaleId);
    }
}

internal sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Points).Quantity();
        e.Property(x => x.User).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ClientId, x.At });
        e.HasIndex(x => x.SaleId);
    }
}

internal sealed class AccountMappingConfiguration : IEntityTypeConfiguration<AccountMapping>
{
    public void Configure(EntityTypeBuilder<AccountMapping> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Key).HasMaxLength(30);
        e.Property(x => x.AccountCode).HasMaxLength(30);
        e.Property(x => x.AccountName).HasMaxLength(60);
        e.HasIndex(x => new { x.StoreId, x.Kind, x.Key }).IsUnique();
    }
}

internal sealed class StoreImageConfiguration : IEntityTypeConfiguration<StoreImage>
{
    public void Configure(EntityTypeBuilder<StoreImage> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.HasIndex(x => new { x.StoreId, x.Kind }).IsUnique();
    }
}
