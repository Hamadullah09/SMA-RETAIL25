using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class FinanceSettingsConfiguration : IEntityTypeConfiguration<FinanceSettings>
{
    public void Configure(EntityTypeBuilder<FinanceSettings> e) =>
        e.ToTable(t => t.HasCheckConstraint("CK_FinanceSettings_Hour", "[NightlyHour] BETWEEN 0 AND 23"));
}

internal sealed class TaxRuleSetConfiguration : IEntityTypeConfiguration<TaxRuleSet>
{
    public void Configure(EntityTypeBuilder<TaxRuleSet> e)
    {
        e.Property(x => x.Country).HasMaxLength(2);
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Notes).HasMaxLength(1000);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasMany(x => x.SalesTaxRates).WithOne().HasForeignKey(x => x.TaxRuleSetId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.WithholdingRules).WithOne().HasForeignKey(x => x.TaxRuleSetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesTaxRateConfiguration : IEntityTypeConfiguration<SalesTaxRate>
{
    public void Configure(EntityTypeBuilder<SalesTaxRate> e)
    {
        e.Property(x => x.Name).HasMaxLength(40);
        e.Property(x => x.Rate).Rate();
    }
}

internal sealed class WithholdingRuleConfiguration : IEntityTypeConfiguration<WithholdingRule>
{
    public void Configure(EntityTypeBuilder<WithholdingRule> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Section).HasMaxLength(40);
        e.Property(x => x.FilerRate).Rate();
        e.Property(x => x.NonFilerRate).Rate();
        e.Property(x => x.Threshold).HasPrecision(18, 2);
        e.HasIndex(x => new { x.TaxRuleSetId, x.Kind }).IsUnique();
    }
}

internal sealed class StoreFinanceConfiguration : IEntityTypeConfiguration<StoreFinance>
{
    public void Configure(EntityTypeBuilder<StoreFinance> e)
    {
        e.HasIndex(x => x.StoreId).IsUnique();
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<TaxRuleSet>().WithMany().HasForeignKey(x => x.TaxRuleSetId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.DepositBankAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SupplierFinanceConfiguration : IEntityTypeConfiguration<SupplierFinance>
{
    public void Configure(EntityTypeBuilder<SupplierFinance> e)
    {
        e.Property(x => x.TaxNumber).HasMaxLength(20);
        e.Property(x => x.SalesTaxNumber).HasMaxLength(20);
        e.Property(x => x.WithholdingKind).HasConversion<string>().HasMaxLength(10);
        e.HasIndex(x => x.SupplierId).IsUnique();
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.DefaultAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SupplierBillConfiguration : IEntityTypeConfiguration<SupplierBill>
{
    public void Configure(EntityTypeBuilder<SupplierBill> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.SupplierReference).HasMaxLength(40);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.Property(x => x.Subtotal).HasPrecision(18, 2);
        e.Property(x => x.TaxTotal).HasPrecision(18, 2);
        e.Property(x => x.Total).HasPrecision(18, 2);
        e.Property(x => x.Paid).HasPrecision(18, 2);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.PostedBy).HasMaxLength(50);
        e.Property(x => x.VoidReason).HasMaxLength(200);
        e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<SupplierBill>().WithMany().HasForeignKey(x => x.OriginalBillId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.SupplierId, x.Status });
        e.HasIndex(x => new { x.StoreId, x.DueDate });
    }
}

internal sealed class SupplierBillLineConfiguration : IEntityTypeConfiguration<SupplierBillLine>
{
    public void Configure(EntityTypeBuilder<SupplierBillLine> e)
    {
        e.Property(x => x.Description).HasMaxLength(200);
        e.Property(x => x.Quantity).Quantity();
        e.Property(x => x.UnitPrice).HasPrecision(18, 3);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.TaxRate).Rate();
        e.Property(x => x.TaxAmount).HasPrecision(18, 2);
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        // Rows the tills own (items, purchase orders and their lines) may be deleted there; the bill line keeps its amounts.
        e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.SetNull);
        // ClientSetNull: SQL Server allows one cascading path from an order (its lines already cascade to here).
        e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.ClientSetNull);
        e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.PurchaseOrderLineId);
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> e)
    {
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.WithholdingAmount).HasPrecision(18, 2);
        e.Property(x => x.WithholdingKind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.WithholdingRate).Rate();
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<PaymentRun>().WithMany().HasForeignKey(x => x.PaymentRunId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.SupplierPaymentId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.SupplierId, x.Date });
    }
}

internal sealed class SupplierPaymentAllocationConfiguration : IEntityTypeConfiguration<SupplierPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentAllocation> e)
    {
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.HasOne<SupplierBill>().WithMany().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentRunConfiguration : IEntityTypeConfiguration<PaymentRun>
{
    public void Configure(EntityTypeBuilder<PaymentRun> e)
    {
        e.Property(x => x.Total).HasPrecision(18, 2);
        e.Property(x => x.Notes).HasMaxLength(200);
        e.Property(x => x.PaidBy).HasMaxLength(50);
        e.Property(x => x.PaidAtUtc).HasPrecision(3);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Number);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> e)
    {
        e.Property(x => x.Payee).HasMaxLength(100);
        e.Property(x => x.Description).HasMaxLength(200);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.TaxAmount).HasPrecision(18, 2);
        e.Property(x => x.PaidBy).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<SupplierBill>().WithMany().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<RecurringExpense>().WithMany().HasForeignKey(x => x.RecurringExpenseId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.StoreId, x.Date });
    }
}

internal sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Payee).HasMaxLength(100);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.TaxAmount).HasPrecision(18, 2);
        e.Property(x => x.PaidBy).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(10);
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.IsActive, x.NextDate });
    }
}

internal sealed class FinanceAttachmentConfiguration : IEntityTypeConfiguration<FinanceAttachment>
{
    public void Configure(EntityTypeBuilder<FinanceAttachment> e)
    {
        e.Property(x => x.OwnerKind).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.ContentType).HasMaxLength(100);
        e.Property(x => x.UploadedBy).HasMaxLength(50);
        e.HasIndex(x => new { x.OwnerKind, x.OwnerId });
    }
}

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.BankName).HasMaxLength(60);
        e.Property(x => x.AccountNumber).HasMaxLength(40);
        e.Property(x => x.Iban).HasMaxLength(34);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        e.HasOne(x => x.LedgerAccount).WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasIndex(x => x.LedgerAccountId).IsUnique();
    }
}

internal sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> e)
    {
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.ToAmount).HasPrecision(18, 2);
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.Memo).HasMaxLength(200);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.FromAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Number);
    }
}

internal sealed class BankDepositConfiguration : IEntityTypeConfiguration<BankDeposit>
{
    public void Configure(EntityTypeBuilder<BankDeposit> e)
    {
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.DrawerSessionId).IsUnique().HasFilter("[DrawerSessionId] IS NOT NULL");
        e.HasIndex(x => x.Number);
    }
}

internal sealed class BankStatementFormatConfiguration : IEntityTypeConfiguration<BankStatementFormat>
{
    public void Configure(EntityTypeBuilder<BankStatementFormat> e)
    {
        e.Property(x => x.BankName).HasMaxLength(60);
        e.Property(x => x.Delimiter).HasMaxLength(2);
        e.Property(x => x.DateColumn).HasMaxLength(60);
        e.Property(x => x.DateFormat).HasMaxLength(30);
        e.Property(x => x.DescriptionColumn).HasMaxLength(60);
        e.Property(x => x.ReferenceColumn).HasMaxLength(60);
        e.Property(x => x.AmountColumn).HasMaxLength(60);
        e.Property(x => x.DebitColumn).HasMaxLength(60);
        e.Property(x => x.CreditColumn).HasMaxLength(60);
        e.Property(x => x.BalanceColumn).HasMaxLength(60);
        e.HasIndex(x => x.BankName).IsUnique();
    }
}

internal sealed class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
{
    public void Configure(EntityTypeBuilder<BankStatement> e)
    {
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.ClosingBalance).HasPrecision(18, 2);
        e.Property(x => x.ImportedBy).HasMaxLength(50);
        e.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BankStatementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> e)
    {
        e.Property(x => x.Description).HasMaxLength(200);
        e.Property(x => x.Reference).HasMaxLength(60);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Balance).HasPrecision(18, 2);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.MatchedBy).HasMaxLength(50);
        e.HasOne<JournalLine>().WithMany().HasForeignKey(x => x.JournalLineId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.JournalLineId).IsUnique().HasFilter("[JournalLineId] IS NOT NULL");
        e.HasIndex(x => new { x.BankAccountId, x.Status, x.Date });
    }
}

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> e)
    {
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.FiscalYearId, x.AccountId, x.StoreId, x.Month }).IsUnique();
    }
}

internal sealed class FiscalInvoiceConfiguration : IEntityTypeConfiguration<FiscalInvoice>
{
    public void Configure(EntityTypeBuilder<FiscalInvoice> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.LastError).HasMaxLength(400);
        e.Property(x => x.FiscalNumber).HasMaxLength(60);
        e.Property(x => x.NextAttemptUtc).HasPrecision(3);
        e.Property(x => x.SentAtUtc).HasPrecision(3);
        e.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.SaleId).IsUnique();
        e.HasIndex(x => new { x.Status, x.NextAttemptUtc });
    }
}

/// <summary>The sale's fiscal invoice number (FBR), configured here next to the queue that fills it.</summary>
internal sealed class SaleFiscalConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> e) => e.Property(x => x.FiscalInvoiceNumber).HasMaxLength(60);
}
