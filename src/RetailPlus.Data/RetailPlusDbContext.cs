using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using RetailPlus.Core.Model;
using RetailPlus.Data.Import;

namespace RetailPlus.Data;

public partial class RetailPlusDbContext(DbContextOptions<RetailPlusDbContext> options) : DbContext(options)
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
        /// <summary>General ledger entries (numbered on the server, from its own range).</summary>
        public const string JournalNumbers = "JournalNumbers";
        /// <summary>Supplier bills and debit notes (server).</summary>
        public const string SupplierBillNumbers = "SupplierBillNumbers";
        /// <summary>Supplier payments (server).</summary>
        public const string SupplierPaymentNumbers = "SupplierPaymentNumbers";
        /// <summary>Expenses (server).</summary>
        public const string ExpenseNumbers = "ExpenseNumbers";
        /// <summary>Bank transfers and deposits (server).</summary>
        public const string BankNumbers = "BankNumbers";
        /// <summary>Payment runs (server).</summary>
        public const string PaymentRunNumbers = "PaymentRunNumbers";
        /// <summary>Payroll runs (numbered on the server, from its own range).</summary>
        public const string PayrollRunNumbers = "PayrollRunNumbers";
        /// <summary>Loans and advances to employees (numbered on the server).</summary>
        public const string LoanNumbers = "LoanNumbers";
        public static readonly string[] All = [InvoiceNumbers, CustomerNumbers, PurchaseOrderNumbers, RmaNumbers, StockCountNumbers, TransferNumbers, GiftCardNumbers, JournalNumbers,
            SupplierBillNumbers, SupplierPaymentNumbers, ExpenseNumbers, BankNumbers, PaymentRunNumbers,
            PayrollRunNumbers, LoanNumbers];
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
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<TimeClockEntry> TimeClockEntries => Set<TimeClockEntry>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
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
    public DbSet<Tombstone> Tombstones => Set<Tombstone>();
    public DbSet<SyncBatch> SyncBatches => Set<SyncBatch>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<StationLink> StationLinks => Set<StationLink>();
    public DbSet<RfidHubLink> RfidHubLinks => Set<RfidHubLink>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<LedgerSettings> LedgerSettings => Set<LedgerSettings>();
    public DbSet<FinanceSettings> FinanceSettings => Set<FinanceSettings>();
    public DbSet<TaxRuleSet> TaxRuleSets => Set<TaxRuleSet>();
    public DbSet<SalesTaxRate> SalesTaxRates => Set<SalesTaxRate>();
    public DbSet<WithholdingRule> WithholdingRules => Set<WithholdingRule>();
    public DbSet<StoreFinance> StoreFinances => Set<StoreFinance>();
    public DbSet<SupplierFinance> SupplierFinances => Set<SupplierFinance>();
    public DbSet<SupplierBill> SupplierBills => Set<SupplierBill>();
    public DbSet<SupplierBillLine> SupplierBillLines => Set<SupplierBillLine>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<PaymentRun> PaymentRuns => Set<PaymentRun>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<FinanceAttachment> FinanceAttachments => Set<FinanceAttachment>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();
    public DbSet<BankDeposit> BankDeposits => Set<BankDeposit>();
    public DbSet<BankStatementFormat> BankStatementFormats => Set<BankStatementFormat>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<FiscalInvoice> FiscalInvoices => Set<FiscalInvoice>();
    public DbSet<PayrollRuleSet> PayrollRuleSets => Set<PayrollRuleSet>();
    public DbSet<PayrollTaxSlab> PayrollTaxSlabs => Set<PayrollTaxSlab>();
    public DbSet<PayrollAllowanceRule> PayrollAllowanceRules => Set<PayrollAllowanceRule>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<HrDepartment> HrDepartments => Set<HrDepartment>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<RosterWeek> RosterWeeks => Set<RosterWeek>();
    public DbSet<RosterDay> RosterDays => Set<RosterDay>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveAdjustment> LeaveAdjustments => Set<LeaveAdjustment>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();
    public DbSet<EmployeeLoan> EmployeeLoans => Set<EmployeeLoan>();
    public DbSet<LoanInstalment> LoanInstalments => Set<LoanInstalment>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayslipLine> PayslipLines => Set<PayslipLine>();

    /// <summary>
    /// Set while rows from a sync package are saved: they keep their times, store, station and version exactly as they
    /// arrived, deletions leave no tombstone of their own (the package's tombstones are handled by the applier), and no
    /// audit lines are written (audit lines that arrive keep their own user, time and station).
    /// </summary>
    public bool ApplyingSync { get; set; }

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
            var now = SyncColumns.Now();
            if (await counter.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastValue, c => c.LastValue + 1).Edited(), cancellationToken) == 0)
            {
                // The station's first number of this kind. Inserted directly so nothing else pending in the context is saved.
                var id = Guid.CreateVersion7();
                long first = NumberCounter.FirstNumber(station);
                await Database.ExecuteSqlAsync(
                    $"""INSERT INTO "NumberCounters" ("Id", "Kind", "StationNumber", "LastValue", "CreatedAtUtc", "UpdatedAtUtc", "SyncEditedAtUtc") VALUES ({id}, {sequence}, {station}, {first}, {now}, {now}, {now})""",
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
        AuditChangesAsync(sync: true, default).AsTask().GetAwaiter().GetResult();
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await AuditChangesAsync(sync: false, cancellationToken);
        Stamp();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Times, versions, and the store and station of new rows; a <see cref="Tombstone"/> for every row deleted, so the
    /// deletion reaches the other databases.
    /// </summary>
    private void Stamp()
    {
        var now = SyncColumns.Now();
        var entries = ChangeTracker.Entries().ToList();
        if (!ApplyingSync)
            foreach (var entry in entries.Where(e => e.State == EntityState.Deleted).ToList())
                if (TombstoneFor(entry, now) is { } tombstone)
                    entries.Add(Tombstones.Add(tombstone));
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            bool added = entry.State == EntityState.Added;
            var edited = entry.Metadata.FindProperty(SyncColumns.EditedAtUtc) is null ? null : entry.Property(SyncColumns.EditedAtUtc);
            if (ApplyingSync)
            {
                // A row from a package is saved as it was in the database it came from.
                if (entry.Entity is ITracked arrived && added && arrived.CreatedAtUtc == default) arrived.CreatedAtUtc = now;
                if (edited is { CurrentValue: DateTime e } && e == default && entry.Entity is ITracked t) edited.CurrentValue = t.UpdatedAtUtc;
                continue;
            }
            if (entry.Entity is ITracked tracked)
            {
                // Rows that arrive from another database keep the time they were made.
                if (added && tracked.CreatedAtUtc == default) tracked.CreatedAtUtc = now;
                else if (!added && tracked.CreatedAtUtc == default) entry.Property(nameof(ITracked.CreatedAtUtc)).IsModified = false;
                tracked.UpdatedAtUtc = now;
            }
            if (edited is not null)
            {
                // Only a change to a column people edit makes the row newer than an edit made elsewhere.
                if (added || IsEdit(entry)) edited.CurrentValue = now;
                else if (edited.IsModified) edited.IsModified = false;
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

    /// <summary>True when a modified row has a changed column that people edit (not only counts the system keeps).</summary>
    private static bool IsEdit(EntityEntry entry) =>
        entry.Properties.Any(p => p.IsModified && !SyncColumns.IsBookkeeping(p.Metadata.Name) && !SyncColumns.IsKept(entry.Metadata.ClrType, p.Metadata.Name));

    /// <summary>The tombstone of a deleted row; none for local bookkeeping and rows without a single GUID key.</summary>
    private static Tombstone? TombstoneFor(EntityEntry entry, DateTime now)
    {
        if (entry.Entity is ILocalOnly || entry.Metadata.FindPrimaryKey() is not { Properties: [{ ClrType: var keyType } key] } || keyType != typeof(Guid))
            return null;
        Guid? store = entry.Entity is IStoreScoped scoped ? scoped.StoreId
            : entry.Metadata.FindProperty(nameof(IStoreScoped.StoreId)) is { } p && p.ClrType == typeof(Guid?) ? (Guid?)entry.Property(p.Name).OriginalValue : null;
        return new Tombstone
        {
            EntityName = entry.Metadata.ClrType.Name, RowId = (Guid)entry.Property(key.Name).OriginalValue!, DeletedAtUtc = now, StoreId = store,
        };
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
                entity.Property<DateTime>(SyncColumns.EditedAtUtc).HasPrecision(3);
                if (Database.IsSqlServer())
                {
                    // Raised by SQL Server on every insert and update, also by ExecuteUpdate and plain SQL; never written by EF.
                    var changeNumber = entity.Property<long>(SyncColumns.ChangeNumber).HasColumnType("rowversion")
                        .HasConversion(new NumberToBytesConverter<long>()).ValueGeneratedOnAddOrUpdate();
                    changeNumber.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
                    changeNumber.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                    entity.HasIndex(SyncColumns.ChangeNumber);
                }
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
