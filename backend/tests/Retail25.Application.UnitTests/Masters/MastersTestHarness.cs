using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Application.Customers;
using Retail25.Application.Inventory;
using Retail25.Application.Loyalty;
using Retail25.Application.Orders;
using Retail25.Application.Purchasing;
using Retail25.Application.Receivables;
using Retail25.Application.Settings;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Purchasing;
using Retail25.Domain.Receivables;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Application.UnitTests.Masters;

/// <summary>
/// A store with a catalogue: one location, the seeded numbering and pricing ladder, and the handlers
/// the back-office screens use. Deliberately lighter than the POS harness — none of these tests need
/// a cart, a drawer or a reader.
/// </summary>
internal sealed class MastersTestHarness : IDisposable
{
    private MastersTestHarness(ApplicationDbContext db)
    {
        Db = db;
        Clock = new FixedClock(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));
        Notifier = Substitute.For<IPosNotifier>();
        Terminals = Substitute.For<ITerminalNotifier>();
        Sequences = new CountingSequenceGenerator();

        Browse = new BrowseProductsHandlers(db);
        Products = new ProductCommandHandlers(db, Notifier, Browse);
        Reference = new ReferenceDataHandlers(db, Notifier);
        CustomerBrowse = new CustomerBrowseHandlers(db);
        Customers = new CustomerCommandHandlers(db, Notifier, Sequences);
        Suppliers = new SupplierHandlers(db, Notifier, Sequences);
        CurrentUser = new TestCurrentUser();
        PurchaseOrders = new PurchaseOrderHandlers(db, Sequences, Notifier, Clock, CurrentUser);
        Inventory = new InventoryHandlers(db, Notifier, CurrentUser, Clock);
        Receivables = new ReceivablesHandlers(db, Clock);
        GiftCards = new GiftCardHandlers(db, Clock);
        Loyalty = new LoyaltyHandlers(db, Clock);
        CustomerOrders = new CustomerOrderHandlers(db, Sequences, Clock);
        Layaways = new LayawayHandlers(db, Sequences, Clock);
        PriceQuotes = new PriceQuoteHandlers(db, Sequences, Clock);
        Settings = new SettingsQueryHandler(db, Clock);
        SettingsCommands = new SettingsCommandHandlers(db, Notifier, Sequences, Clock);
        Hardware = new HardwareSettingsHandlers(db, Notifier, Terminals, Clock);
        Commerce = new CommerceSettingsHandlers(db, Notifier, Clock);
        RecycleBin = new RecycleBinHandler(db);
        RestoreReference = new RestoreReferenceRowHandler(db, Notifier);
    }

    public ApplicationDbContext Db { get; }

    public FixedClock Clock { get; }

    /// <summary>The clock's business date. <c>IDateTime.Today</c> is a default interface member, so it
    /// is not reachable through the concrete test clock.</summary>
    public DateOnly Today => ((IDateTime)Clock).Today();

    public IPosNotifier Notifier { get; }

    public ITerminalNotifier Terminals { get; }

    public CountingSequenceGenerator Sequences { get; }

    public BrowseProductsHandlers Browse { get; }

    public ProductCommandHandlers Products { get; }

    public ReferenceDataHandlers Reference { get; }

    public CustomerBrowseHandlers CustomerBrowse { get; }

    public CustomerCommandHandlers Customers { get; }

    public SupplierHandlers Suppliers { get; }

    public TestCurrentUser CurrentUser { get; }

    public PurchaseOrderHandlers PurchaseOrders { get; }

    public InventoryHandlers Inventory { get; }

    public ReceivablesHandlers Receivables { get; }

    public GiftCardHandlers GiftCards { get; }

    public LoyaltyHandlers Loyalty { get; }

    public CustomerOrderHandlers CustomerOrders { get; }

    public LayawayHandlers Layaways { get; }

    public PriceQuoteHandlers PriceQuotes { get; }

    public SettingsQueryHandler Settings { get; }

    public SettingsCommandHandlers SettingsCommands { get; }

    public HardwareSettingsHandlers Hardware { get; }

    public CommerceSettingsHandlers Commerce { get; }

    public RecycleBinHandler RecycleBin { get; }

    public RestoreReferenceRowHandler RestoreReference { get; }

    public Location Location { get; private set; } = null!;

    public static async Task<MastersTestHarness> CreateAsync()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));

        // The auditing interceptor is what turns a delete into a soft delete. Without it these tests
        // would prove that Remove() removes the row — which is true, and is not what production does.
        var interceptor = new AuditingInterceptor(new TestCurrentUser(), Substitute.For<IRequestContext>(), clock);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"masters-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        var harness = new MastersTestHarness(new ApplicationDbContext(options));
        await harness.SeedAsync();
        return harness;
    }

    private async Task SeedAsync()
    {
        Db.Currencies.Add(Currency.Create("CAD", "Canadian Dollar", "$", 2, RoundingMode.AwayFromZero, 0.01m, true).Value);

        Location = Location.Create("Test Store", "TST", "CAD", "UTC", TimeOnly.MinValue).Value;
        Db.Locations.Add(Location);

        Db.TaxConfigurations.Add(TaxConfiguration.Create(
            Location.Id,
            new DateOnly(2020, 1, 1),
            true, "GST", new Percentage(5m),
            true, "PST", new Percentage(7m),
            false,
            false, "Service", Percentage.Zero, false,
            TaxationType.Exclusive,
            null).Value);

        Db.PosPolicies.Add(PosPolicy.CreateDefault(Location.Id));
        Db.PricingRuleSettings.AddRange(PricingRuleSetting.SeedDefaults(Location.Id));
        Db.NumberSequences.AddRange(NumberSequence.SeedDefaults(Location.Id));

        await Db.SaveChangesAsync();
    }

    public async Task<Product> AddProductAsync(
        string stockCode,
        string name,
        decimal price = 10m,
        decimal onHand = 0m,
        ProductType type = ProductType.Standard,
        Guid? departmentId = null)
    {
        var product = Product.Create(Location.Id, stockCode, name, type, price).Value;
        product.UpdateStockLevels(onHand, 0m);

        if (departmentId is { } id)
        {
            product.SetDepartment(id);
        }

        Db.Products.Add(product);
        await Db.SaveChangesAsync();
        return product;
    }

    public async Task<Department> AddDepartmentAsync(string name)
    {
        var department = Department.Create(Location.Id, name).Value;
        Db.Departments.Add(department);
        await Db.SaveChangesAsync();
        return department;
    }

    public async Task<Supplier> AddSupplierAsync(string company, string number)
    {
        var supplier = Supplier.Create(Location.Id, company, number).Value;
        Db.Suppliers.Add(supplier);
        await Db.SaveChangesAsync();
        return supplier;
    }

    public async Task<ProductSupplier> AddProductSupplierAsync(
        Product product, Supplier supplier, int rank, decimal cost, decimal caseQty = 0m)
    {
        var link = ProductSupplier.Create(product.Id, supplier.Id, rank, cost).Value;
        link.Update(rank, cost, reorderNumber: null, caseQty, minimumOrderQty: 0m);
        Db.ProductSuppliers.Add(link);
        await Db.SaveChangesAsync();
        return link;
    }

    public async Task<TenderType> AddTenderAsync(string code, string name, TenderBehaviour behaviour)
    {
        var tender = TenderType.Create(code, name, behaviour, 10).Value;
        Db.TenderTypes.Add(tender);
        await Db.SaveChangesAsync();
        return tender;
    }

    public async Task<Station> AddStationAsync(string code)
    {
        var station = Station.Create(Location.Id, code).Value;
        Db.Stations.Add(station);
        await Db.SaveChangesAsync();
        return station;
    }

    public async Task<(Customer Customer, CustomerAccount Account)> AddCustomerWithAccountAsync(
        string firstName, string lastName, decimal creditLimit = 0m)
    {
        var customer = Customer.Create(Location.Id, DateTime.UtcNow.Ticks, firstName, lastName).Value;
        Db.Customers.Add(customer);

        var account = CustomerAccount.Create(customer.Id, DateTime.UtcNow.Ticks, creditLimit);
        Db.CustomerAccounts.Add(account);

        await Db.SaveChangesAsync();
        return (customer, account);
    }

    public async Task<Invoice> AddInvoiceAsync(
        Guid customerId, decimal invoiceTotal, DateOnly issuedOn, DateOnly dueOn, decimal penaltyAccrued = 0m)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = DateTime.UtcNow.Ticks,
            CustomerId = customerId,
            TransactionId = Guid.NewGuid(),
            IssuedOn = issuedOn,
            DueOn = dueOn,
            InvoiceTotal = invoiceTotal,
            BalanceDue = invoiceTotal,
            PenaltyAccrued = penaltyAccrued,
            Status = InvoiceStatus.Open,
            StaffId = Guid.NewGuid(),
            CreatedAt = Clock.Now,
        };

        Db.Invoices.Add(invoice);

        Db.ARLedgerEntries.Add(new ARLedgerEntry
        {
            CustomerId = customerId,
            InvoiceId = invoice.Id,
            EntryType = AREntryType.Charge,
            Amount = invoiceTotal,
            OccurredAt = Clock.Now,
        });

        // Mirrors production's ApplyOnAccountAsync: the charge that creates an invoice also raises
        // the account's running balance, not just the invoice's own.
        var account = await Db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == customerId);
        if (account is not null)
        {
            account.BalanceDue += invoiceTotal;
        }

        await Db.SaveChangesAsync();
        return invoice;
    }

    public static CustomerIdentitySection Person(string first, string last, string? company = null)
        => new(first, last, company, null, null, null, null);

    public static SupplierSection SupplierDetails(string company)
        => new(company, null, null, null, new Address(), new ContactDetails());

    public void Dispose() => Db.Dispose();
}
