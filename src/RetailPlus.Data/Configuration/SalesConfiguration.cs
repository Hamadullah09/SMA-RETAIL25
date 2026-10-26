using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> e)
    {
        e.Property(x => x.Staff).HasMaxLength(15);
        e.Property(x => x.ClientName).HasMaxLength(60);
        e.Property(x => x.CustomerNumber).HasMaxLength(11);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Tax1Name).HasMaxLength(20);
        e.Property(x => x.Tax2Name).HasMaxLength(20);
        e.Property(x => x.Tax1Rate).Rate();
        e.Property(x => x.Tax2Rate).Rate();
        e.Property(x => x.VoidedBy).HasMaxLength(15);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasIndex(x => new { x.StationNumber, x.InvoiceNumber });
        e.HasIndex(x => x.InvoiceNumber);
        e.HasIndex(x => x.SoldAt);
        e.HasIndex(x => x.ClientId);
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.Department).HasMaxLength(15);
        e.Property(x => x.SerialNumber).HasMaxLength(50);
        e.Property(x => x.SupplierName).HasMaxLength(30);
        e.Property(x => x.Epc).HasMaxLength(128).IsUnicode(false);
        e.HasOne<Sale>().WithMany(x => x.Lines).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.ItemId);
        e.HasIndex(x => x.Plu);
    }
}

internal sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> e)
    {
        e.Property(x => x.TenderType).HasMaxLength(16);
        e.Property(x => x.AuthCode).HasMaxLength(40);
        e.Property(x => x.Reference).HasMaxLength(200);
        e.HasOne<Sale>().WithMany(x => x.Payments).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DrawerSessionConfiguration : IEntityTypeConfiguration<DrawerSession>
{
    public void Configure(EntityTypeBuilder<DrawerSession> e)
    {
        e.HasIndex(x => new { x.StationNumber, x.ClosedAt });
        e.HasIndex(x => x.OpenedAt);
    }
}

internal sealed class DrawerSessionTenderConfiguration : IEntityTypeConfiguration<DrawerSessionTender>
{
    public void Configure(EntityTypeBuilder<DrawerSessionTender> e)
    {
        e.Property(x => x.TenderType).HasMaxLength(16);
        e.HasOne<DrawerSession>().WithMany(x => x.Tenders).HasForeignKey(x => x.DrawerSessionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.DrawerSessionId, x.TenderType }).IsUnique();
    }
}

internal sealed class DrawerMovementConfiguration : IEntityTypeConfiguration<DrawerMovement>
{
    public void Configure(EntityTypeBuilder<DrawerMovement> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Reason).HasMaxLength(100);
        e.Property(x => x.User).HasMaxLength(15);
        e.HasOne<DrawerSession>().WithMany(x => x.Movements).HasForeignKey(x => x.DrawerSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SuspendedSaleConfiguration : IEntityTypeConfiguration<SuspendedSale>
{
    public void Configure(EntityTypeBuilder<SuspendedSale> e) => e.Property(x => x.Label).HasMaxLength(60);
}

internal sealed class CustomerOrderConfiguration : IEntityTypeConfiguration<CustomerOrder>
{
    public void Configure(EntityTypeBuilder<CustomerOrder> e)
    {
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.Company).HasMaxLength(30);
        e.Property(x => x.Phone).HasMaxLength(16);
        e.Property(x => x.SoldBy).HasMaxLength(15);
        e.Property(x => x.CustomerNumber).HasMaxLength(11);
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(37);
        e.Property(x => x.Size).HasMaxLength(6);
        e.Property(x => x.Quantity).Quantity();
        e.HasIndex(x => x.Company);
        e.HasIndex(x => x.OrderDate);
        e.HasIndex(x => x.Description);
    }
}

internal sealed class RmaConfiguration : IEntityTypeConfiguration<Rma>
{
    public void Configure(EntityTypeBuilder<Rma> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(30);
        e.Property(x => x.Size).HasMaxLength(6);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.FirstName).HasMaxLength(12);
        e.Property(x => x.LastName).HasMaxLength(16);
        e.Property(x => x.Company).HasMaxLength(30);
        e.Property(x => x.CustomerPhone).HasMaxLength(16);
        e.Property(x => x.CustomerEmail).HasMaxLength(100);
        e.Property(x => x.CustomerRmaNumber).HasMaxLength(10);
        e.Property(x => x.Supplier).HasMaxLength(30);
        e.Property(x => x.SupplierFirstName).HasMaxLength(12);
        e.Property(x => x.SupplierLastName).HasMaxLength(16);
        e.Property(x => x.SupplierPhone).HasMaxLength(16);
        e.Property(x => x.SupplierEmail).HasMaxLength(100);
        e.Property(x => x.SupplierRmaNumber).HasMaxLength(10);
        e.Property(x => x.SetType).HasMaxLength(16);
    }
}

internal sealed class TenderTypeConfiguration : IEntityTypeConfiguration<TenderType>
{
    public void Configure(EntityTypeBuilder<TenderType> e)
    {
        e.Property(x => x.Name).HasMaxLength(16);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class TaxConfigurationConfiguration : IEntityTypeConfiguration<TaxConfiguration>
{
    public void Configure(EntityTypeBuilder<TaxConfiguration> e)
    {
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.Tax1Name).HasMaxLength(20);
        e.Property(x => x.Tax2Name).HasMaxLength(20);
        e.Property(x => x.Tax1Rate).Rate();
        e.Property(x => x.Tax2Rate).Rate();
    }
}

internal sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> e)
    {
        e.Property(x => x.Currency).HasMaxLength(10);
        e.Property(x => x.Rate).HasPrecision(14, 6);
        e.HasIndex(x => x.Currency).IsUnique();
    }
}

internal sealed class FastKeyConfiguration : IEntityTypeConfiguration<FastKey>
{
    public void Configure(EntityTypeBuilder<FastKey> e)
    {
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Color).HasMaxLength(15);
        e.HasIndex(x => new { x.Panel, x.KeyNumber }).IsUnique();
    }
}

internal sealed class StaffPeriodConfiguration : IEntityTypeConfiguration<StaffPeriod>
{
    public void Configure(EntityTypeBuilder<StaffPeriod> e)
    {
        e.Property(x => x.UserName).HasMaxLength(15);
        e.Property(x => x.FromTime).HasMaxLength(5);
        e.Property(x => x.ToTime).HasMaxLength(5);
        e.Property(x => x.Hours).Quantity();
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> e)
    {
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.OnHandAfter).Quantity();
        e.Property(x => x.Reason).HasMaxLength(100);
        e.Property(x => x.User).HasMaxLength(15);
        e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ItemId, x.At });
    }
}
