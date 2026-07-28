using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Persistence.Converters;
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

/// <summary>
/// The single database context. Identity is generic over <see cref="Guid"/> keys so user
/// identifiers match every other key in the system — a string-keyed user would be the one exception
/// and would show up as a cast in every audit column.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
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
    public DbSet<PriceLevelDefinition> PriceLevelDefinitions => Set<PriceLevelDefinition>();

    /// <summary>
    /// Model-wide rules, applied before any per-entity configuration.
    /// <para>
    /// Setting decimal precision here rather than on each property is deliberate: PostgreSQL
    /// defaults an unqualified <c>decimal</c> to unlimited precision, and a money column that
    /// silently accepts twenty decimal places is a rounding bug waiting to be discovered at a till.
    /// </para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Ledger precision: four decimal places, matching Money.StorageScale, so fractional-cent
        // unit costs survive and totals are rounded once at the presentation boundary.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        configurationBuilder.Properties<Percentage>()
            .HaveConversion<PercentageConverter>()
            .HavePrecision(9, 4);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        IgnoreDomainEvents(builder);
    }

    /// <summary>
    /// Domain events are dispatched in memory after a transaction commits; they are never stored.
    /// Ignoring them centrally means adding a new aggregate does not require remembering to exclude
    /// its event collection — a mistake that surfaces as a baffling migration rather than a
    /// compile error.
    /// </summary>
    private static void IgnoreDomainEvents(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Ignore(nameof(AggregateRoot.DomainEvents));
            }
        }
    }
}
