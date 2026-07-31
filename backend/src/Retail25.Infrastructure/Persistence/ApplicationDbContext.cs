using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
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

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// One context for the whole application, including ASP.NET Core Identity and OpenIddict.
/// <para>
/// Sharing a context matters here: issuing a token, granting a role and writing the audit row that
/// records both have to commit or roll back together. Two contexts would make it possible for a
/// grant to survive a failed sign-in, or for an authorisation to exist with no trail behind it.
/// </para>
/// </summary>
public class ApplicationDbContext
    : IdentityDbContext<Identity.ApplicationUser, Identity.ApplicationRole, Guid>, IApplicationDbContext
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
    public DbSet<SaleAdjustment> SaleAdjustments => Set<SaleAdjustment>();
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
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();

    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<CustomerOrderLine> CustomerOrderLines => Set<CustomerOrderLine>();
    public DbSet<Layaway> Layaways => Set<Layaway>();
    public DbSet<LayawayLine> LayawayLines => Set<LayawayLine>();
    public DbSet<LayawayPayment> LayawayPayments => Set<LayawayPayment>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();
    public DbSet<PriceQuoteLine> PriceQuoteLines => Set<PriceQuoteLine>();

    // --- Inventory ---
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<SalesHistoryArchive> SalesHistoryArchives => Set<SalesHistoryArchive>();

    // --- Legacy migration ---
    public DbSet<MigrationBatch> MigrationBatches => Set<MigrationBatch>();
    public DbSet<MigrationStagingRow> MigrationStagingRows => Set<MigrationStagingRow>();

    // --- Accounting sync ---
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<ExternalEntityMap> ExternalEntityMaps => Set<ExternalEntityMap>();

    // --- Terminals ---
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<DrawerSession> DrawerSessions => Set<DrawerSession>();
    public DbSet<DrawerLedgerEntry> DrawerLedgerEntries => Set<DrawerLedgerEntry>();
    public DbSet<PrinterProfile> PrinterProfiles => Set<PrinterProfile>();
    public DbSet<ReaderProfile> ReaderProfiles => Set<ReaderProfile>();
    public DbSet<ScaleProfile> ScaleProfiles => Set<ScaleProfile>();
    public DbSet<PoleDisplayProfile> PoleDisplayProfiles => Set<PoleDisplayProfile>();

    // --- Staff ---
    // --- Security & audit ---
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<SupervisorApproval> SupervisorApprovals => Set<SupervisorApproval>();

    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<TimeClockEntry> TimeClockEntries => Set<TimeClockEntry>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<CommissionLedgerEntry> CommissionLedgerEntries => Set<CommissionLedgerEntry>();

    // --- Configuration ---
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<TaxConfiguration> TaxConfigurations => Set<TaxConfiguration>();
    public DbSet<PosPolicy> PosPolicies => Set<PosPolicy>();
    public DbSet<PricingRuleSetting> PricingRuleSettings => Set<PricingRuleSetting>();
    public DbSet<TenderType> TenderTypes => Set<TenderType>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<LoyaltyPolicy> LoyaltyPolicies => Set<LoyaltyPolicy>();
    public DbSet<LateChargePolicy> LateChargePolicies => Set<LateChargePolicy>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // A convention rather than three per-property calls: the next entity that carries a rate is
        // mapped automatically instead of being the one somebody forgot.
        builder.Properties<Domain.ValueObjects.Percentage>()
            .HaveConversion<ValueObjectConverters.PercentageConverter>()
            .HavePrecision(9, 4);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // OpenIddict keeps its applications, authorizations, scopes and tokens in this context, so
        // token issuance participates in the same transaction as everything else.
        builder.UseOpenIddict<Guid>();
    }
}
