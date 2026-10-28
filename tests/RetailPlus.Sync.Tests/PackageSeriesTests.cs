using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Sync.Tests;

/// <summary>
/// A till with more rows than one package holds (its first package after joining holds everything): its changes go as a
/// series of packages of a bounded number of rows, sent and applied in order, resumed after a connection drops; and the
/// server's packages are written and applied a few rows at a time.
/// </summary>
public sealed class PackageSeriesTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static async Task ItemsAsync(Till till, string prefix, int count)
    {
        var session = await till.SessionAsync();
        for (int i = 1; i <= count; i++)
            await till.Inventory.SaveAsync(new Item { LocationId = session.LocationId, Plu = $"{prefix}{i:000}", Name = $"Item {prefix} {i}", Price1 = i, OnHand = i % 7 }, null, null, null);
    }

    private static PackageManifest Manifest(string file)
    {
        using var stream = File.OpenRead(file);
        using var package = PackageReader.Open(stream);
        return package.Manifest;
    }

    private static int Waiting(Till till) => Directory.GetFiles(till.Settings.Outbox, "*" + SyncPackage.Extension).Length;

    /// <summary>The connection drops for the uploads after the first few.</summary>
    private sealed class Drop(Func<HttpRequestMessage, bool> drop) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            drop(request) ? throw new HttpRequestException("The connection dropped.") : base.SendAsync(request, cancellationToken);
    }

    [SkippableFact]
    public async Task A_till_sends_a_series_of_bounded_packages_in_order_and_goes_on_after_the_connection_drops()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        till.Settings.MaxRowsPerPackage = 40;
        await ItemsAsync(till, "SER", 120);

        // The first package after joining holds everything the till has: far more than 40 rows.
        var files = await till.Sync.CreatePackagesAsync();
        Assert.True(files.Count >= 6, $"{files.Count} packages");
        Assert.Equal(files.Order(StringComparer.Ordinal), files);
        var manifests = files.Select(Manifest).ToList();
        Assert.All(manifests, m => Assert.InRange(m.Rows, 1, 40));
        Assert.Single(manifests.Select(m => m.SeriesId).Distinct());
        Assert.NotNull(manifests[0].SeriesId);
        Assert.Equal(Enumerable.Range(1, files.Count), manifests.Select(m => m.Part));
        Assert.Null(manifests[0].PreviousBatchId);
        Assert.Equal(manifests.SkipLast(1).Select(m => (Guid?)m.BatchId), manifests.Skip(1).Select(m => m.PreviousBatchId));
        Assert.Equal([.. Enumerable.Repeat(false, files.Count - 1), true], manifests.Select(m => m.LastPart));
        Assert.Equal([.. Enumerable.Repeat(false, files.Count - 1), true], manifests.Select(m => m.PackagedThroughUtc is not null));
        Assert.Equal(files.Count, manifests.Select(m => m.BatchId).Distinct().Count());

        // A package that comes before the one it follows is refused: the server applies a series in order.
        int station = till.Data.Station!.StationNumber;
        var key = StationKey.Parse(till.Settings.StationKey!);
        var early = Path.Combine(world.Folder, "early-" + Guid.NewGuid().ToString("N")[..6] + SyncPackage.Extension);
        File.Copy(files[1], early);
        var refused = await Assert.ThrowsAsync<SyncRefusedException>(() => new SyncClient(world.Server.CreateClient()).UploadAsync(station, key, early));
        Assert.Contains("in order", refused.Message, StringComparison.Ordinal);

        // The connection drops after two packages: those two are on the server, the others wait in the outbox.
        int uploads = 0;
        var dropping = new TillSync(till.Data, till.Settings,
            _ => world.Server.CreateDefaultClient(new Drop(r => r.RequestUri!.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal) && ++uploads > 2)));
        var broken = await dropping.RunAsync();
        Assert.False(broken.Ok);
        Assert.Contains("could not be reached", broken.Error, StringComparison.Ordinal);
        Assert.Equal(2, broken.Sent);
        Assert.Equal(files.Count - 2, files.Count(File.Exists));
        Assert.Equal(files.Skip(2), files.Where(File.Exists));

        // The next run goes on from the third package, and the server ends up with everything.
        await till.SyncAsync();
        Assert.Equal(0, Waiting(till));
        await using (var server = world.ServerData.Create())
        {
            var batches = manifests.Select(m => m.BatchId).ToList();
            var applied = await server.SyncBatches.AsNoTracking().Where(b => batches.Contains(b.BatchId) && b.Status == SyncBatchStatus.Applied).ToListAsync();
            Assert.Equal(files.Count, applied.Count);
            var order = applied.OrderBy(b => b.AppliedAtUtc).ThenBy(b => b.Id).Select(b => b.BatchId).ToList();
            Assert.Equal(batches, order);
            // The package sent too early was recorded as refused, and applied later in its turn.
            Assert.True(await server.SyncBatches.AnyAsync(b => b.BatchId == manifests[1].BatchId && b.Status == SyncBatchStatus.Failed));

            var items = await server.Items.AsNoTracking().Where(i => i.Plu.StartsWith("SER")).ToListAsync();
            Assert.Equal(120, items.Count);
            Assert.All(items, i => Assert.Equal(int.Parse(i.Plu[3..], System.Globalization.CultureInfo.InvariantCulture) % 7, i.OnHand));
        }

        // Sending a package of the series again changes nothing.
        var again = Path.Combine(world.Folder, "again-" + Guid.NewGuid().ToString("N")[..6] + SyncPackage.Extension);
        File.Copy(early, again);
        Assert.Equal(nameof(SyncBatchStatus.Duplicate), (await new SyncClient(world.Server.CreateClient()).UploadAsync(station, key, again)).Status);

        // A package saved to a file for a USB stick stays one file, however many rows it holds.
        var stick = await till.Sync.SavePackageToFileAsync(Path.Combine(world.Folder, "stick-" + Guid.NewGuid().ToString("N")[..6]), includeDatabase: false);
        var whole = Manifest(stick);
        Assert.True(whole.Rows > 40, $"{whole.Rows} rows");
        Assert.Null(whole.SeriesId);
        Assert.True(whole.LastPart);
    }

    [SkippableFact]
    public async Task A_refused_package_sets_the_rest_of_its_series_aside()
    {
        Require();
        var store = await world.AddStoreAsync();
        var till = await world.NewJoinedTillAsync(store.Id);
        till.Settings.MaxRowsPerPackage = 30;
        await ItemsAsync(till, "REF", 40);
        var files = await till.Sync.CreatePackagesAsync();
        Assert.True(files.Count >= 3);

        // The second package is changed after it was written: the server refuses it, and the till does not send the rest.
        using (var zip = ZipFile.Open(files[1], ZipArchiveMode.Update))
        {
            var entry = zip.Entries.First(e => e.FullName.StartsWith("tables/", StringComparison.Ordinal));
            string name = entry.FullName, json;
            using (var reader = new StreamReader(entry.Open())) json = await reader.ReadToEndAsync();
            entry.Delete();
            await using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            await writer.WriteAsync(json + " ");
        }
        var (sent, refused) = await till.Sync.SendOutboxAsync();
        Assert.Equal((1, files.Count - 1), (sent, refused));
        Assert.Equal(0, Waiting(till));
        Assert.Equal(files.Count - 1, Directory.GetFiles(till.Sync.RefusedFolder, "*" + SyncPackage.Extension).Length);
        await using (var db = till.Data.Create())
        {
            var batch = await db.SyncBatches.AsNoTracking().SingleAsync(b => b.FileName == Path.GetFileName(files[2]));
            Assert.Equal(SyncBatchStatus.Failed, batch.Status);
            Assert.Contains("earlier package of the same series", batch.Error, StringComparison.Ordinal);
        }

        // "Send everything again" puts it right.
        await till.Sync.ResetAsync();
        await till.SyncAsync();
        await using var server = world.ServerData.Create();
        Assert.Equal(40, await server.Items.CountAsync(i => i.Plu.StartsWith("REF")));
    }

    [SkippableFact]
    public async Task The_servers_package_larger_than_a_package_holds_is_written_as_it_is_read_and_applied_a_few_rows_at_a_time()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        a.Settings.MaxRowsPerPackage = 50;
        b.Settings.MaxRowsPerPackage = 25;
        await ItemsAsync(a, "BIG", 150);
        await a.SyncAsync();

        // Till B receives one package from the server with every new row, and applies it 25 rows at a time.
        var run = await b.SyncAsync();
        Assert.True(run.Received!.Inserted > 150, $"{run.Received.Inserted} rows");
        await using (var tillB = b.Data.Create())
        {
            var items = await tillB.Items.AsNoTracking().Where(i => i.Plu.StartsWith("BIG")).ToListAsync();
            Assert.Equal(150, items.Count);
            Assert.All(items, i => Assert.Equal(int.Parse(i.Plu[3..], System.Globalization.CultureInfo.InvariantCulture) % 7, i.OnHand));
            var ids = items.Select(i => i.Id).ToList();
            Assert.Equal(items.Sum(i => i.OnHand), (await tillB.StockMovements.Where(m => ids.Contains(m.ItemId)).Select(m => m.Quantity).ToListAsync()).Sum());
        }

        // A till that joins now gets everything in its first package, downloaded like any other, and goes on from its end.
        var c = await world.NewJoinedTillAsync(store.Id);
        await using var tillC = c.Data.Create();
        Assert.Equal(150, await tillC.Items.CountAsync(i => i.Plu.StartsWith("BIG")));
        Assert.True((await tillC.SyncStates.AsNoTracking().SingleAsync()).DownloadCursor > 0);
        Assert.Equal(SyncBatchStatus.Applied, (await c.SyncAsync()).Received!.Status);
    }
}
