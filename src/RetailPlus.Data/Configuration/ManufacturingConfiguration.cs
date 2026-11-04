using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class WorkCentreConfiguration : IEntityTypeConfiguration<WorkCentre>
{
    public void Configure(EntityTypeBuilder<WorkCentre> e)
    {
        e.Property(x => x.Code).HasMaxLength(10);
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.LabourCostPerHour).UnitCost();
        e.Property(x => x.MachineCostPerHour).UnitCost();
        e.Property(x => x.OverheadPerHour).UnitCost();
        e.Property(x => x.CapacityHoursPerDay).HasPrecision(5, 2);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
    }
}

internal sealed class BillOfMaterialsConfiguration : IEntityTypeConfiguration<BillOfMaterials>
{
    public void Configure(EntityTypeBuilder<BillOfMaterials> e)
    {
        e.ToTable("BillsOfMaterials");
        e.Property(x => x.ProductCode).HasMaxLength(16);
        e.Property(x => x.ProductName).HasMaxLength(30);
        e.Property(x => x.YieldQuantity).Quantity();
        e.Property(x => x.OverheadPercent).Rate();
        e.Property(x => x.Notes).HasMaxLength(400);
        e.HasIndex(x => new { x.ProductCode, x.Revision }).IsUnique();
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.ByProducts).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Operations).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> e)
    {
        e.Property(x => x.ComponentCode).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).HasPrecision(14, 4);
        e.Property(x => x.ScrapPercent).Rate();
        e.Property(x => x.IssueMethod).HasConversion<string>().HasMaxLength(10);
        e.HasIndex(x => new { x.BillOfMaterialsId, x.LineNumber });
        e.HasIndex(x => x.ComponentCode);
    }
}

internal sealed class BomByProductConfiguration : IEntityTypeConfiguration<BomByProduct>
{
    public void Configure(EntityTypeBuilder<BomByProduct> e)
    {
        e.Property(x => x.StockCode).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).HasPrecision(14, 4);
        e.Property(x => x.CostSharePercent).Rate();
        e.HasIndex(x => new { x.BillOfMaterialsId, x.LineNumber });
    }
}

internal sealed class RoutingOperationConfiguration : IEntityTypeConfiguration<RoutingOperation>
{
    public void Configure(EntityTypeBuilder<RoutingOperation> e)
    {
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.SetupMinutes).HasPrecision(10, 2);
        e.Property(x => x.RunMinutesPerUnit).HasPrecision(12, 4);
        e.Property(x => x.Instructions).HasMaxLength(1000);
        e.HasOne(x => x.WorkCentre).WithMany().HasForeignKey(x => x.WorkCentreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.BillOfMaterialsId, x.Sequence }).IsUnique();
    }
}

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> e)
    {
        e.Property(x => x.ProductCode).HasMaxLength(16);
        e.Property(x => x.ProductName).HasMaxLength(30);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.Property(x => x.StandardMaterialCost).UnitCost();
        e.Property(x => x.StandardLabourCost).UnitCost();
        e.Property(x => x.StandardMachineCost).UnitCost();
        e.Property(x => x.StandardOverheadCost).UnitCost();
        e.Property(x => x.OverheadPercent).Rate();
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.ReleasedBy).HasMaxLength(50);
        e.Property(x => x.CompletedBy).HasMaxLength(50);
        e.Property(x => x.ClosedBy).HasMaxLength(50);
        e.Property(x => x.CancelledBy).HasMaxLength(50);
        e.Ignore(x => x.StandardUnitCost);
        e.Ignore(x => x.IsOpen);
        e.HasOne(x => x.MaterialLocation).WithMany().HasForeignKey(x => x.MaterialLocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.OutputLocation).WithMany().HasForeignKey(x => x.OutputLocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BillOfMaterials>().WithMany().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.CustomerOrderId).OnDelete(DeleteBehavior.SetNull);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Operations).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StationNumber, x.Number });
        e.HasIndex(x => new { x.StoreId, x.Status });
        e.HasIndex(x => x.ProductCode);
    }
}

internal sealed class ProductionOrderLineConfiguration : IEntityTypeConfiguration<ProductionOrderLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderLine> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.StockCode).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.QuantityPerUnit).HasPrecision(14, 6);
        e.Property(x => x.QuantityPlanned).Quantity();
        e.Property(x => x.QuantityReserved).Quantity();
        e.Property(x => x.ScrapPercent).Rate();
        e.Property(x => x.CostSharePercent).Rate();
        e.Property(x => x.StandardUnitCost).UnitCost();
        e.Property(x => x.IssueMethod).HasConversion<string>().HasMaxLength(10);
        e.HasIndex(x => new { x.ProductionOrderId, x.LineNumber });
        e.HasIndex(x => x.ItemId);
    }
}

internal sealed class ProductionOrderOperationConfiguration : IEntityTypeConfiguration<ProductionOrderOperation>
{
    public void Configure(EntityTypeBuilder<ProductionOrderOperation> e)
    {
        e.Property(x => x.WorkCentreName).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.SetupMinutes).HasPrecision(10, 2);
        e.Property(x => x.RunMinutesPerUnit).HasPrecision(12, 4);
        e.Property(x => x.Instructions).HasMaxLength(1000);
        e.Property(x => x.LabourRate).UnitCost();
        e.Property(x => x.MachineRate).UnitCost();
        e.Property(x => x.OverheadRate).UnitCost();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        // The work centre is only named here (the order keeps its own copy of the rates), so it is not a reference the sync follows.
        e.HasIndex(x => new { x.ProductionOrderId, x.Sequence }).IsUnique();
        e.HasIndex(x => x.WorkCentreId);
    }
}

internal sealed class ProductionReportConfiguration : IEntityTypeConfiguration<ProductionReport>
{
    public void Configure(EntityTypeBuilder<ProductionReport> e)
    {
        e.Property(x => x.Minutes).HasPrecision(10, 2);
        e.Property(x => x.GoodQuantity).Quantity();
        e.Property(x => x.ScrapQuantity).Quantity();
        e.Property(x => x.UserName).HasMaxLength(50);
        e.Property(x => x.LabourCost).HasPrecision(14, 2);
        e.Property(x => x.MachineCost).HasPrecision(14, 2);
        e.Property(x => x.OverheadCost).HasPrecision(14, 2);
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ProductionOrderId, x.OperationSequence });
        e.HasIndex(x => x.At);
    }
}

/// <summary>Production adds to the stock movements a column and an index of its own (the rest is in <see cref="StockMovementConfiguration"/>).</summary>
internal sealed class StockMovementProductionConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> e) => e.HasIndex(x => x.ProductionOrderId);
}

internal sealed class MrpRunConfiguration : IEntityTypeConfiguration<MrpRun>
{
    public void Configure(EntityTypeBuilder<MrpRun> e)
    {
        e.Property(x => x.RunBy).HasMaxLength(50);
        e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StoreId, x.RunAtUtc });
    }
}

internal sealed class MrpSuggestionConfiguration : IEntityTypeConfiguration<MrpSuggestion>
{
    public void Configure(EntityTypeBuilder<MrpSuggestion> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.StockCode).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.SupplierName).HasMaxLength(60);
        e.Property(x => x.Reason).HasMaxLength(200);
        e.Property(x => x.FirmedDocument).HasMaxLength(40);
        e.Property(x => x.FirmedBy).HasMaxLength(50);
        e.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StoreId, x.Status });
    }
}

internal sealed class SupplierLeadTimeConfiguration : IEntityTypeConfiguration<SupplierLeadTime>
{
    public void Configure(EntityTypeBuilder<SupplierLeadTime> e)
    {
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.SupplierId).IsUnique();
    }
}

internal sealed class QualityPlanConfiguration : IEntityTypeConfiguration<QualityPlan>
{
    public void Configure(EntityTypeBuilder<QualityPlan> e)
    {
        e.Property(x => x.ProductCode).HasMaxLength(16);
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.HasIndex(x => x.ProductCode).IsUnique();
        e.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.QualityPlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QualityCheckConfiguration : IEntityTypeConfiguration<QualityCheck>
{
    public void Configure(EntityTypeBuilder<QualityCheck> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Minimum).HasPrecision(14, 4);
        e.Property(x => x.Maximum).HasPrecision(14, 4);
        e.Property(x => x.Unit).HasMaxLength(10);
        e.HasIndex(x => new { x.QualityPlanId, x.LineNumber });
    }
}

internal sealed class QualityInspectionConfiguration : IEntityTypeConfiguration<QualityInspection>
{
    public void Configure(EntityTypeBuilder<QualityInspection> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.StockCode).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.QuantityInspected).Quantity();
        e.Property(x => x.QuantityFailed).Quantity();
        e.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.InspectedBy).HasMaxLength(50);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<QualityPlan>().WithMany().HasForeignKey(x => x.QualityPlanId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Location>().WithMany().HasForeignKey(x => x.HoldLocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.QualityInspectionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StoreId, x.InspectedAt });
    }
}

internal sealed class QualityResultConfiguration : IEntityTypeConfiguration<QualityResult>
{
    public void Configure(EntityTypeBuilder<QualityResult> e)
    {
        e.Property(x => x.CheckName).HasMaxLength(60);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Minimum).HasPrecision(14, 4);
        e.Property(x => x.Maximum).HasPrecision(14, 4);
        e.Property(x => x.NumberValue).HasPrecision(14, 4);
        e.Property(x => x.TextValue).HasMaxLength(200);
    }
}
