using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPlus.Core.Model;
using RetailPlus.Data.Import;

namespace RetailPlus.Data;

public class RetailPlusDbContext(DbContextOptions<RetailPlusDbContext> options) : DbContext(options)
{
    /// <summary>Sequences for the document numbers the original kept in NUMBERS.DBF.</summary>
    public static class Sequences
    {
        public const string InvoiceNumbers = "InvoiceNumbers";
        public const string CustomerNumbers = "CustomerNumbers";
        public const string PurchaseOrderNumbers = "PurchaseOrderNumbers";
        public const string RmaNumbers = "RmaNumbers";
        public static readonly string[] All = [InvoiceNumbers, CustomerNumbers, PurchaseOrderNumbers, RmaNumbers];
    }

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemSupplier> ItemSuppliers => Set<ItemSupplier>();
    public DbSet<ItemMatrixColumn> ItemMatrixColumns => Set<ItemMatrixColumn>();
    public DbSet<ItemVariant> ItemVariants => Set<ItemVariant>();
    public DbSet<KitComponent> KitComponents => Set<KitComponent>();
    public DbSet<ItemSerial> ItemSerials => Set<ItemSerial>();
    public DbSet<ItemPeriodSales> ItemPeriodSales => Set<ItemPeriodSales>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<StoreSettingExtra> StoreSettingExtras => Set<StoreSettingExtra>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationSetting> StationSettings => Set<StationSetting>();
    public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleLineTag> SaleLineTags => Set<SaleLineTag>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<DrawerSession> DrawerSessions => Set<DrawerSession>();
    public DbSet<DrawerSessionTender> DrawerSessionTenders => Set<DrawerSessionTender>();
    public DbSet<DrawerMovement> DrawerMovements => Set<DrawerMovement>();
    public DbSet<SuspendedSale> SuspendedSales => Set<SuspendedSale>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<Rma> Rmas => Set<Rma>();
    public DbSet<TenderType> TenderTypes => Set<TenderType>();
    public DbSet<TaxConfiguration> TaxConfigurations => Set<TaxConfiguration>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<FastKey> FastKeys => Set<FastKey>();
    public DbSet<StaffPeriod> StaffPeriods => Set<StaffPeriod>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<RfidReader> RfidReaders => Set<RfidReader>();
    public DbSet<RfidAntenna> RfidAntennas => Set<RfidAntenna>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportSourceRow> ImportSourceRows => Set<ImportSourceRow>();

    /// <summary>Takes the next value of one of the <see cref="Sequences"/>.</summary>
    public async Task<long> NextNumberAsync(string sequence, CancellationToken cancellationToken = default)
    {
        if (Array.IndexOf(Sequences.All, sequence) < 0)
            throw new ArgumentException($"Unknown sequence {sequence}", nameof(sequence));
        // A plain command rather than SqlQueryRaw: EF would wrap the statement in a subquery, and SQL Server does
        // not allow NEXT VALUE FOR there. The name is checked against the fixed list above.
        await Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR [dbo].[" + sequence + "]";
            command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Amounts by default; costs, quantities and rates override this per property (see Precision).
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<DateTime>().HavePrecision(0);
        configurationBuilder.Properties<DateTime?>().HavePrecision(0);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation(RetailPlusDb.Collation);
        foreach (var name in Sequences.All)
            modelBuilder.HasSequence<long>(name, "dbo").StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RetailPlusDbContext).Assembly);
    }
}
