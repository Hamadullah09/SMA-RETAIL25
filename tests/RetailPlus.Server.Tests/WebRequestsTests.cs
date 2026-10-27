using System.Net;
using System.Text.Json;
using static RetailPlus.Server.Tests.BackOffice;

namespace RetailPlus.Server.Tests;

/// <summary>
/// What the web app asked for after it was built against the contract: the currency symbol for every role, a staff list for
/// filters, whether the user may change shared records, a store's locations, and UTC times the browser reads as UTC.
/// </summary>
public sealed class WebRequestsTests(BackOffice office) : IClassFixture<BackOffice>
{
    private void Require() => Skip.IfNot(office.Available, office.UnavailableReason);

    private sealed record Me(string UserName, bool CanChangeShared);
    private sealed record Staff(Guid Id, string UserName, string? FullName);
    private sealed record Place(Guid Id, string Code, string? Name);

    [SkippableFact]
    public async Task Stores_carry_their_currency_symbol_for_every_role()
    {
        Require();
        using var super = await office.SignInAsync();
        var settings = await GetAsync<JsonElement>(super, $"/api/stores/{office.StoreA.Id}/settings");
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(settings.GetRawText())!;
        body["currencySymbol"] = "Rs";
        using (var put = await super.PutAsync($"/api/stores/{office.StoreA.Id}/settings", System.Net.Http.Json.JsonContent.Create(body)))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var viewer = await office.SignInAsync(office.ViewerA);
        var stores = await GetAsync<List<StoreInfo>>(viewer, "/api/stores");
        Assert.Equal("Rs", Assert.Single(stores).CurrencySymbol);
    }

    [SkippableFact]
    public async Task The_staff_list_is_for_reports_and_covers_the_users_store()
    {
        Require();
        using var reporter = await office.SignInAsync(office.ReporterA);
        var staff = await GetAsync<List<Staff>>(reporter, "/api/staff");
        Assert.Contains(staff, s => s.UserName == office.CashierA.UserName);
        Assert.DoesNotContain(staff, s => s.UserName == office.CashierB.UserName);

        using var super = await office.SignInAsync();
        var everyone = await GetAsync<List<Staff>>(super, "/api/staff");
        Assert.Contains(everyone, s => s.UserName == office.CashierB.UserName);
        Assert.Equal(everyone.Select(s => s.UserName).Order(StringComparer.OrdinalIgnoreCase), everyone.Select(s => s.UserName), StringComparer.OrdinalIgnoreCase);

        using var viewer = await office.SignInAsync(office.ViewerA);
        using var refused = await viewer.GetAsync("/api/staff");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [SkippableFact]
    public async Task Only_users_who_see_every_store_may_change_shared_records_when_there_are_several()
    {
        Require();
        using var super = await office.SignInAsync();
        Assert.True((await GetAsync<Me>(super, "/api/auth/me")).CanChangeShared);
        using var adminA = await office.SignInAsync(office.AdminA);
        Assert.False((await GetAsync<Me>(adminA, "/api/auth/me")).CanChangeShared);
    }

    [SkippableFact]
    public async Task A_stores_locations_are_listed_to_those_who_see_it()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        var places = await GetAsync<List<Place>>(adminA, $"/api/stores/{office.StoreA.Id}/locations");
        Assert.Contains(places, p => p.Code == "POS");
        using var other = await adminA.GetAsync($"/api/stores/{office.StoreB.Id}/locations");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [SkippableFact]
    public async Task Utc_times_end_with_z_and_the_tills_local_times_do_not()
    {
        Require();
        using var super = await office.SignInAsync();
        using var tills = JsonDocument.Parse(await super.GetStringAsync("/api/tills"));
        var till = tills.RootElement.EnumerateArray().First(t => t.GetProperty("lastUploadAtUtc").ValueKind == JsonValueKind.String);
        Assert.EndsWith("Z", till.GetProperty("lastUploadAtUtc").GetString(), StringComparison.Ordinal);

        var today = office.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using var sales = JsonDocument.Parse(await super.GetStringAsync($"/api/sales?from={office.Today.AddDays(-2):yyyy-MM-dd}&to={today}"));
        var at = sales.RootElement.GetProperty("items").EnumerateArray().First().GetProperty("at").GetString()!;
        Assert.DoesNotContain("Z", at, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task The_audit_log_takes_the_stores_business_days()
    {
        Require();
        using var super = await office.SignInAsync();
        var today = office.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using var page = JsonDocument.Parse(await super.GetStringAsync($"/api/audit?from={today}&to={today}&pageSize=500"));
        var times = page.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("atUtc").GetDateTime().ToLocalTime()).ToList();
        Assert.NotEmpty(times);
        Assert.All(times, t => Assert.Equal(office.Today, DateOnly.FromDateTime(t)));
    }
}
