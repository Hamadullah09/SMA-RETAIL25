using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Server;
using RetailPlus.Sync;

namespace RetailPlus.Sync.Tests;

/// <summary>
/// The server, run in-process on a throwaway LocalDB database (or RETAILPLUS_TEST_SQL), and tills on throwaway SQLite
/// files that reach it over HTTP through the test server. Skipped when no SQL Server is reachable.
/// </summary>
public sealed class SyncWorld : IAsyncLifetime
{
    public const string AdminUser = "super";
    public const string AdminPassword = "Super-secret-1";
    public const string ServerUrl = "http://localhost/";

    private readonly string _database = "RetailPlus_SyncTest_" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<DataContext> _tills = [];

    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "RetailPlus_SyncTest_" + Guid.NewGuid().ToString("N")[..12]);
    public bool Available { get; private set; }
    public string UnavailableReason { get; private set; } = "";
    public WebApplicationFactory<Program> Server { get; private set; } = null!;

    public string ConnectionString => Environment.GetEnvironmentVariable("RETAILPLUS_TEST_SQL") is { Length: > 0 } server
        ? new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString
        : RetailPlusDb.LocalDbConnectionString(_database);

    /// <summary>The server's database, as the server sees it.</summary>
    public DataContext ServerData => Server.Services.GetRequiredService<DataContext>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Folder);
        try
        {
            await using (var probe = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master", ConnectTimeout = 15 }.ConnectionString))
                await probe.OpenAsync();
        }
        catch (SqlException ex)
        {
            UnavailableReason = "No SQL Server for the sync tests: " + ex.Message;
            return;
        }
        Server = new Factory(ConnectionString, Path.Combine(Folder, "server"));
        _ = Server.Server;   // starts it: migrations, roles, the super administrator
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (Available)
        {
            await Server.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var db = RetailPlusDb.CreateContext(DatabaseConnection.SqlServer(ConnectionString));
            await db.Database.EnsureDeletedAsync();
        }
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Factory(string connection, string dataFolder) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:RetailPlus", connection);
            builder.UseSetting("Server:DataFolder", dataFolder);
            builder.UseSetting("Bootstrap:AdminUserName", AdminUser);
            builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            builder.UseSetting("Jwt:Key", "sync-tests-signing-key-0123456789abcdef");
        }
    }

    // The web side ------------------------------------------------------------------------------------------------------

    /// <summary>An HTTP client signed in as the given user.</summary>
    public async Task<HttpClient> SignInAsync(string user = AdminUser, string password = AdminPassword)
    {
        var client = Server.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(user, password));
        response.EnsureSuccessStatusCode();
        var signedIn = (await response.Content.ReadFromJsonAsync<SignInResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Token);
        return client;
    }

    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async Task<StoreInfo> AddStoreAsync(string? code = null)
    {
        using var admin = await SignInAsync();
        code ??= "S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var response = await admin.PostAsJsonAsync("/api/stores", new StoreRequest(code, "Store " + code, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoreInfo>(Json))!;
    }

    /// <summary>A till set up on the server (the next free number unless one is given), with its one-time enrolment code.</summary>
    public async Task<EnrollmentCodeInfo> AddStationAsync(Guid storeId, int? number = null)
    {
        using var admin = await SignInAsync();
        using var response = await admin.PostAsJsonAsync($"/api/stores/{storeId}/stations", new NewStationRequest(number, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EnrollmentCodeInfo>(Json))!;
    }

    // Tills -------------------------------------------------------------------------------------------------------------

    /// <summary>A new till as the app starts one: its own SQLite file, seeded with a store of its own and station 1.</summary>
    public async Task<Till> NewTillAsync()
    {
        var file = Path.Combine(Folder, "till-" + Guid.NewGuid().ToString("N")[..8], "smaretail.db");
        var data = new DataContext(DatabaseConnection.SqliteFile(file));
        await data.MigrateAsync();
        await new SetupService(data).SeedAsync(1);
        _tills.Add(data);
        var settings = new TillSyncSettings { Outbox = TillSync.OutboxFor(file), AppVersion = "test" };
        return new Till(data, settings, new TillSync(data, settings, _ => Server.CreateClient()), file);
    }

    /// <summary>Joins a till to a store as a new station of the server.</summary>
    public async Task<Till> JoinAsync(Till till, Guid storeId, int? number = null)
    {
        var code = await AddStationAsync(storeId, number);
        var joined = await TillSync.JoinAsync(till.Data, ServerUrl, code.Code, "test", _ => Server.CreateClient());
        till.Settings.ServerUrl = ServerUrl;
        till.Settings.StationKey = joined.StationKey;
        return till;
    }

    /// <summary>A new till joined to the store.</summary>
    public async Task<Till> NewJoinedTillAsync(Guid storeId) => await JoinAsync(await NewTillAsync(), storeId);
}

/// <summary>A till: its database, its sync settings and its sync.</summary>
public sealed class Till(DataContext data, TillSyncSettings settings, TillSync sync, string file)
{
    public DataContext Data { get; } = data;
    public TillSyncSettings Settings { get; } = settings;
    public TillSync Sync { get; } = sync;
    public string File { get; } = file;

    public InventoryService Inventory => new(Data);
    public SalesService Sales => new(Data);
    public PartyService Parties => new(Data);
    public SetupService Setup => new(Data);

    /// <summary>The till's session at its store's POS location.</summary>
    public async Task<SessionInfo> SessionAsync()
    {
        await using var db = Data.Create();
        var location = await db.Locations.AsNoTracking().FirstAsync(l => l.StoreId == Data.Station!.StoreId && l.Code == "POS");
        return new SessionInfo { StationNumber = Data.Station!.StationNumber, LocationId = location.Id, LocationCode = location.Code, UserName = "tester", AccessLevel = 4 };
    }

    /// <summary>One synchronisation; fails the test when it reports a problem.</summary>
    public async Task<SyncRunResult> SyncAsync()
    {
        var result = await Sync.RunAsync();
        Assert.True(result.Ok, result.Error);
        return result;
    }
}
