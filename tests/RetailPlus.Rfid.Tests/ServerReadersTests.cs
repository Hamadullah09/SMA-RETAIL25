using System.Net;
using System.Text;
using System.Text.Json;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid.Tests;

/// <summary>A hub's reader list from the server, against a fake server: the key it sends, and the list it keeps for a start without the server.</summary>
public sealed class ServerReadersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetailPlus_RfidList_" + Guid.NewGuid().ToString("N")[..12]);

    private string ListFile => Path.Combine(_folder, "readers.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public static readonly Guid StoreId = Guid.CreateVersion7();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Down { get; set; }
        public List<RfidReader> Readers { get; } = [];
        public List<string> Paths { get; } = [];
        public List<string?> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Keys.Add(request.Headers.TryGetValues(RfidServerReaders.KeyHeader, out var values) ? values.Single() : null);
            if (Down) throw new HttpRequestException("No route to the server.");
            var body = Status == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new RfidServerReaderList(StoreId, "S1", "Store one", "main", Readers), RfidServerReaders.Json)
                : "{\"error\":\"The server does not know this RFID hub key, or it was revoked.\"}";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static RfidReader Reader(string name) => new()
    {
        Id = Guid.CreateVersion7(), Name = name, Host = "sim", Port = 4001, BeeperMode = RfidBeeperMode.AfterInventory, Region = RfidFrequencyRegion.Etsi,
        Antennas = [new RfidAntenna { Port = 1, StationNumber = 3, PowerDbm = 27, Purpose = RfidAntennaPurpose.PointOfSale }],
    };

    private RfidServerReaders Hub(FakeServer server, string key = "rfh_one") => new("https://server.example/", key, "main", ListFile, server);

    [Fact]
    public async Task Reads_the_store_readers_with_its_key_and_keeps_the_list()
    {
        using var server = new FakeServer();
        server.Readers.Add(Reader("Counter"));
        using var readers = Hub(server);

        var reader = Assert.Single(await readers.LoadAsync());
        Assert.Equal("Counter", reader.Name);
        Assert.Equal(RfidBeeperMode.AfterInventory, reader.BeeperMode);
        Assert.Equal(RfidFrequencyRegion.Etsi, reader.Region);
        Assert.Equal((3, 27), (reader.Antennas.Single().StationNumber!.Value, reader.Antennas.Single().PowerDbm));
        Assert.Equal("Store one", readers.StoreName);
        Assert.Equal("/api/rfid/readers?hub=main", Assert.Single(server.Paths));
        Assert.Equal("rfh_one", Assert.Single(server.Keys));
        Assert.True(File.Exists(ListFile));
        Assert.DoesNotContain("rfh_one", await File.ReadAllTextAsync(ListFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hub_that_starts_without_the_server_serves_the_readers_it_kept()
    {
        using var server = new FakeServer();
        server.Readers.Add(Reader("Counter"));
        using (var first = Hub(server)) await first.LoadAsync();

        // Restarted while the server cannot be reached: the kept list, once.
        server.Down = true;
        var problems = new List<string>();
        using var restarted = Hub(server);
        restarted.Log = problems.Add;
        Assert.Equal("Counter", Assert.Single(await restarted.LoadAsync()).Name);
        Assert.Contains(problems, p => p.Contains("kept", StringComparison.Ordinal));
        // Later failures are thrown, so the hub goes on with what it has and logs the problem.
        await Assert.ThrowsAsync<HttpRequestException>(() => restarted.LoadAsync());

        // The server answers again: its list is taken and kept.
        server.Down = false;
        server.Readers.Add(Reader("Gate"));
        Assert.Equal(2, (await restarted.LoadAsync()).Count);
        server.Down = true;
        using var again = Hub(server);
        Assert.Equal(2, (await again.LoadAsync()).Count);
    }

    [Fact]
    public async Task A_refused_key_forgets_the_kept_list()
    {
        using var server = new FakeServer();
        server.Readers.Add(Reader("Counter"));
        using var readers = Hub(server);
        await readers.LoadAsync();

        server.Status = HttpStatusCode.Unauthorized;   // revoked on the server
        var refused = await Assert.ThrowsAsync<RfidException>(() => readers.LoadAsync());
        Assert.Contains("key", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(ListFile));

        server.Down = true;
        using var restarted = Hub(server);
        await Assert.ThrowsAsync<HttpRequestException>(() => restarted.LoadAsync());
    }

    [Fact]
    public async Task A_list_kept_with_another_key_is_not_used()
    {
        using var server = new FakeServer();
        server.Readers.Add(Reader("Counter"));
        using (var first = Hub(server, "rfh_one")) await first.LoadAsync();

        server.Down = true;
        using var other = Hub(server, "rfh_two");
        await Assert.ThrowsAsync<HttpRequestException>(() => other.LoadAsync());
    }
}
