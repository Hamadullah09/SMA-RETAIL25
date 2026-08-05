using Microsoft.EntityFrameworkCore;
using Retail25.Domain.Accounting;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Migration;
using Retail25.Domain.Orders;
using Retail25.Domain.Purchasing;
using Retail25.Domain.Receivables;
using Retail25.Domain.Sales;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Abstractions;

/// <summary>
/// Application-layer view of the database. Infrastructure implements this; Application never
/// references EF Core types above the DbSet/SaveChanges boundary.
/// </summary>
public interface IApplicationDbContext
{
    // --- Catalog ---
    DbSet<Product> Products { get; }
    DbSet<ProductPrice> ProductPrices { get; }

    DbSet<ProductImage> ProductImages { get; }
    DbSet<PriceBreak> PriceBreaks { get; }
    DbSet<SalePricing> SalePricings { get; }
    DbSet<BonusPricing> BonusPricings { get; }
    DbSet<Department> Departments { get; }
    DbSet<Category> Categories { get; }
    DbSet<MatrixDimension> MatrixDimensions { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<KitComponent> KitComponents { get; }
    DbSet<SerializedUnit> SerializedUnits { get; }
    DbSet<ProductSupplier> ProductSuppliers { get; }

    // --- Sales ---
    DbSet<Cart> Carts { get; }
    DbSet<CartLine> CartLines { get; }
    DbSet<CartAdjustment> CartAdjustments { get; }
    DbSet<CartTaxOverride> CartTaxOverrides { get; }
    DbSet<SalesTransaction> SalesTransactions { get; }
    DbSet<SaleLine> SaleLines { get; }
    DbSet<SaleAdjustment> SaleAdjustments { get; }
    DbSet<SaleTender> SaleTenders { get; }
    DbSet<SaleTaxSnapshot> SaleTaxSnapshots { get; }

    // --- Customers ---
    DbSet<Customer> Customers { get; }
    DbSet<CustomerAccount> CustomerAccounts { get; }
    DbSet<CustomerPricingProfile> CustomerPricingProfiles { get; }
    DbSet<LoyaltyLedgerEntry> LoyaltyLedgerEntries { get; }

    // --- Purchasing ---
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<PurchaseOrderReceipt> PurchaseOrderReceipts { get; }

    // --- Receivables ---
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoicePayment> InvoicePayments { get; }
    DbSet<ARLedgerEntry> ARLedgerEntries { get; }
    DbSet<GiftCertificate> GiftCertificates { get; }
    DbSet<GiftCard> GiftCards { get; }

    // --- Orders ---
    DbSet<CustomerOrder> CustomerOrders { get; }
    DbSet<CustomerOrderLine> CustomerOrderLines { get; }
    DbSet<Layaway> Layaways { get; }
    DbSet<LayawayLine> LayawayLines { get; }
    DbSet<LayawayPayment> LayawayPayments { get; }
    DbSet<PriceQuote> PriceQuotes { get; }
    DbSet<PriceQuoteLine> PriceQuoteLines { get; }

    // --- Inventory ---
    DbSet<StockLevel> StockLevels { get; }
    DbSet<StockLedgerEntry> StockLedgerEntries { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountLine> StockCountLines { get; }
    DbSet<FiscalYear> FiscalYears { get; }
    DbSet<SalesHistoryArchive> SalesHistoryArchives { get; }

    // --- Legacy migration ---
    DbSet<MigrationBatch> MigrationBatches { get; }
    DbSet<MigrationStagingRow> MigrationStagingRows { get; }

    // --- Accounting sync ---
    DbSet<SyncLog> SyncLogs { get; }
    DbSet<ExternalEntityMap> ExternalEntityMaps { get; }

    // --- Terminals ---
    DbSet<Station> Stations { get; }
    DbSet<DrawerSession> DrawerSessions { get; }
    DbSet<DrawerLedgerEntry> DrawerLedgerEntries { get; }
    DbSet<PrinterProfile> PrinterProfiles { get; }
    DbSet<ReaderProfile> ReaderProfiles { get; }
    DbSet<ScaleProfile> ScaleProfiles { get; }
    DbSet<PoleDisplayProfile> PoleDisplayProfiles { get; }

    // --- Staff ---
    // --- Security & audit ---
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<SupervisorApproval> SupervisorApprovals { get; }

    DbSet<StaffProfile> StaffProfiles { get; }
    DbSet<TimeClockEntry> TimeClockEntries { get; }
    DbSet<CommissionRule> CommissionRules { get; }
    DbSet<CommissionLedgerEntry> CommissionLedgerEntries { get; }

    // --- Configuration ---
    DbSet<Location> Locations { get; }
    DbSet<BrandingAsset> BrandingAssets { get; }
    DbSet<BusinessProfile> BusinessProfiles { get; }
    DbSet<TaxConfiguration> TaxConfigurations { get; }
    DbSet<PosPolicy> PosPolicies { get; }
    DbSet<PricingRuleSetting> PricingRuleSettings { get; }
    DbSet<TenderType> TenderTypes { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<LoyaltyPolicy> LoyaltyPolicies { get; }
    DbSet<LateChargePolicy> LateChargePolicies { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
