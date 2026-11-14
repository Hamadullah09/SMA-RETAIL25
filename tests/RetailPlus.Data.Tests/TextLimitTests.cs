using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Text longer than its column is refused before it is saved, naming the field and its size, on SQLite (which would keep
/// it) as on SQL Server (which would refuse it without saying which field).
/// </summary>
public abstract class TextLimitTests(DatabaseFixture db)
{
    private void Require() => Skip.IfNot(db.Available, db.UnavailableReason);

    private static Supplier Supplier(string address1) =>
        new() { Company = "Len " + Guid.NewGuid().ToString("N")[..6], Address1 = address1, City = "Lahore" };

    [SkippableFact]
    public async Task Text_longer_than_its_column_is_refused_with_the_field_and_its_size()
    {
        Require();
        var parties = new PartyService(db.Data);
        var tooLong = Supplier(new string('A', 31));
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => parties.SaveSupplierAsync(tooLong));
        Assert.Equal("Address 1 is too long: at most 30 characters.", refused.Message);
        await using (var check = db.CreateContext())
            Assert.False(await check.Suppliers.AnyAsync(s => s.Company == tooLong.Company));

        // Exactly the size is fine, and so are spaces past it (SQL Server drops them).
        var full = await parties.SaveSupplierAsync(Supplier(new string('B', 30)));
        await parties.SaveSupplierAsync(Supplier(new string('C', 30) + "  "));

        // A change is checked as well.
        full.City = "A city name far too long";
        refused = await Assert.ThrowsAsync<BusinessRuleException>(() => parties.SaveSupplierAsync(full));
        Assert.Equal("City is too long: at most 18 characters.", refused.Message);
    }

    [SkippableFact]
    public async Task Only_the_columns_changed_are_checked_and_rows_from_a_sync_package_are_saved_as_they_arrived()
    {
        Require();
        var supplier = await new PartyService(db.Data).SaveSupplierAsync(Supplier("1 Mall Road"));
        string longText = new('D', 31);
        if (db.Connection.Provider == DatabaseProvider.Sqlite)
        {
            // A till may hold text saved before the check: changing another column of the row still works.
            await using (var ctx = db.CreateContext())
                await ctx.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Address1, longText));
            await using (var ctx = db.CreateContext())
            {
                var stored = await ctx.Suppliers.SingleAsync(s => s.Id == supplier.Id);
                stored.City = "Karachi";
                await ctx.SaveChangesAsync();
            }
        }

        // A package's rows are left to the database: SQLite keeps them, SQL Server refuses them itself.
        var arrived = Supplier(longText);
        await using var sync = db.CreateContext();
        sync.ApplyingSync = true;
        sync.Suppliers.Add(arrived);
        if (db.Connection.Provider == DatabaseProvider.Sqlite)
        {
            await sync.SaveChangesAsync();
            return;
        }
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => sync.SaveChangesAsync());
        Assert.Equal(Told(Assert.IsType<SqlException>(refused.InnerException)), TextLimits.FromSqlServer(refused, db.Data));
    }

    /// <summary>What the server says about Address 1 being too long: SQL Server 2019 and later name the column (2628), older ones do not (8152).</summary>
    private static string Told(SqlException ex) =>
        ex.Number == 2628 ? "Address 1 is too long: at most 30 characters." : "A text is longer than its field allows. Shorten it and save again.";

    [SkippableFact]
    public async Task SQL_Server_refusing_text_too_long_is_told_with_the_field()
    {
        Require();
        Skip.If(db.Connection.Provider != DatabaseProvider.SqlServer, "SQL Server only.");
        var supplier = await new PartyService(db.Data).SaveSupplierAsync(Supplier("2 Mall Road"));
        await using var ctx = db.CreateContext();
        // A bulk update is not checked before it runs.
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            ctx.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Address1, new string('E', 31))));
        Assert.Equal(Told(ex), TextLimits.FromSqlServer(ex, db.Data));
        Assert.Null(TextLimits.FromSqlServer(new InvalidOperationException("Something else")));
    }
}

public sealed class TextLimitTestsOnSqlite(SqliteFixture db) : TextLimitTests(db), IClassFixture<SqliteFixture>;

public sealed class TextLimitTestsOnSqlServer(SqlServerFixture db) : TextLimitTests(db), IClassFixture<SqlServerFixture>;

public class FieldNameTests
{
    [Theory]
    [InlineData("Address1", "Address 1")]
    [InlineData("CustomerEmail", "Customer email")]
    [InlineData("Plu", "Stock code")]
    [InlineData("ComponentPlu", "Component stock code")]
    [InlineData("WhatsAppNumber", "WhatsApp number")]
    [InlineData("Tax1Name", "Tax 1 name")]
    [InlineData("Iban", "IBAN")]
    [InlineData("TimeZoneId", "Time zone ID")]
    public void Property_names_are_written_as_people_read_them(string property, string expected) =>
        Assert.Equal(expected, TextLimits.FieldName(property));

    [Theory]
    [InlineData("abc", 3, false)]
    [InlineData("abcd", 3, true)]
    [InlineData("abc  ", 3, false)]
    [InlineData("ab  c", 3, true)]
    [InlineData("abc\t", 3, true)]
    public void Spaces_past_the_size_do_not_count(string text, int max, bool tooLong) =>
        Assert.Equal(tooLong, TextLimits.TooLong(text, max));
}
