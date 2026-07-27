using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Purchasing;
using Retail25.Domain.Receivables;
using Retail25.Domain.Sales;
using Retail25.Domain.Staff;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // --- Catalog ---
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<PriceBreak> PriceBreaks => Set<PriceBreak>();
    public DbSet<SalePricing> SalePricings => Set<SalePricing>();
    public DbSet<BonusPricing> BonusPricings => Set<BonusPricing>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MatrixDimension> MatrixDimensions => Set<MatrixDimension>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<KitComponent> KitComponents => Set<KitComponent>();
    public DbSet<SerializedUnit> SerializedUnits => Set<SerializedUnit>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();

    // --- Sales ---
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<CartAdjustment> CartAdjustments => Set<CartAdjustment>();
    public DbSet<CartTaxOverride> CartTaxOverrides => Set<CartTaxOverride>();
    public DbSet<SalesTransaction> SalesTransactions => Set<SalesTransaction>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleTender> SaleTenders => Set<SaleTender>();
    public DbSet<SaleTaxSnapshot> SaleTaxSnapshots => Set<SaleTaxSnapshot>();

    // --- Customers ---
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<CustomerPricingProfile> CustomerPricingProfiles => Set<CustomerPricingProfile>();
    public DbSet<LoyaltyLedgerEntry> LoyaltyLedgerEntries => Set<LoyaltyLedgerEntry>();

    // --- Purchasing ---
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderReceipt> PurchaseOrderReceipts => Set<PurchaseOrderReceipt>();

    // --- Receivables ---
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<ARLedgerEntry> ARLedgerEntries => Set<ARLedgerEntry>();
    public DbSet<GiftCertificate> GiftCertificates => Set<GiftCertificate>();

    // --- Inventory ---
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();

    // --- Terminals ---
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<DrawerSession> DrawerSessions => Set<DrawerSession>();
    public DbSet<DrawerLedgerEntry> DrawerLedgerEntries => Set<DrawerLedgerEntry>();
    public DbSet<PrinterProfile> PrinterProfiles => Set<PrinterProfile>();
    public DbSet<ReaderProfile> ReaderProfiles => Set<ReaderProfile>();

    // --- Staff ---
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<TimeClockEntry> TimeClockEntries => Set<TimeClockEntry>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();

    // --- Configuration ---
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<TaxConfiguration> TaxConfigurations => Set<TaxConfiguration>();
    public DbSet<PosPolicy> PosPolicies => Set<PosPolicy>();
    public DbSet<TenderType> TenderTypes => Set<TenderType>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<LoyaltyPolicy> LoyaltyPolicies => Set<LoyaltyPolicy>();
    public DbSet<LateChargePolicy> LateChargePolicies => Set<LateChargePolicy>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
