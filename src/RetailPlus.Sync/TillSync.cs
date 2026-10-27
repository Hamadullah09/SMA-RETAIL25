using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Sync;

/// <summary>How a till reaches its server.</summary>
public sealed class TillSyncSettings
{
    /// <summary>The server's address, e.g. https://pos.example.com/; null while the till has not joined one.</summary>
    public string? ServerUrl { get; set; }
    /// <summary>The station's key (Base64), as given when the till joined.</summary>
    public string? StationKey { get; set; }
    /// <summary>Where packages wait until the server has them: the outbox folder next to the database file.</summary>
    public required string Outbox { get; set; }
    public string? AppVersion { get; set; }

    public bool Joined => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(StationKey);
}

/// <summary>Where a till stands with its server, for the status line and the Server tab.</summary>
public sealed record TillSyncStatus(bool Joined, DateTime? LastSentAtUtc, DateTime? LastReceivedAtUtc, DateTime? LastAttemptAtUtc,
    int Waiting, int Refused, string? LastError, long Cursor, DateTime PackagedThroughUtc);

/// <summary>What one run did.</summary>
public sealed record SyncRunResult(string? PackageWritten, int Sent, int Refused, ApplyResult? Received, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>A till that joined a server.</summary>
public sealed record JoinResult(Guid StoreId, string StoreName, int StationNumber, string StationKey, ApplyResult FirstPackage);

/// <summary>
/// The till's side of the synchronisation. A run writes a package of what changed (when anything did) into the
/// outbox, sends the outbox to the server oldest first, then downloads and applies what changed on the server. Packages
/// stay in the outbox until the server has them, so nothing is lost while there is no connection; the server ignores a
/// package it already has, so sending one twice is harmless.
/// </summary>
public sealed class TillSync(DataContext data, TillSyncSettings settings, Func<Uri, HttpClient>? httpFactory = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly PackageWriter _writer = new();
    private readonly PackageApplier _applier = new();

    public TillSyncSettings Settings { get; } = settings;

    /// <summary>The outbox of a till whose database is <paramref name="databaseFile"/>: a folder next to it.</summary>
    public static string OutboxFor(string databaseFile) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!, "outbox");

    /// <summary>Packages the server refused for good are kept here, for the record.</summary>
    public string RefusedFolder => Path.Combine(Settings.Outbox, "refused");

    private SemaphoreSlim Gate => Locks.GetOrAdd(Path.GetFullPath(Settings.Outbox), _ => new SemaphoreSlim(1, 1));

    private TillScope Till => data.Station is { StationNumber: > 0 } here ? new TillScope(here.StoreId, here.StationNumber)
        : throw new InvalidOperationException("This program does not know its station yet.");

    private byte[] Key => StationKey.Parse(Settings.StationKey ?? throw new InvalidOperationException("This till has not joined a server."));

    private SyncClient Client()
    {
        var url = Settings.ServerUrl ?? throw new InvalidOperationException("This till has not joined a server.");
        return new SyncClient((httpFactory ?? DefaultHttp)(ServerUri(url)));
    }

    /// <summary>The server address with the trailing slash relative paths need.</summary>
    public static Uri ServerUri(string url) => new(url.Trim().TrimEnd('/') + "/", UriKind.Absolute);

    private static HttpClient DefaultHttp(Uri server) => new() { BaseAddress = server, Timeout = TimeSpan.FromMinutes(5) };

    // Status ------------------------------------------------------------------------------------------------------------

    public async Task<TillSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var state = await db.SyncStates.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new SyncState();
        int waiting = Directory.Exists(Settings.Outbox) ? Directory.GetFiles(Settings.Outbox, "*" + SyncPackage.Extension).Length : 0;
        int refused = Directory.Exists(RefusedFolder) ? Directory.GetFiles(RefusedFolder, "*" + SyncPackage.Extension).Length : 0;
        return new TillSyncStatus(Settings.Joined, state.LastSentAtUtc, state.LastReceivedAtUtc, state.LastAttemptAtUtc, waiting, refused,
            state.LastError, state.DownloadCursor, state.PackagedThroughUtc);
    }

    private static async Task<SyncState> StateAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        var state = await db.SyncStates.FirstOrDefaultAsync(cancellationToken);
        if (state is not null) return state;
        db.SyncStates.Add(state = new SyncState());
        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    private async Task NoteAsync(string? error, Action<SyncState>? change = null)
    {
        await using var db = data.Create();
        var state = await StateAsync(db, CancellationToken.None);
        state.LastAttemptAtUtc = SyncColumns.Now();
        state.LastError = error;
        change?.Invoke(state);
        await db.SaveChangesAsync();
    }

    // Packages ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a package of everything changed since the last one into the outbox and moves the mark on; null when nothing
    /// changed. With <paramref name="includeDatabase"/> the package also carries a copy of the whole database file.
    /// </summary>
    public async Task<string?> CreatePackageAsync(bool includeDatabase = false, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var state = await StateAsync(db, cancellationToken);
        var (manifest, file) = await WriteAsync(db, state.PackagedThroughUtc, state.DownloadCursor, includeDatabase, Settings.Outbox, cancellationToken);
        if (manifest is null) return null;
        state.PackagedThroughUtc = manifest.PackagedThroughUtc!.Value;
        db.SyncBatches.Add(new SyncBatch
        {
            BatchId = manifest.BatchId, StoreId = manifest.StoreId, StationNumber = manifest.StationNumber, Direction = SyncDirection.Upload,
            FileName = Path.GetFileName(file), Rows = manifest.Rows, AppVersion = manifest.AppVersion, ReceivedAtUtc = manifest.CreatedAtUtc,
            Status = SyncBatchStatus.Waiting,
        });
        await db.SaveChangesAsync(cancellationToken);
        return file;
    }

    private async Task<(PackageManifest? Manifest, string? File)> WriteAsync(RetailPlusDbContext db, DateTime changedAfter, long cursor, bool includeDatabase,
        string folder, CancellationToken cancellationToken, bool evenIfEmpty = false)
    {
        Directory.CreateDirectory(folder);
        string? copy = null;
        var temp = Path.Combine(folder, ".writing-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (includeDatabase)
            {
                if (data.Connection.Provider != DatabaseProvider.Sqlite) throw new BusinessRuleException("Only a till's own database file can be copied into a package.");
                var copies = Path.Combine(Path.GetTempPath(), "smaretail-copy-" + Guid.NewGuid().ToString("N"));
                copy = await new BackupService(data).BackupAsync(copies, cancellationToken);
            }
            PackageManifest? manifest;
            await using (var output = File.Create(temp))
                manifest = await _writer.WriteUploadAsync(db, output, new UploadOptions(Till, Key, changedAfter == default ? null : changedAfter, cursor, Settings.AppVersion)
                {
                    DatabaseCopy = copy, EvenIfEmpty = evenIfEmpty,
                }, cancellationToken);
            if (manifest is null) return (null, null);
            var file = Path.Combine(folder, SyncPackage.FileName(manifest));
            File.Move(temp, file);
            return (manifest, file);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (copy is not null) Directory.Delete(Path.GetDirectoryName(copy)!, true);
        }
    }

    /// <summary>
    /// "Save package to file": one package with everything the server has not confirmed yet (it covers the packages
    /// still in the outbox, which stay there and are sent later as well), for a USB stick. Returns the file written.
    /// </summary>
    public async Task<string> SavePackageToFileAsync(string folder, bool includeDatabase, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var state = await StateAsync(db, cancellationToken);
        await CreatePackageAsync(false, cancellationToken);   // what is new also waits in the outbox, in case the stick is lost
        var (_, file) = await WriteAsync(db, state.SentThroughUtc, state.DownloadCursor, includeDatabase, folder, cancellationToken, evenIfEmpty: true);
        return file!;
    }

    /// <summary>
    /// "Load package from file": applies a package the server made for this till and that came on a USB stick. It must
    /// start where this till's last download ended (or earlier).
    /// </summary>
    public async Task<ApplyResult> LoadPackageFromFileAsync(string file, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await ApplyDownloadAsync(file, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ApplyResult> ApplyDownloadAsync(string file, CancellationToken cancellationToken)
    {
        long cursor;
        await using (var db = data.Create()) cursor = (await StateAsync(db, cancellationToken)).DownloadCursor;
        await using var stream = File.OpenRead(file);
        using var package = PackageReader.Open(stream);
        var manifest = package.Manifest;
        if (manifest.Kind == SyncDirection.Download && manifest.Cursor > cursor)
            throw new PackageRejectedException($"This package starts at change {manifest.Cursor}, but this till has only received changes up to {cursor}. Ask for a package from {cursor}.");
        if (manifest.Rows == 0 && manifest.Kind == SyncDirection.Download)
        {
            package.Verify(Key);
            if (manifest.StoreId != Till.StoreId || manifest.StationNumber != Till.StationNumber) throw new PackageRejectedException("The package is for another station.");
            await NoteAsync(null, s =>
            {
                s.DownloadCursor = Math.Max(s.DownloadCursor, manifest.CursorTo ?? s.DownloadCursor);
                s.LastReceivedAtUtc = SyncColumns.Now();
            });
            return new ApplyResult(SyncBatchStatus.Applied, manifest.BatchId, 0, 0, 0, 0, [], null);
        }
        return await _applier.ApplyAsync(data, package, Key, new ApplyOptions
        {
            Side = SyncSide.Till, Till = Till, FileName = Path.GetFileName(file),
            AfterApply = async (db, m) =>
            {
                var state = await db.SyncStates.FirstAsync(cancellationToken);
                state.DownloadCursor = Math.Max(state.DownloadCursor, m.CursorTo ?? state.DownloadCursor);
                state.LastReceivedAtUtc = SyncColumns.Now();
                state.LastError = null;
            },
        }, cancellationToken);
    }

    /// <summary>
    /// "Send everything again": the next package holds every row, and the next download every row this till receives.
    /// For recovery after a refused package or a restored backup.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var state = await StateAsync(db, cancellationToken);
        state.PackagedThroughUtc = default;
        state.SentThroughUtc = default;
        state.DownloadCursor = 0;
        await db.SaveChangesAsync(cancellationToken);
    }

    // Running -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One synchronisation: package what changed, send the outbox, download and apply. Problems are recorded in the
    /// status and returned, never thrown: selling goes on whatever happens here. Runs one at a time.
    /// </summary>
    public async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.Joined) return new SyncRunResult(null, 0, 0, null, "This till has not joined a server.");
        await Gate.WaitAsync(cancellationToken);
        string? written = null;
        int sent = 0, refused = 0;
        ApplyResult? received = null;
        try
        {
            written = await CreatePackageAsync(false, cancellationToken);
            (sent, refused) = await SendOutboxAsync(cancellationToken);
            var file = Path.Combine(Settings.Outbox, ".download-" + Guid.NewGuid().ToString("N"));
            try
            {
                long cursor;
                await using (var db = data.Create()) cursor = (await StateAsync(db, cancellationToken)).DownloadCursor;
                await Client().DownloadAsync(Till.StationNumber, Key, cursor, file, cancellationToken);
                received = await ApplyDownloadAsync(file, cancellationToken);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
            string? error = received.Status == SyncBatchStatus.Failed ? "The server's package could not be applied: " + received.Error
                : refused > 0 ? $"The server refused {refused} package(s); see the refused folder." : null;
            await NoteAsync(error);
            return new SyncRunResult(written, sent, refused, received, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SyncAuthException or SyncRefusedException
                                       or PackageRejectedException or BusinessRuleException or InvalidOperationException or DbUpdateException or JsonException)
        {
            var error = ex is HttpRequestException or TaskCanceledException ? "The server could not be reached: " + ex.Message : ex.Message;
            await NoteAsync(error);
            return new SyncRunResult(written, sent, refused, received, error);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Sends the waiting packages, oldest first. Stops at the first connection problem; a refused package is set aside.</summary>
    public async Task<(int Sent, int Refused)> SendOutboxAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Settings.Outbox)) return (0, 0);
        int sent = 0, refused = 0;
        var client = Client();
        foreach (var file in Directory.GetFiles(Settings.Outbox, "*" + SyncPackage.Extension).Order(StringComparer.Ordinal))
        {
            PackageManifest manifest;
            await using (var stream = File.OpenRead(file))
            using (var package = PackageReader.Open(stream))
                manifest = package.Manifest;
            string status;
            string? error = null;
            try
            {
                var response = await client.UploadAsync(Till.StationNumber, Key, file, cancellationToken);
                status = response.Status;
                sent++;
            }
            catch (SyncRefusedException ex)
            {
                Directory.CreateDirectory(RefusedFolder);
                File.Move(file, Path.Combine(RefusedFolder, Path.GetFileName(file)), overwrite: true);
                refused++;
                status = nameof(SyncBatchStatus.Failed);
                error = ex.Message;
            }
            await using var db = data.Create();
            var state = await StateAsync(db, cancellationToken);
            if (error is null)
            {
                File.Delete(file);
                state.LastSentAtUtc = SyncColumns.Now();
                if (manifest.PackagedThroughUtc is { } through && through > state.SentThroughUtc) state.SentThroughUtc = through;
            }
            else state.LastError = error;
            var batch = await db.SyncBatches.FirstOrDefaultAsync(b => b.BatchId == manifest.BatchId && b.Direction == SyncDirection.Upload, cancellationToken);
            if (batch is not null)
            {
                batch.Status = error is null ? SyncBatchStatus.Sent : SyncBatchStatus.Failed;
                batch.Error = error;
                batch.AppliedAtUtc = error is null ? SyncColumns.Now() : null;
                batch.Notes = error is null && status == nameof(SyncBatchStatus.Duplicate) ? "The server had it already." : batch.Notes;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return (sent, refused);
    }

    // Joining ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Joins this till to a server with an enrolment code. The till becomes the station the server set up: its data, made
    /// under the store and station it was seeded with, is moved to the server's store and station number in one
    /// transaction together with the server's first package (where the same thing exists on both, such as the POS
    /// location or a tender type, the server's is kept and the till's rows are moved over to it). Everything on the till
    /// is sent with the next run.
    /// </summary>
    public static async Task<JoinResult> JoinAsync(DataContext data, string serverUrl, string code, string? appVersion,
        Func<Uri, HttpClient>? httpFactory = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Connection.Provider != DatabaseProvider.Sqlite)
            throw new BusinessRuleException("Only a till with its own database file can join a server; this one uses a shared SQL Server database.");
        var server = ServerUri(serverUrl);
        var client = new SyncClient((httpFactory ?? DefaultHttp)(server));
        var joined = await client.EnrollAsync(code, appVersion, cancellationToken);
        var key = StationKey.Parse(joined.StationKey);
        var till = new TillScope(joined.StoreId, joined.StationNumber);
        var old = data.Station;

        var file = Path.Combine(Path.GetTempPath(), "smaretail-join-" + Guid.NewGuid().ToString("N") + SyncPackage.Extension);
        await File.WriteAllBytesAsync(file, Convert.FromBase64String(joined.Package), cancellationToken);
        try
        {
            await using var stream = File.OpenRead(file);
            using var package = PackageReader.Open(stream);
            package.Verify(key);
            var stationId = ServerStationId(package, joined.StationNumber)
                ?? throw new PackageRejectedException($"The server's package does not hold station {joined.StationNumber}.");
            var result = await new PackageApplier().ApplyAsync(data, package, key, new ApplyOptions
            {
                Side = SyncSide.Till, Till = till, FileName = "enrolment",
                BeforeApply = db => MoveToServerAsync(db, old, till, stationId, cancellationToken),
                AfterApply = async (db, m) =>
                {
                    var state = await db.SyncStates.FirstOrDefaultAsync(cancellationToken);
                    if (state is null) db.SyncStates.Add(state = new SyncState { UpdatedAtUtc = SyncColumns.Now() });
                    state.PackagedThroughUtc = default;   // the next package holds everything this till has
                    state.SentThroughUtc = default;
                    state.DownloadCursor = m.CursorTo ?? 0;
                    state.LastReceivedAtUtc = SyncColumns.Now();
                    state.LastError = null;
                },
            }, cancellationToken);
            if (result.Status != SyncBatchStatus.Applied) throw new PackageRejectedException("The server's first package could not be applied: " + result.Error);
            data.Station = new StationIdentity(joined.StoreId, joined.StationNumber);
            return new JoinResult(joined.StoreId, joined.StoreName, joined.StationNumber, joined.StationKey, result);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The id of the server's station row for this till, from the first package.</summary>
    private static Guid? ServerStationId(PackageReader package, int number)
    {
        if (package.Manifest.Tables.FirstOrDefault(t => t.Entity == nameof(Station)) is not { } entry) return null;
        using var rows = package.ReadTable(entry);
        foreach (var row in rows.RootElement.EnumerateArray())
            if (row.GetProperty(nameof(Station.Number)).GetInt32() == number) return row.GetProperty(nameof(Station.Id)).GetGuid();
        return null;
    }

    /// <summary>
    /// Re-points the till's rows from the store and station it was seeded with to the server's: every reference to the
    /// old store goes to the server's store, the old station row's settings go to the server's station row, rows stamped
    /// with the old station number get the new one, and the old station's number counters go (the new number has its own
    /// range). Runs inside the first package's transaction, with references checked at the commit.
    /// </summary>
    private static async Task MoveToServerAsync(RetailPlusDbContext db, StationIdentity? old, TillScope till, Guid stationId, CancellationToken cancellationToken)
    {
        if (old is null) return;
        var sql = db.GetService<ISqlGenerationHelper>();
        // The rows replaced here never reached the server, so they leave no tombstones (the context is applying a package).
        if (old.StoreId != till.StoreId && await db.Stores.SingleOrDefaultAsync(s => s.Id == old.StoreId, cancellationToken) is { } oldStore)
        {
            await PackageApplier.RepointAsync(db, db.Model.FindEntityType(typeof(Store))!, old.StoreId, till.StoreId, cancellationToken);
            db.Stores.Remove(oldStore);
        }
        if (await db.Stations.SingleOrDefaultAsync(s => s.Number == old.StationNumber, cancellationToken) is { } oldStation && oldStation.Id != stationId)
        {
            await PackageApplier.RepointAsync(db, db.Model.FindEntityType(typeof(Station))!, oldStation.Id, stationId, cancellationToken);
            db.Stations.Remove(oldStation);
        }
        if (old.StationNumber != till.StationNumber)
        {
            foreach (var type in db.Model.GetEntityTypes().Where(t => t.GetTableName() is not null && !typeof(ILocalOnly).IsAssignableFrom(t.ClrType) && t.ClrType != typeof(NumberCounter)))
            {
                if (type.FindProperty(nameof(IStationStamped.StationNumber)) is not { } property || (property.ClrType != typeof(int) && property.ClrType != typeof(int?))) continue;
                var table = sql.DelimitIdentifier(type.GetTableName()!, type.GetSchema());
                var column = sql.DelimitIdentifier(property.GetColumnName());
                string restamp = "UPDATE " + table + " SET " + column + " = {0} WHERE " + column + " = {1}";
                await db.Database.ExecuteSqlRawAsync(restamp, [till.StationNumber, old.StationNumber], cancellationToken);
            }
            // The new number has its own range of document numbers (or gets its counters back from the server).
            db.NumberCounters.RemoveRange(await db.NumberCounters.Where(c => c.StationNumber == old.StationNumber).ToListAsync(cancellationToken));
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
