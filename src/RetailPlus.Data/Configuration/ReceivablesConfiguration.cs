using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> e)
    {
        e.Property(x => x.Kind).HasConversion<byte>();
        e.Property(x => x.AccountNumber).HasMaxLength(10);
        e.Property(x => x.CustomerNumber).HasMaxLength(11);
        e.Property(x => x.FirstName).HasMaxLength(12);
        e.Property(x => x.LastName).HasMaxLength(16);
        e.Property(x => x.Company).HasMaxLength(30);
        e.Property(x => x.SalesPerson).HasMaxLength(15);
        e.Property(x => x.DiscountPercent).Rate();
        e.Property(x => x.Tax1Rate).Rate();
        e.Property(x => x.Tax2Rate).Rate();
        e.Property(x => x.ShipTo).HasMaxLength(200);
        e.Property(x => x.BillTo).HasMaxLength(200);
        e.Property(x => x.Email).HasMaxLength(100);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.Ignore(x => x.DisplayName);
        e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.ClientSetNull);
        // Tags: INV_NO, ACCTNO, COMPANY, INVOICES (last name), INV_DATE (descending).
        e.HasIndex(x => new { x.Kind, x.InvoiceNumber }).IsUnique();
        e.HasIndex(x => x.AccountNumber);
        e.HasIndex(x => x.Company);
        e.HasIndex(x => x.LastName);
        e.HasIndex(x => x.InvoiceDate).IsDescending();
        e.HasIndex(x => x.ClientId);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(60);
        e.Property(x => x.Quantity).Quantity();
        e.HasOne<Invoice>().WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> e)
    {
        e.Property(x => x.TenderType).HasMaxLength(16);
        e.Property(x => x.Note).HasMaxLength(100);
        e.HasOne<Invoice>().WithMany(x => x.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Method).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(100);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Number).IsUnique();
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> e)
    {
        e.Property(x => x.Plu).HasMaxLength(16);
        e.Property(x => x.Description).HasMaxLength(30);
        e.Property(x => x.Size).HasMaxLength(6);
        e.Property(x => x.SupplierName).HasMaxLength(30);
        e.Property(x => x.ReorderNumber).HasMaxLength(16);
        e.Property(x => x.CaseQuantity).Quantity();
        e.Property(x => x.OrderQuantity).Quantity();
        e.Property(x => x.CostEach).UnitCost();
        e.Property(x => x.OnHandAtOrder).Quantity();
        e.Property(x => x.OnOrderAtOrder).Quantity();
        e.Property(x => x.BaseStock).Quantity();
        e.Property(x => x.ReorderPoint).Quantity();
        e.Property(x => x.ReorderQuantity).Quantity();
        e.Property(x => x.BackOrders).Quantity();
        e.Property(x => x.QuantityReceived).Quantity();
        e.HasOne<PurchaseOrder>().WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.PurchaseOrderId, x.Plu });
    }
}
