using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using RetailPlus.Core.Model;
using RetailPlus.Data.Import;

namespace RetailPlus.Data;

public class RetailPlusDbContext(DbContextOptions<RetailPlusDbContext> options) : DbContext(options)
{
    /// <summary>The kinds of document number the original kept in NUMBERS.DBF; see <see cref="NumberCounter"/>.</summary>
    public static class Sequences
    {
        public const string InvoiceNumbers = "InvoiceNumbers";
        public const string CustomerNumbers = "CustomerNumbers";
        public const string PurchaseOrderNumbers = "PurchaseOrderNumbers";
        public const string RmaNumbers = "RmaNumbers";
        public const string StockCountNumbers = "StockCountNumbers";
        public const string TransferNumbers = "TransferNumbers";
        /// <summary>For gift certificates printed by the store, which have no number of their own.</summary>
        public const string GiftCardNumbers = "GiftCardNumbers";
        public static readonly string[] All = [InvoiceNumbers, CustomerNumbers, PurchaseOrderNumbers, RmaNumbers, StockCountNumbers, TransferNumbers, GiftCardNumbers];
    }

    /// <summary>
    /// The store and station this context works for. Rows it adds without a store or station get these, and document
    /// numbers come from this station's range. Null for tools that only read, or that set the store on every row.
    /// </summary>
    public StationIdentity? Station { get; set; }

    public DbSet<Store> Stores => Set<Store>();
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
    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();
    public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
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
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<RfidReader> RfidReaders => Set<RfidReader>();
    public DbSet<RfidAntenna> RfidAntennas => Set<RfidAntenna>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<ItemPicture> ItemPictures => Set<ItemPicture>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportSourceRow> ImportSourceRows => Set<ImportSourceRow>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<StockCountTag> StockCountTags => Set<StockCountTag>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<GiftCardTransaction> GiftCardTransactions => Set<GiftCardTransaction>();
    public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();
    public DbSet<AccountMapping> AccountMappings => Set<AccountMapping>();
    public DbSet<StoreImage> StoreImages => Set<StoreImage>();

    /// <summary>The settings row of this context's store (of the only store when no station is set).</summary>
    public IQueryable<StoreSettings> CurrentStoreSettings =>
        Station is { } station ? StoreSettings.Where(s => s.StoreId == station.StoreId) : StoreSettings;

    /// <summary>
    /// Takes the next number of one of the <see cref="Sequences"/> from this station's range. The counter is raised
    /// and read in one transaction (the caller's, or one of its own), so two windows never get the same number.
    /// </summary>
    public async Task<long> NextNumberAsync(string sequence, CancellationToken cancellationToken = default)
    {
        if (Array.IndexOf(Sequences.All, sequence) < 0)
            throw new ArgumentException($"Unknown sequence {sequence}", nameof(sequence));
        int station = Station is { StationNumber: > 0 } here ? here.StationNumber
            : throw new InvalidOperationException("Document numbers need the station this database works for.");
        var own = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var counter = NumberCounters.Where(c => c.Kind == sequence && c.StationNumber == station);
            var now = DateTime.UtcNow;
            if (await counter.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastValue, c => c.LastValue + 1).SetProperty(c => c.UpdatedAtUtc, now), cancellationToken) == 0)
            {
                // The station's first number of this kind. Inserted directly so nothing else pending in the context is saved.
                var id = Guid.CreateVersion7();
                long first = NumberCounter.FirstNumber(station);
                await Database.ExecuteSqlAsync(
                    $"""INSERT INTO "NumberCounters" ("Id", "Kind", "StationNumber", "LastValue", "CreatedAtUtc", "UpdatedAtUtc") VALUES ({id}, {sequence}, {station}, {first}, {now}, {now})""",
                    cancellationToken);
            }
            var value = await counter.Select(c => c.LastValue).SingleAsync(cancellationToken);
            if (own is not null) await own.CommitAsync(cancellationToken);
            return value;
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Times, versions, and the store and station of new rows.</summary>
    private void Stamp()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            bool added = entry.State == EntityState.Added;
            if (entry.Entity is ITracked tracked)
            {
                // Rows that arrive from another database keep the time they were made.
                if (added && tracked.CreatedAtUtc == default) tracked.CreatedAtUtc = now;
                else if (!added && tracked.CreatedAtUtc == default) entry.Property(nameof(ITracked.CreatedAtUtc)).IsModified = false;
                tracked.UpdatedAtUtc = now;
            }
            if (entry.Entity is IVersioned versioned) versioned.Version = Guid.NewGuid();
            if (!added) continue;
            if (entry.Entity is IStoreScoped scoped && scoped.StoreId == Guid.Empty)
                scoped.StoreId = Station?.StoreId
                    ?? throw new InvalidOperationException($"A new {entry.Metadata.ClrType.Name} needs a store, and this database session has none.");
            if (entry.Entity is IStationStamped stamped && stamped.StationNumber == 0 && Station is { StationNumber: > 0 } station)
                stamped.StationNumber = station.StationNumber;
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
        if (Database.IsSqlServer()) modelBuilder.UseCollation(RetailPlusDb.Collation);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RetailPlusDbContext).Assembly);

        foreach (var type in modelBuilder.Model.GetEntityTypes().Select(t => t.ClrType).ToList())
        {
            var entity = modelBuilder.Entity(type);
            if (typeof(Entity).IsAssignableFrom(type))
                entity.Property(nameof(Entity.Id)).HasValueGenerator<Version7GuidGenerator>().ValueGeneratedOnAdd();
            if (typeof(ITracked).IsAssignableFrom(type))
            {
                // Milliseconds, so changes in the same second can be told apart when synchronising.
                entity.Property(nameof(ITracked.CreatedAtUtc)).HasPrecision(3);
                entity.Property(nameof(ITracked.UpdatedAtUtc)).HasPrecision(3);
                entity.HasIndex(nameof(ITracked.UpdatedAtUtc));
            }
            if (typeof(IVersioned).IsAssignableFrom(type))
                entity.Property(nameof(IVersioned.Version)).IsConcurrencyToken();
            if (typeof(IStoreScoped).IsAssignableFrom(type) && type != typeof(Store))
                entity.HasOne(typeof(Store)).WithMany().HasForeignKey(nameof(IStoreScoped.StoreId)).OnDelete(DeleteBehavior.Restrict);
        }

        // SQLite compares text case-sensitively unless told otherwise; the original's index tags were upper case.
        if (Database.IsSqlite())
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetDeclaredProperties()).Where(p => p.ClrType == typeof(string)))
                property.SetCollation(RetailPlusDb.SqliteCollation);
    }
}

/// <summary>Version 7 GUIDs: unique without a central server, and in the order they were made.</summary>
internal sealed class Version7GuidGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
