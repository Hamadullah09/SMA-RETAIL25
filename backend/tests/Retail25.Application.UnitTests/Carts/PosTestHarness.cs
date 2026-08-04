using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Retail25.Application.Rfid;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Application.UnitTests.Carts;

/// <summary>
/// A store in a box: one location, one station, a currency, taxes, the seeded pricing ladder and an
/// in-memory cart store.
/// <para>
/// The cart store is a dictionary rather than Redis because these tests are about handler behaviour,
/// not about serialization. Redis round-tripping is covered where it belongs, and mixing the two
/// would make every handler test need a container.
/// </para>
/// </summary>
internal sealed class PosTestHarness : IDisposable
{
    private PosTestHarness(ApplicationDbContext db)
    {
        Db = db;
        Clock = new FixedClock(new DateTimeOffset(2026, 7, 28, 14, 30, 0, TimeSpan.Zero));
        CartStore = new InMemoryCartStore();
        Notifier = Substitute.For<IPosNotifier>();
        TerminalNotifier = Substitute.For<ITerminalNotifier>();
        Debouncer = new InMemoryTagDebouncer();
        CurrentUser = new TestCurrentUser();

        ContextLoader = new PosContextLoader(db, Clock);
        Pricing = new CartPricingService(db);
        Workflow = new CartWorkflow(CartStore, ContextLoader, Pricing, Notifier, Clock);
        Resolver = new IdentifierResolver(db);
        LineFactory = new CartLineFactory(db, CurrentUser, Debouncer, Notifier);

        RfidNotifier = Substitute.For<IRfidNotifier>();
        TagRegistry = new TagStreamRegistry();
        TagFeed = new TagObservationPublisher(
            TagRegistry,
            RfidNotifier,
            db,
            NullLogger<TagObservationPublisher>.Instance);
    }

    public ApplicationDbContext Db { get; }

    public FixedClock Clock { get; }

    public InMemoryCartStore CartStore { get; }

    public IPosNotifier Notifier { get; }

    public ITerminalNotifier TerminalNotifier { get; }

    public InMemoryTagDebouncer Debouncer { get; }

    public TestCurrentUser CurrentUser { get; }

    public PosContextLoader ContextLoader { get; }

    public CartPricingService Pricing { get; }

    public CartWorkflow Workflow { get; }

    public IdentifierResolver Resolver { get; }

    public CartLineFactory LineFactory { get; }

    public IRfidNotifier RfidNotifier { get; }

    /// <summary>
    /// Fresh per harness, so one test's debounce window cannot suppress the next test's reads — the
    /// registry is a singleton in production precisely because it remembers.
    /// </summary>
    public TagStreamRegistry TagRegistry { get; }

    public TagObservationPublisher TagFeed { get; }

    /// <summary>
    /// Routes the one command that dispatches to another. Substituting MediatR wholesale would hide
    /// which command actually ran, so only the forwarding path is stubbed.
    /// </summary>
    public MediatR.ISender Sender { get; private set; } = null!;

    public Location Location { get; private set; } = null!;

    public Station Station { get; private set; } = null!;

    public Currency Currency { get; private set; } = null!;

    public static async Task<PosTestHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pos-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var harness = new PosTestHarness(new ApplicationDbContext(options));
        await harness.SeedAsync();
        harness.Sender = Substitute.For<MediatR.ISender>();
        return harness;
    }

    private async Task SeedAsync()
    {
        Currency = Currency.Create("CAD", "Canadian Dollar", "$", 2, RoundingMode.AwayFromZero, 0.01m, true).Value;
        Db.Currencies.Add(Currency);

        Location = Location.Create("Test Store", "TST", "CAD", "UTC", TimeOnly.MinValue).Value;
        Db.Locations.Add(Location);

        Station = Station.Create(Location.Id, "001", "Front counter").Value;
        Db.Stations.Add(Station);

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

        await Db.SaveChangesAsync();
    }

    public async Task<Product> AddProductAsync(
        string stockCode,
        string name,
        decimal price,
        ProductType type = ProductType.Standard,
        string? upc = null,
        bool tax1 = true,
        bool tax2 = true)
    {
        var product = Product.Create(Location.Id, stockCode, name, type, price, tax1, tax2).Value;

        if (upc is not null)
        {
            product.UpdateDetails(name, null, upc, null, null);
        }

        Db.Products.Add(product);
        await Db.SaveChangesAsync();
        return product;
    }

    public async Task<SerializedUnit> AddTaggedUnitAsync(Product product, string epc)
    {
        var unit = SerializedUnit.Create(product.Id, Location.Id, null, epc, Clock.Now).Value;
        unit.Commission();
        Db.SerializedUnits.Add(unit);
        await Db.SaveChangesAsync();
        return unit;
    }

    /// <summary>
    /// A reader profile with real thresholds, so the anti-false-positive controls are actually
    /// exercised. Without one the handler falls back to a permissive default and every stray read
    /// would be accepted.
    /// </summary>
    public async Task<ReaderProfile> AddReaderProfileAsync(
        string antennaZones = "1=Checkout;2=Checkout;9=Exit",
        int rssiThresholdDbm = -70,
        int minimumReadCount = 2)
    {
        var profile = ReaderProfile.CreateDefault(Location.Id);
        profile.StationId = Station.Id;
        profile.AntennaZones = antennaZones;
        profile.RssiThresholdDbm = rssiThresholdDbm;
        profile.MinimumReadCount = minimumReadCount;

        Db.ReaderProfiles.Add(profile);
        await Db.SaveChangesAsync();
        return profile;
    }

    public async Task<TenderType> AddTenderAsync(string code, string name, TenderBehaviour behaviour)
    {
        var tender = TenderType.Create(code, name, behaviour, 10).Value;
        Db.TenderTypes.Add(tender);
        await Db.SaveChangesAsync();
        return tender;
    }

    /// <summary>
    /// Puts a staff profile behind the signed-in user and returns it. Access level 0 is the trainee
    /// preset — which is what makes everything they ring practice rather than real.
    /// </summary>
    public async Task<Domain.Staff.StaffProfile> SignInAsAsync(string code, int accessLevel)
    {
        var staff = Domain.Staff.StaffProfile.Create(TestIds.Next(), code, code, "Tester", accessLevel);
        Db.StaffProfiles.Add(staff);
        await Db.SaveChangesAsync();

        CurrentUser.StaffId = staff.Id;
        return staff;
    }

    public async Task<Domain.Staff.CommissionRule> AddCommissionRuleAsync(
        long staffId,
        Domain.Staff.CommissionType type,
        decimal value,
        long? productId = null,
        long? departmentId = null,
        decimal? max = null)
    {
        var rule = Domain.Staff.CommissionRule.Create(staffId, type, value, productId, departmentId, max).Value;
        Db.CommissionRules.Add(rule);
        await Db.SaveChangesAsync();
        return rule;
    }

    public async Task<Cart> OpenCartAsync()
    {
        var cart = Cart.Open(Station.Id, Location.Id, CurrentUser.StaffId ?? TestIds.Next(), Clock.Now, 720);
        await CartStore.SaveAsync(new CartSnapshot(cart));
        return cart;
    }

    public void Dispose() => Db.Dispose();
}

internal sealed class FixedClock : IDateTime
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    /// <summary>Moves the clock on, for anything whose behaviour depends on time passing.</summary>
    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

/// <summary>Grants everything by default; individual tests take permissions away to test the gates.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _permissions = new(PermissionKeys.All, StringComparer.Ordinal);

    public long? UserId { get; set; } = TestIds.Next();

    public long? StaffId { get; set; } = TestIds.Next();

    public long? StationId { get; set; }

    public long? LocationId { get; set; }

    public bool IsAuthenticated => true;

    public IReadOnlySet<string> Permissions => _permissions;

    public void Revoke(string permission) => _permissions.Remove(permission);
}

/// <summary>
/// A counter standing in for the Postgres sequence. The production generator needs a real database;
/// what the tests care about is that every caller gets a distinct, ascending number per kind.
/// </summary>
internal sealed class CountingSequenceGenerator : ISequenceGenerator
{
    private readonly Dictionary<SequenceKind, long> _counters = [];
    private long _transaction;
    private long _invoice;

    public Task<long> NextTransactionNumberAsync(long locationId, CancellationToken ct = default)
        => Task.FromResult(Interlocked.Increment(ref _transaction));

    public Task<long> NextInvoiceNumberAsync(long locationId, CancellationToken ct = default)
        => Task.FromResult(Interlocked.Increment(ref _invoice));

    public Task<long> NextAsync(SequenceKind kind, long locationId, CancellationToken ct = default)
    {
        var next = _counters.GetValueOrDefault(kind) + 1;
        _counters[kind] = next;
        return Task.FromResult(next);
    }

    public Task RestartAsync(SequenceKind kind, long locationId, long nextNumber, CancellationToken ct = default)
    {
        _counters[kind] = nextNumber - 1;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCartStore : ICartStore
{
    private readonly Dictionary<long, CartSnapshot> _carts = [];
    private readonly Dictionary<long, long> _stationCarts = [];

    public Task<CartSnapshot?> GetAsync(long cartId, CancellationToken ct = default)
        => Task.FromResult(_carts.GetValueOrDefault(cartId));

    public Task<CartSnapshot?> GetByStationAsync(long stationId, CancellationToken ct = default)
        => Task.FromResult(_stationCarts.TryGetValue(stationId, out var cartId) ? _carts.GetValueOrDefault(cartId) : null);

    public Task SaveAsync(CartSnapshot snapshot, CancellationToken ct = default)
    {
        _carts[snapshot.Cart.Id] = snapshot;

        if (snapshot.Cart.IsActive)
        {
            _stationCarts[snapshot.Cart.StationId] = snapshot.Cart.Id;
        }
        else
        {
            _stationCarts.Remove(snapshot.Cart.StationId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(long cartId, long stationId, CancellationToken ct = default)
    {
        _carts.Remove(cartId);
        _stationCarts.Remove(stationId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTagDebouncer : ITagDebouncer
{
    private readonly Dictionary<string, long> _claims = new(StringComparer.Ordinal);

    public Task<bool> TryClaimAsync(string epc, long stationId, TimeSpan window, CancellationToken ct = default)
    {
        var key = epc.ToUpperInvariant();

        if (_claims.TryGetValue(key, out var holder) && holder != stationId)
        {
            return Task.FromResult(false);
        }

        _claims[key] = stationId;
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string epc, long stationId, CancellationToken ct = default)
    {
        var key = epc.ToUpperInvariant();

        if (_claims.TryGetValue(key, out var holder) && holder == stationId)
        {
            _claims.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<long?> GetHolderAsync(string epc, CancellationToken ct = default)
        => Task.FromResult(_claims.TryGetValue(epc.ToUpperInvariant(), out var holder) ? holder : (long?)null);
}
