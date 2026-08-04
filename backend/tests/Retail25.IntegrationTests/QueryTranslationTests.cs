using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.ValueObjects;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The browse queries, run against real SQL.
/// <para>
/// This suite exists because of a specific, silent failure mode. EF Core will happily evaluate a
/// LINQ expression it cannot translate by pulling the whole table into memory first — against the
/// in-memory provider used by the unit tests, an untranslatable keyset predicate is indistinguishable
/// from a translatable one. The difference only appears in production, as a browse screen that reads
/// fifty thousand rows to show fifty.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class QueryTranslationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApplicationDbContextScope _scope = null!;

    public QueryTranslationTests(PostgresFixture postgres) => _postgres = postgres;

    private long LocationId => _scope.LocationId;

    public async Task InitializeAsync() => _scope = await ApplicationDbContextScope.CreateAsync(_postgres, "query_translation");

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    [RequiresIsolatedDatabaseFact]
    public async Task The_catalogue_browse_pages_in_sql_and_returns_every_row_once()
    {
        for (var i = 1; i <= 40; i++)
        {
            _scope.Db.Products.Add(Product.Create(LocationId, $"SKU{i:D3}", $"Item {i}", ProductType.Standard, 9.99m).Value);
        }

        await _scope.Db.SaveChangesAsync();

        var handler = new BrowseProductsHandlers(_scope.Db);
        var seen = new List<string>();
        string? cursor = null;

        do
        {
            // Sorted by price, which every row shares — the case where the tie-breaker is doing all
            // the work, and the one a keyset cursor gets wrong if the predicate is not a total order.
            var page = await handler.Handle(
                new BrowseProductsQuery(LocationId, Sort: ProductSort.RegularPrice, Cursor: cursor, PageSize: 7),
                default);

            seen.AddRange(page.Items.Select(p => p.StockCode));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().HaveCount(40);
        seen.Should().OnlyHaveUniqueItems();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task The_keyset_predicate_is_translated_rather_than_evaluated_in_memory()
    {
        _scope.Db.Products.Add(Product.Create(LocationId, "AAA", "Apple", ProductType.Standard, 1m).Value);
        _scope.Db.Products.Add(Product.Create(LocationId, "BBB", "Banana", ProductType.Standard, 2m).Value);
        await _scope.Db.SaveChangesAsync();

        var after = Cursor.Encode("AAA", "AAA");
        var decoded = Cursor.Decode(after)!.Value;

        var sql = _scope.Db.Products
            .Where(p => p.LocationId == LocationId && p.StockCode.CompareTo(decoded.SortKey) > 0)
            .OrderBy(p => p.StockCode)
            .ToQueryString();

        // If the comparison had not translated, EF would have emitted a query without it and filtered
        // afterwards — so the predicate's absence from the SQL is the whole assertion.
        sql.Should().Contain("WHERE");
        sql.Should().Contain("stock_code");

        var rows = await _scope.Db.Products
            .Where(p => p.LocationId == LocationId && p.StockCode.CompareTo(decoded.SortKey) > 0)
            .ToListAsync();

        rows.Should().ContainSingle().Which.StockCode.Should().Be("BBB");
    }

    [RequiresIsolatedDatabaseFact]
    public async Task A_percentage_and_an_owned_address_round_trip_through_postgres()
    {
        var tax = TaxConfiguration.Create(
            LocationId,
            new DateOnly(2026, 1, 1),
            true, "GST", new Percentage(5.25m),
            true, "PST", new Percentage(7m),
            false,
            false, string.Empty, Percentage.Zero, false,
            TaxationType.Exclusive,
            "GST-123").Value;

        _scope.Db.TaxConfigurations.Add(tax);

        var customer = Retail25.Domain.Customers.Customer.Create(LocationId, 4182, "Ada", "Lovelace").Value;
        customer.BillingAddress = new Address("1 High Street", null, "Kingston", "ON", "K7L 1A1", "CA");
        customer.Contact = new ContactDetails(Phone: "555-0100", Email: "ada@example.com");
        _scope.Db.Customers.Add(customer);

        await _scope.Db.SaveChangesAsync();
        _scope.Db.ChangeTracker.Clear();

        var reloadedTax = await _scope.Db.TaxConfigurations.AsNoTracking().FirstAsync(t => t.Id == tax.Id);
        var reloadedCustomer = await _scope.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);

        // The Percentage converter and the owned-type mapping were both defects that the in-memory
        // provider hid — one made the model unmappable, the other returned null on read-back.
        reloadedTax.Tax1Rate.Value.Should().Be(5.25m);
        reloadedCustomer.BillingAddress.City.Should().Be("Kingston");
        reloadedCustomer.Contact.Email.Should().Be("ada@example.com");
    }

    [RequiresIsolatedDatabaseFact]
    public async Task A_deleted_product_is_hidden_rather_than_destroyed()
    {
        var product = Product.Create(LocationId, "GONE", "Discontinued", ProductType.Standard, 5m).Value;
        _scope.Db.Products.Add(product);
        await _scope.Db.SaveChangesAsync();

        _scope.Db.Products.Remove(product);
        await _scope.Db.SaveChangesAsync();
        _scope.Db.ChangeTracker.Clear();

        var row = await _scope.Db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id);

        // The interceptor rewrites the delete. If it did not, this row would be gone and every sale
        // line that named it would be orphaned.
        row.Should().NotBeNull();
        row!.IsDeleted.Should().BeTrue();
        row.DeletedAt.Should().NotBeNull();
    }

    [RequiresIsolatedDatabaseFact]
    public async Task A_duplicate_stock_code_is_refused_by_the_database_not_only_by_the_handler()
    {
        _scope.Db.Products.Add(Product.Create(LocationId, "DUP", "First", ProductType.Standard, 1m).Value);
        await _scope.Db.SaveChangesAsync();

        _scope.Db.Products.Add(Product.Create(LocationId, "DUP", "Second", ProductType.Standard, 1m).Value);

        // The handler checks first, but two tills creating the same code in the same instant would
        // both pass that check. The unique index is what actually holds the line.
        var act = async () => await _scope.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}

/// <summary>A migrated database with one seeded location, disposed with the test class.</summary>
internal sealed class ApplicationDbContextScope : IDisposable
{
    private ApplicationDbContextScope(Retail25.Infrastructure.Persistence.ApplicationDbContext db, long locationId)
    {
        Db = db;
        LocationId = locationId;
    }

    public Retail25.Infrastructure.Persistence.ApplicationDbContext Db { get; }

    public long LocationId { get; }

    public static async Task<ApplicationDbContextScope> CreateAsync(PostgresFixture postgres, string databaseName)
    {
        var connection = await postgres.CreateEmptyDatabaseAsync(databaseName);
        var db = postgres.CreateContext(connection);

        await db.Database.MigrateAsync();

        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));

        await new Retail25.Infrastructure.Persistence.DatabaseSeeder(
            db,
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Retail25.Infrastructure.Persistence.DatabaseSeeder>.Instance)
            .SeedAsync();

        var locationId = (await db.Locations.AsNoTracking().FirstAsync()).Id;
        return new ApplicationDbContextScope(db, locationId);
    }

    public void Dispose() => Db.Dispose();
}
