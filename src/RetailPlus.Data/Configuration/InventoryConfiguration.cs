using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e)
    {
        e.Property(x => x.Code).HasMaxLength(3);
        e.Property(x => x.Name).HasMaxLength(50);
        e.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> e)
    {
        e.Property(x => x.Name).HasMaxLength(15);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.Property(x => x.Name).HasMaxLength(15);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.Barcode).HasMaxLength(14).IsUnicode(false);
        e.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Brand).HasMaxLength(16);
        e.Property(x => x.Size).HasMaxLength(6);
        e.Property(x => x.WeightUnit).HasMaxLength(4);
        e.Property(x => x.SupplierCode).HasMaxLength(16);
        e.Property(x => x.BinLocation).HasMaxLength(25);
        e.Property(x => x.SubstituteCode).HasMaxLength(16);
        e.Property(x => x.TagAlongCode).HasMaxLength(16);
        e.Property(x => x.ParentCode).HasMaxLength(16);
        e.Property(x => x.CommissionType).HasMaxLength(20);
        e.Property(x => x.PhotoPath).HasMaxLength(260);

        e.Property(x => x.OnHand).Quantity();
        e.Property(x => x.BaseStock).Quantity();
        e.Property(x => x.ReorderPoint).Quantity();
        e.Property(x => x.ReorderQuantity).Quantity();
        e.Property(x => x.OnOrder).Quantity();
        e.Property(x => x.LastReceivedQuantity).Quantity();
        e.Property(x => x.CustomerOrdered).Quantity();
        e.Property(x => x.OnHandBeforeYearEnd).Quantity();
        e.Property(x => x.BreakQuantity1).Quantity();
        e.Property(x => x.BreakQuantity2).Quantity();
        e.Property(x => x.BreakQuantity3).Quantity();
        e.Property(x => x.ShipWeight).Quantity();
        e.Property(x => x.ThcWeight).Quantity();
        e.Property(x => x.Weight).Quantity();
        e.Property(x => x.LastCost).UnitCost();
        e.Property(x => x.AverageCost).UnitCost();
        e.Property(x => x.MarginPercent).Rate();
        e.Property(x => x.RowVersion).IsRowVersion();

        e.HasIndex(x => new { x.LocationId, x.Barcode });

        // Lookups are removed by the app, not by cascading deletes (items are soft-deleted).
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.ClientSetNull);

        // The original's index tags: PLU, <LOC>INV (name), DEPT+PLU, SUPPLIER+PLU, SUPPCODE.
        e.HasIndex(x => new { x.LocationId, x.Plu }).IsUnique().HasFilter("[IsDeleted] = 0");
        e.HasIndex(x => new { x.LocationId, x.Name });
        e.HasIndex(x => new { x.LocationId, x.DepartmentId, x.Plu });
        e.HasIndex(x => new { x.LocationId, x.SupplierId, x.Plu });
        e.HasIndex(x => new { x.LocationId, x.SupplierCode });
    }
}

internal sealed class ItemSupplierConfiguration : IEntityTypeConfiguration<ItemSupplier>
{
    public void Configure(EntityTypeBuilder<ItemSupplier> e)
    {
        e.Property(x => x.SupplierName).HasMaxLength(30);
        e.Property(x => x.SupplierStockCode).HasMaxLength(30);
        e.Property(x => x.Cost).UnitCost();
        e.Property(x => x.CaseQuantity).Quantity();
        e.HasOne<Item>().WithMany(x => x.Suppliers).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasIndex(x => new { x.ItemId, x.Rank }).IsUnique();
    }
}

internal sealed class ItemMatrixColumnConfiguration : IEntityTypeConfiguration<ItemMatrixColumn>
{
    public void Configure(EntityTypeBuilder<ItemMatrixColumn> e)
    {
        e.Property(x => x.Heading).HasMaxLength(50);
        e.HasOne<Item>().WithMany(x => x.MatrixColumns).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ItemId, x.Ordinal }).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_ItemMatrixColumn_Ordinal", "[Ordinal] BETWEEN 1 AND 3"));
    }
}

internal sealed class ItemVariantConfiguration : IEntityTypeConfiguration<ItemVariant>
{
    public void Configure(EntityTypeBuilder<ItemVariant> e)
    {
        e.Property(x => x.Value1).HasMaxLength(50);
        e.Property(x => x.Value2).HasMaxLength(50);
        e.Property(x => x.Value3).HasMaxLength(50);
        e.Property(x => x.Code).HasMaxLength(30);
        e.Property(x => x.OnHand).Quantity();
        e.HasOne<Item>().WithMany(x => x.Variants).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ItemId, x.Ordinal }).IsUnique();
        e.HasIndex(x => x.Code);   // matrix barcode scanning
    }
}

internal sealed class KitComponentConfiguration : IEntityTypeConfiguration<KitComponent>
{
    public void Configure(EntityTypeBuilder<KitComponent> e)
    {
        e.Property(x => x.ComponentPlu).HasMaxLength(30);
        e.Property(x => x.Description).HasMaxLength(50);
        e.Property(x => x.Quantity).Quantity();
        e.HasOne<Item>().WithMany(x => x.KitComponents).HasForeignKey(x => x.KitItemId).OnDelete(DeleteBehavior.Cascade);
        // A second path to Item: no cascade, or SQL Server rejects the schema (multiple cascade paths).
        e.HasOne(x => x.ComponentItem).WithMany().HasForeignKey(x => x.ComponentItemId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasIndex(x => new { x.KitItemId, x.Ordinal }).IsUnique();
    }
}

internal sealed class ItemSerialConfiguration : IEntityTypeConfiguration<ItemSerial>
{
    public void Configure(EntityTypeBuilder<ItemSerial> e)
    {
        e.Property(x => x.SerialNumber).HasMaxLength(50);
        e.Property(x => x.Status).HasMaxLength(30);
        e.HasOne<Item>().WithMany(x => x.Serials).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ItemId, x.SerialNumber });
    }
}

internal sealed class ItemPeriodSalesConfiguration : IEntityTypeConfiguration<ItemPeriodSales>
{
    public void Configure(EntityTypeBuilder<ItemPeriodSales> e)
    {
        e.Property(x => x.Kind).HasConversion<byte>();
        e.Property(x => x.Units).Quantity();
        e.HasOne<Item>().WithMany(x => x.PeriodSales).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ItemId, x.Year, x.Kind, x.Period }).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_ItemPeriodSales_Period",
            "([Kind] = 1 AND [Period] BETWEEN 1 AND 12) OR ([Kind] = 2 AND [Period] BETWEEN 1 AND 54)"));
    }
}
