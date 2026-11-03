using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

// The supply chain tables, and the supply chain columns of tables configured elsewhere (EF applies every configuration
// of an entity, so these add to the ones in the other files).

internal sealed class ItemBatchConfiguration : IEntityTypeConfiguration<ItemBatch>
{
    public void Configure(EntityTypeBuilder<ItemBatch> e)
    {
        e.Property(x => x.BatchNumber).HasMaxLength(30);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasIndex(x => new { x.ItemId, x.BatchNumber }).IsUnique();
        e.HasIndex(x => new { x.StoreId, x.ExpiresOn });
    }
}

internal sealed class BinConfiguration : IEntityTypeConfiguration<Bin>
{
    public void Configure(EntityTypeBuilder<Bin> e)
    {
        e.Property(x => x.Code).HasMaxLength(25);
        e.Property(x => x.Zone).HasMaxLength(30);
        e.Property(x => x.Description).HasMaxLength(100);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.LocationId, x.Code }).IsUnique();
    }
}

internal sealed class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.RequestedBy).HasMaxLength(50);
        e.Property(x => x.DecidedBy).HasMaxLength(50);
        e.Property(x => x.Note).HasMaxLength(200);
        e.Property(x => x.DecisionNote).HasMaxLength(200);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseRequisitionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StoreId, x.Number }).IsUnique();
        e.HasIndex(x => new { x.StoreId, x.Status });
    }
}

internal sealed class PurchaseRequisitionLineConfiguration : IEntityTypeConfiguration<PurchaseRequisitionLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisitionLine> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.SupplierName).HasMaxLength(30);
        e.Property(x => x.Note).HasMaxLength(200);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.CostEach).UnitCost();
        e.HasIndex(x => new { x.PurchaseRequisitionId, x.LineNumber }).IsUnique();
    }
}

internal sealed class PurchaseApprovalLimitConfiguration : IEntityTypeConfiguration<PurchaseApprovalLimit>
{
    public void Configure(EntityTypeBuilder<PurchaseApprovalLimit> e)
    {
        e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.RoleId).IsUnique();
    }
}

internal sealed class SupplySettingsConfiguration : IEntityTypeConfiguration<SupplySettings>
{
    public void Configure(EntityTypeBuilder<SupplySettings> e)
    {
        e.Property(x => x.QuantityTolerancePercent).Rate();
        e.Property(x => x.QuantityToleranceUnits).Quantity();
        e.Property(x => x.PriceTolerancePercent).Rate();
        e.Property(x => x.PriceToleranceAmount).UnitCost();
        e.HasIndex(x => x.StoreId).IsUnique();
    }
}

internal sealed class BillMatchConfiguration : IEntityTypeConfiguration<BillMatch>
{
    public void Configure(EntityTypeBuilder<BillMatch> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.BillNumber).HasMaxLength(40);
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Reason).HasMaxLength(400);
        e.Property(x => x.DecidedBy).HasMaxLength(50);
        e.Property(x => x.DecisionNote).HasMaxLength(200);
        foreach (var q in new[] { nameof(BillMatch.QuantityOrdered), nameof(BillMatch.QuantityReceived), nameof(BillMatch.QuantityBilled), nameof(BillMatch.QuantityMatched) })
            e.Property<decimal>(q).HasPrecision(12, 3);
        foreach (var c in new[] { nameof(BillMatch.OrderedCost), nameof(BillMatch.ReceivedCost), nameof(BillMatch.BilledCost) })
            e.Property<decimal>(c).HasPrecision(12, 3);
        e.HasIndex(x => x.BillLineId).IsUnique();
        e.HasIndex(x => new { x.StoreId, x.Status });
        e.HasIndex(x => x.PurchaseOrderLineId);
        e.HasIndex(x => x.BillId);
    }
}

internal sealed class PriceVarianceConfiguration : IEntityTypeConfiguration<PriceVariance>
{
    public void Configure(EntityTypeBuilder<PriceVariance> e)
    {
        e.Property(x => x.Quantity).Quantity();
        e.HasIndex(x => x.PurchaseOrderLineId).IsUnique();
    }
}

internal sealed class LandedCostVoucherConfiguration : IEntityTypeConfiguration<LandedCostVoucher>
{
    public void Configure(EntityTypeBuilder<LandedCostVoucher> e)
    {
        e.Property(x => x.Basis).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.Note).HasMaxLength(200);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.PostedBy).HasMaxLength(50);
        e.HasMany(x => x.Charges).WithOne().HasForeignKey(x => x.LandedCostVoucherId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Receipts).WithOne().HasForeignKey(x => x.LandedCostVoucherId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.LandedCostVoucherId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StoreId, x.Number }).IsUnique();
    }
}

internal sealed class LandedCostChargeConfiguration : IEntityTypeConfiguration<LandedCostCharge>
{
    public void Configure(EntityTypeBuilder<LandedCostCharge> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Description).HasMaxLength(100);
    }
}

internal sealed class LandedCostReceiptConfiguration : IEntityTypeConfiguration<LandedCostReceipt>
{
    public void Configure(EntityTypeBuilder<LandedCostReceipt> e)
    {
        e.HasIndex(x => new { x.LandedCostVoucherId, x.PurchaseOrderId }).IsUnique();
        e.HasIndex(x => x.PurchaseOrderId);
    }
}

internal sealed class LandedCostAllocationConfiguration : IEntityTypeConfiguration<LandedCostAllocation>
{
    public void Configure(EntityTypeBuilder<LandedCostAllocation> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.Weight).Quantity();
        e.Property(x => x.OnHand).Quantity();
    }
}

// Supply chain columns of tables configured in the other files ------------------------------------------------------

internal sealed class StockMovementBatchConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> e)
    {
        e.HasOne<ItemBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ItemId, x.BatchId });
    }
}

internal sealed class StockCountBinConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> e)
    {
        e.Property(x => x.Zone).HasMaxLength(30);
        e.Property(x => x.BinCode).HasMaxLength(25);
    }
}

internal sealed class StockCountLineBatchConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> e) => e.Property(x => x.BatchNumber).HasMaxLength(30);
}

internal sealed class StockTransferLineBatchConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> e) => e.Property(x => x.BatchNumber).HasMaxLength(30);
}

internal sealed class PurchaseOrderLineCostConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> e) => e.Property(x => x.OrderedCost).UnitCost();
}

internal sealed class PurchaseOrderSupplierConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> e) => e.HasIndex(x => new { x.SupplierId, x.ReceivedAt });
}

internal sealed class ItemSupplierLeadTimeConfiguration : IEntityTypeConfiguration<ItemSupplier>
{
    public void Configure(EntityTypeBuilder<ItemSupplier> e) => e.Property(x => x.MinimumOrder).Quantity();
}
