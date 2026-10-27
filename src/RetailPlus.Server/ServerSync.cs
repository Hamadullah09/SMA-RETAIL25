using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Sync;

namespace RetailPlus.Server;

/// <summary>A till the server knows, with its key, after its request was checked.</summary>
public sealed record AuthenticatedStation(StationLink Link, byte[] Key)
{
    public TillScope Till => new(Link.StoreId, Link.StationNumber);
}

/// <summary>
/// The server's side of the synchronisation: tills joining with an enrolment code, their uploads (checked with their key
/// and applied), and the packages of what each till should receive.
/// </summary>
public sealed class ServerSync(DataContext data, StationKeys keys, ServerFolders folders, ILogger<ServerSync> log)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> StationLocks = new();
    private readonly PackageWriter _writer = new();
    private readonly PackageApplier _applier = new();

    public static string AppVersion { get; } = typeof(ServerSync).Assembly.GetName().Version?.ToString() ?? "1.0";

    // Enrolment ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Joins a till with its one-time code: a new key is made (any earlier one stops working), the code is used up, and
    /// the till gets its store, number, key and a first package of everything it should have. Null when the code is
    /// unknown, used or expired.
    /// </summary>
    public async Task<EnrollResponse?> EnrollAsync(EnrollRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return null;
        var hash = StationKeys.HashCode(request.Code);
        await using var db = data.Create();
        var link = await db.StationLinks.SingleOrDefaultAsync(l => l.EnrollmentCodeHash == hash, cancellationToken);
        if (link is null || link.RetiredAtUtc is not null || link.EnrollmentExpiresAtUtc < DateTime.UtcNow) return null;
        var store = await db.Stores.AsNoTracking().SingleAsync(s => s.Id == link.StoreId, cancellationToken);
        var key = StationKey.New();
        link.ProtectedKey = keys.Protect(key);
        link.EnrollmentCodeHash = null;
        link.EnrollmentExpiresAtUtc = null;
        link.EnrolledAtUtc = SyncColumns.Now();
        link.AppVersion = request.AppVersion;
        link.DownloadCursor = 0;
        await db.SaveChangesAsync(cancellationToken);
        ServerLog.StationJoined(log, link.StationNumber, store.Code, request.MachineName);

        using var package = new MemoryStream();
        var manifest = await _writer.WriteDownloadAsync(db, package, new DownloadOptions(new TillScope(link.StoreId, link.StationNumber), StationKey.Parse(key), 0, AppVersion), cancellationToken);
        await RecordDownloadAsync(manifest, cancellationToken);
        await using (var acknowledged = data.Create())
        {
            // The first package is applied with the joining, so the till starts from its end (for packages on a USB stick).
            var row = await acknowledged.StationLinks.SingleAsync(l => l.Id == link.Id, cancellationToken);
            row.DownloadCursor = manifest.CursorTo ?? 0;
            await acknowledged.SaveChangesAsync(cancellationToken);
        }
        return new EnrollResponse(store.Id, store.Code, store.Name, link.StationNumber, key, Convert.ToBase64String(package.ToArray()));
    }

    // Station requests --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks a till's signed request (X-Station, X-Station-Time, X-Station-Signature over the method, path, time and
    /// body hash). Null when the station is unknown, retired, not joined, the time is off, or the signature is wrong.
    /// </summary>
    public async Task<AuthenticatedStation?> AuthenticateAsync(HttpRequest request, string bodySha256, CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.Headers[StationAuth.StationHeader], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || !long.TryParse(request.Headers[StationAuth.TimeHeader], NumberStyles.Integer, CultureInfo.InvariantCulture, out var time)
            || request.Headers[StationAuth.SignatureHeader].ToString() is not { Length: > 0 } signature)
            return null;
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - time) > StationAuth.Window.TotalSeconds) return null;
        var link = await LinkAsync(number, cancellationToken);
        if (link is null || keys.Unprotect(link.ProtectedKey) is not { } key) return null;
        var bytes = StationKey.Parse(key);
        var pathAndQuery = request.PathBase.Add(request.Path).ToString() + request.QueryString.ToString();
        return StationAuth.Verify(bytes, request.Method, pathAndQuery, time, bodySha256, signature) ? new AuthenticatedStation(link, bytes) : null;
    }

    /// <summary>A joined, not retired station, by number.</summary>
    public async Task<StationLink?> LinkAsync(int number, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        return await db.StationLinks.AsNoTracking().SingleOrDefaultAsync(l => l.StationNumber == number && l.RetiredAtUtc == null && l.ProtectedKey != null, cancellationToken);
    }

    /// <summary>The key of a joined station, for packages carried on a USB stick.</summary>
    public byte[]? KeyOf(StationLink link) => keys.Unprotect(link.ProtectedKey) is { } key ? StationKey.Parse(key) : null;

    // Uploads ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Copies a request body into the incoming folder (at most <paramref name="limit"/> bytes) and returns the file and
    /// its SHA-256. The caller deletes the file.
    /// </summary>
    public async Task<(string File, string Sha256)> ReceiveAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        var file = Path.Combine(folders.Incoming, Guid.NewGuid().ToString("N") + SyncPackage.Extension);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        await using (var target = File.Create(file))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    target.Close();
                    File.Delete(file);
                    throw new PackageRejectedException($"The package is larger than the {limit / (1024 * 1024)} MB the server takes.");
                }
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        return (file, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>
    /// Applies a till's package (sent by the till, or brought on a USB stick and uploaded by an administrator). Uploads of
    /// one station are applied one at a time. Every attempt is recorded in SyncBatches.
    /// </summary>
    public async Task<UploadResponse> ApplyUploadAsync(StationLink link, byte[] key, string file, string sha256, string fileName, CancellationToken cancellationToken)
    {
        var gate = StationLocks.GetOrAdd(link.StationNumber, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(file);
            ApplyResult result;
            PackageManifest manifest;
            try
            {
                using var package = PackageReader.Open(stream);
                manifest = package.Manifest;
                result = await _applier.ApplyAsync(data, package, key, new ApplyOptions
                {
                    Side = SyncSide.Server, Till = new TillScope(link.StoreId, link.StationNumber), FileName = fileName, FileSha256 = sha256,
                }, cancellationToken);
            }
            catch (PackageRejectedException ex)
            {
                await RecordUnreadableAsync(link, fileName, sha256, ex.Message, cancellationToken);
                return new UploadResponse(nameof(SyncBatchStatus.Failed), Guid.Empty, 0, 0, 0, [], ex.Message);
            }
            await using (var db = data.Create())
            {
                var row = await db.StationLinks.SingleAsync(l => l.Id == link.Id, cancellationToken);
                row.LastUploadAtUtc = SyncColumns.Now();
                if (manifest.AppVersion is { Length: > 0 } version) row.AppVersion = version;
                if (result.Status != SyncBatchStatus.Failed) row.DownloadCursor = Math.Max(row.DownloadCursor, manifest.Cursor);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (result.Status == SyncBatchStatus.Failed)
                ServerLog.PackageRefused(log, manifest.BatchId, link.StationNumber, result.Error);
            return new UploadResponse(result.Status.ToString(), result.BatchId, result.Inserted, result.Updated, result.Deleted, result.Notes, result.Error);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A file that is not a package at all is recorded too, so the failure shows on the till's line.</summary>
    private async Task RecordUnreadableAsync(StationLink link, string fileName, string sha256, string error, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        db.SyncBatches.Add(new SyncBatch
        {
            BatchId = Guid.Empty, StoreId = link.StoreId, StationNumber = link.StationNumber, Direction = SyncDirection.Upload, FileName = fileName,
            FileSha256 = sha256, ReceivedAtUtc = SyncColumns.Now(), Status = SyncBatchStatus.Failed, Error = error,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    // Downloads -------------------------------------------------------------------------------------------------------

    /// <summary>Writes the package of what the station should receive after <paramref name="cursor"/>.</summary>
    public async Task<PackageManifest> WriteDownloadAsync(StationLink link, byte[] key, long cursor, Stream output, bool fromTill, CancellationToken cancellationToken)
    {
        PackageManifest manifest;
        await using (var db = data.Create())
            manifest = await _writer.WriteDownloadAsync(db, output, new DownloadOptions(new TillScope(link.StoreId, link.StationNumber), key, cursor, AppVersion), cancellationToken);
        await using (var db = data.Create())
        {
            var row = await db.StationLinks.SingleAsync(l => l.Id == link.Id, cancellationToken);
            if (fromTill)
            {
                // The till asks from the change it has applied, so this is also its acknowledgement.
                row.LastDownloadAtUtc = SyncColumns.Now();
                row.DownloadCursor = Math.Max(row.DownloadCursor, manifest.Cursor);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        await RecordDownloadAsync(manifest, cancellationToken);
        return manifest;
    }

    /// <summary>Packages with rows are recorded (empty ones, every few minutes per till, are not).</summary>
    private async Task RecordDownloadAsync(PackageManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.Rows == 0) return;
        await using var db = data.Create();
        db.SyncBatches.Add(new SyncBatch
        {
            BatchId = manifest.BatchId, StoreId = manifest.StoreId, StationNumber = manifest.StationNumber, Direction = SyncDirection.Download,
            FileName = SyncPackage.FileName(manifest), Rows = manifest.Rows, AppVersion = manifest.AppVersion, ReceivedAtUtc = SyncColumns.Now(),
            AppliedAtUtc = SyncColumns.Now(), Status = SyncBatchStatus.Sent, Notes = $"Changes {manifest.Cursor} to {manifest.CursorTo}",
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
