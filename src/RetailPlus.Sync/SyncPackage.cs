using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RetailPlus.Core.Model;

namespace RetailPlus.Sync;

/// <summary>One file inside a package, with what the manifest promises about it.</summary>
public sealed class PackageEntry
{
    /// <summary>The entity name for a table; "Tombstone" for the deletions; "Database" for the database copy.</summary>
    public string Entity { get; set; } = "";
    public string File { get; set; } = "";
    public int Rows { get; set; }
    public long Bytes { get; set; }
    /// <summary>SHA-256 of the file, hexadecimal.</summary>
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// manifest.json: what a package is and holds. It is signed (manifest.sig, HMAC-SHA256 with the station's key), and it
/// holds the SHA-256 of every other file, so the signature covers the whole package.
/// </summary>
public sealed class PackageManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    /// <summary>The package's id, a version 7 GUID; a package applied once is never applied again.</summary>
    public Guid BatchId { get; set; }
    public SyncDirection Kind { get; set; }
    public Guid StoreId { get; set; }
    public int StationNumber { get; set; }
    public string? AppVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Upload: rows changed after this time (UTC); null when the package holds everything.</summary>
    public DateTime? ChangedAfterUtc { get; set; }
    /// <summary>
    /// Upload: rows changed up to this time are in this package or an earlier one. A series sets it on its last package
    /// only: until that one is sent, the earlier ones do not hold everything.
    /// </summary>
    public DateTime? PackagedThroughUtc { get; set; }
    /// <summary>
    /// Upload: the series this package belongs to. A till's changes are split into packages of a bounded number of rows;
    /// they share the series id, and each has its own <see cref="BatchId"/>. Null for a package written whole.
    /// </summary>
    public Guid? SeriesId { get; set; }
    /// <summary>The package's place in its series, from 1.</summary>
    public int Part { get; set; } = 1;
    /// <summary>The package before this one in its series: it must have been applied first (parents come before children).</summary>
    public Guid? PreviousBatchId { get; set; }
    /// <summary>False for every package of a series but the last.</summary>
    public bool LastPart { get; set; } = true;
    /// <summary>Upload: the server's change number the till had received when it wrote the package. Download: where the package starts.</summary>
    public long Cursor { get; set; }
    /// <summary>Download: the server's change number the package goes up to; the till's next download starts there.</summary>
    public long? CursorTo { get; set; }
    public List<PackageEntry> Tables { get; set; } = [];
    public PackageEntry? Tombstones { get; set; }
    /// <summary>A copy of the till's whole database file, when one was asked for.</summary>
    public PackageEntry? Database { get; set; }

    public int Rows => Tables.Sum(t => t.Rows) + (Tombstones?.Rows ?? 0);
}

/// <summary>A deletion in a package.</summary>
public sealed record TombstoneRow(string EntityName, Guid RowId, DateTime DeletedAtUtc, Guid? StoreId, Guid? MergedIntoId);

/// <summary>A package that cannot be applied: damaged, changed after it was signed, or not meant for this database.</summary>
public sealed class PackageRejectedException(string message) : Exception(message);

/// <summary>
/// The .smapkg file: a zip holding manifest.json, manifest.sig, tables/&lt;Entity&gt;.json (the rows, with the CLR
/// property names), tombstones.json, and optionally database.sqlite (a copy of the till's database).
/// </summary>
public static class SyncPackage
{
    public const string Extension = ".smapkg";
    public const string ManifestFile = "manifest.json";
    public const string SignatureFile = "manifest.sig";
    public const string TombstonesFile = "tombstones.json";
    public const string DatabaseFile = "database.sqlite";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The name a package is saved under: time, kind, station (and for a till's package its place in the series), so a
    /// folder of them sorts in the order they are to be sent.
    /// </summary>
    public static string FileName(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Kind == SyncDirection.Upload
            ? string.Create(CultureInfo.InvariantCulture, $"{manifest.CreatedAtUtc:yyyyMMdd-HHmmss-fff}-up-{manifest.StationNumber:000}-{manifest.Part:00000}-{manifest.BatchId:N}{Extension}")
            : string.Create(CultureInfo.InvariantCulture, $"{manifest.CreatedAtUtc:yyyyMMdd-HHmmss}-down-{manifest.StationNumber:000}-{manifest.BatchId:N}{Extension}");
    }

    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string Sha256(Stream data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>HMAC-SHA256 of the manifest with the station's key, as Base64.</summary>
    public static string Sign(byte[] manifest, byte[] key) => Convert.ToBase64String(HMACSHA256.HashData(key, manifest));
}

/// <summary>Writes a package: the files first, the signed manifest last.</summary>
public sealed class PackageBuilder : IDisposable
{
    private readonly ZipArchive _zip;

    public PackageBuilder(Stream output, PackageManifest manifest)
    {
        _zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Manifest = manifest;
    }

    public PackageManifest Manifest { get; }

    /// <summary>
    /// Starts a file of rows written one at a time, straight into the package (compressed, hashed and counted on the way),
    /// so a table of any size takes no more memory than one row. One file is open at a time.
    /// </summary>
    public RowsWriter BeginRows(string entity, string file) => new(entity, file, _zip.CreateEntry(file, CompressionLevel.Optimal).Open());

    /// <summary>Adds a file from disk (the database copy) and records it in the manifest.</summary>
    public async Task<PackageEntry> AddFileAsync(string entity, string file, string path, CancellationToken cancellationToken)
    {
        var entry = _zip.CreateEntry(file, CompressionLevel.Optimal);
        string sha;
        long length;
        await using (var source = File.OpenRead(path))
        {
            length = source.Length;
            sha = SyncPackage.Sha256(source);
            source.Position = 0;
            await using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken);
        }
        return new PackageEntry { Entity = entity, File = file, Rows = 0, Bytes = length, Sha256 = sha };
    }

    /// <summary>Writes the manifest and its signature and closes the zip.</summary>
    public void Finish(byte[] key)
    {
        var manifest = JsonSerializer.SerializeToUtf8Bytes(Manifest, SyncPackage.Json);
        using (var stream = _zip.CreateEntry(SyncPackage.ManifestFile, CompressionLevel.Optimal).Open()) stream.Write(manifest);
        using (var stream = _zip.CreateEntry(SyncPackage.SignatureFile, CompressionLevel.NoCompression).Open())
            stream.Write(Encoding.ASCII.GetBytes(SyncPackage.Sign(manifest, key)));
        _zip.Dispose();
    }

    public void Dispose() => _zip.Dispose();
}

/// <summary>One file of rows being written into a package: a JSON array, one row at a time.</summary>
public sealed class RowsWriter : IDisposable
{
    private readonly Stream _entry;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly CountingStream _counted;
    private readonly Utf8JsonWriter _json;
    private bool _disposed;

    internal RowsWriter(string entity, string file, Stream entry)
    {
        Entity = entity;
        File = file;
        _entry = entry;
        _counted = new CountingStream(entry, _hash);
        _json = new Utf8JsonWriter(_counted);
        _json.WriteStartArray();
    }

    public string Entity { get; }
    public string File { get; }
    public int Rows { get; private set; }

    /// <summary>Writes one row (a JSON object or value) with <paramref name="write"/>.</summary>
    public void Write(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        write(_json);
        Rows++;
        if (_json.BytesPending > 32 * 1024) _json.Flush();
    }

    /// <summary>Closes the array and the file, and says what the manifest records about it.</summary>
    public PackageEntry Finish()
    {
        _json.WriteEndArray();
        _json.Flush();
        var entry = new PackageEntry { Entity = Entity, File = File, Rows = Rows, Bytes = _counted.Length, Sha256 = Convert.ToHexStringLower(_hash.GetHashAndReset()) };
        Dispose();
        return entry;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _json.Dispose();
        _entry.Dispose();
        _hash.Dispose();
    }

    /// <summary>Passes writes on, hashing and counting them.</summary>
    private sealed class CountingStream(Stream inner, IncrementalHash hash) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            hash.AppendData(buffer);
            _written += buffer.Length;
            inner.Write(buffer);
        }
    }
}

/// <summary>Reads a package. Nothing in it is trusted until <see cref="Verify"/> has passed.</summary>
public sealed class PackageReader : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly byte[] _manifestBytes;
    private readonly string _signature;

    private PackageReader(ZipArchive zip, byte[] manifestBytes, string signature, PackageManifest manifest)
    {
        _zip = zip;
        _manifestBytes = manifestBytes;
        _signature = signature;
        Manifest = manifest;
    }

    /// <summary>What the package says it is; not to be trusted before <see cref="Verify"/>.</summary>
    public PackageManifest Manifest { get; }

    /// <summary>Opens a package; throws <see cref="PackageRejectedException"/> when it is not one.</summary>
    public static PackageReader Open(Stream package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ZipArchive? zip = null;
        try
        {
            zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var manifestBytes = ReadAll(zip.GetEntry(SyncPackage.ManifestFile) ?? throw new PackageRejectedException("The package has no manifest."));
            var signature = Encoding.ASCII.GetString(ReadAll(zip.GetEntry(SyncPackage.SignatureFile) ?? throw new PackageRejectedException("The package is not signed."))).Trim();
            var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, SyncPackage.Json)
                           ?? throw new PackageRejectedException("The package manifest is empty.");
            var reader = new PackageReader(zip, manifestBytes, signature, manifest);
            zip = null;
            return reader;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            throw new PackageRejectedException("The file is not a sync package or is damaged: " + ex.Message);
        }
        finally
        {
            zip?.Dispose();
        }
    }

    /// <summary>
    /// Checks the signature with the station's key, then every file against the manifest (size, SHA-256, row count), so
    /// a package changed after it was written, or signed with another key, is refused.
    /// </summary>
    public void Verify(byte[] key)
    {
        var expected = Convert.FromBase64String(SyncPackage.Sign(_manifestBytes, key));
        byte[] actual;
        try
        {
            actual = Convert.FromBase64String(_signature);
        }
        catch (FormatException)
        {
            throw new PackageRejectedException("The package signature is not readable.");
        }
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new PackageRejectedException("The package signature does not match this station's key: it was changed after it was written, or comes from another station.");
        if (Manifest.Format != PackageManifest.CurrentFormat)
            throw new PackageRejectedException($"The package is in format {Manifest.Format}; this program reads format {PackageManifest.CurrentFormat}. Update the program.");
        var listed = new HashSet<string>(StringComparer.Ordinal) { SyncPackage.ManifestFile, SyncPackage.SignatureFile };
        foreach (var entry in Manifest.Tables.Append(Manifest.Tombstones).Append(Manifest.Database).OfType<PackageEntry>())
        {
            listed.Add(entry.File);
            var file = _zip.GetEntry(entry.File) ?? throw new PackageRejectedException($"The package lacks {entry.File}.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            using (var stream = file.Open())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    length += read;
                }
            }
            if (length != entry.Bytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != entry.Sha256)
                throw new PackageRejectedException($"{entry.File} in the package was changed or damaged (its checksum does not match).");
            if (entry.File != Manifest.Database?.File)
            {
                int rows;
                using (var json = file.Open()) rows = CountRows(json);
                if (rows != entry.Rows) throw new PackageRejectedException($"{entry.File} holds {rows} rows; the manifest says {entry.Rows}.");
            }
        }
        if (_zip.Entries.FirstOrDefault(e => !listed.Contains(e.FullName)) is { } extra)
            throw new PackageRejectedException($"The package holds a file the manifest does not list: {extra.FullName}.");
        Verified = true;
    }

    public bool Verified { get; private set; }

    /// <summary>The bytes of one file of the package.</summary>
    public byte[] ReadBytes(string file) => ReadAll(_zip.GetEntry(file) ?? throw new PackageRejectedException($"The package lacks {file}."));

    /// <summary>The rows of one table, as JSON objects, all at once (for small tables).</summary>
    public JsonDocument ReadTable(PackageEntry entry) => JsonDocument.Parse(ReadBytes(entry.File));

    /// <summary>
    /// The rows of one table a few at a time, read as the file is decompressed, so a table of any size needs memory for
    /// <paramref name="rowsAtATime"/> rows only.
    /// </summary>
    public async IAsyncEnumerable<List<JsonElement>> ReadRowsAsync(PackageEntry entry, int rowsAtATime, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowsAtATime, 1);
        var file = _zip.GetEntry(entry.File) ?? throw new PackageRejectedException($"The package lacks {entry.File}.");
        await using var stream = file.Open();
        var chunk = new List<JsonElement>(Math.Min(rowsAtATime, 4096));
        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        {
            chunk.Add(row);
            if (chunk.Count < rowsAtATime) continue;
            yield return chunk;
            chunk = new List<JsonElement>(Math.Min(rowsAtATime, 4096));
        }
        if (chunk.Count > 0) yield return chunk;
    }

    public IReadOnlyList<TombstoneRow> ReadTombstones() =>
        Manifest.Tombstones is { } entry ? JsonSerializer.Deserialize<List<TombstoneRow>>(ReadBytes(entry.File), SyncPackage.Json) ?? [] : [];

    /// <summary>Copies the database copy out of the package; false when it has none.</summary>
    public async Task<bool> ExtractDatabaseAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Manifest.Database is not { } entry || _zip.GetEntry(entry.File) is not { } file) return false;
        await using var source = file.Open();
        await using var target = File.Create(path);
        await source.CopyToAsync(target, cancellationToken);
        return true;
    }

    /// <summary>The elements of the JSON array in <paramref name="json"/>, counted as it is read (never all in memory).</summary>
    internal static int CountRows(Stream json)
    {
        var buffer = new byte[64 * 1024];
        int filled = 0, count = 0;
        bool final = false, started = false;
        var state = new JsonReaderState();
        try
        {
            while (true)
            {
                if (!final)
                {
                    int read = json.Read(buffer, filled, buffer.Length - filled);
                    if (read == 0) final = true;
                    filled += read;
                }
                var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), final, state);
                while (reader.Read())
                {
                    if (!started)
                    {
                        if (reader.TokenType != JsonTokenType.StartArray) throw new PackageRejectedException("A file of rows in the package is not a list.");
                        started = true;
                    }
                    else if (reader.CurrentDepth == 1 && reader.TokenType is not (JsonTokenType.EndObject or JsonTokenType.EndArray)) count++;
                }
                if (final) break;
                int used = (int)reader.BytesConsumed;
                state = reader.CurrentState;
                if (used == 0 && filled == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);   // one value larger than the buffer
                else
                {
                    Buffer.BlockCopy(buffer, used, buffer, 0, filled - used);
                    filled -= used;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new PackageRejectedException("A file of rows in the package is not readable: " + ex.Message);
        }
        return started ? count : throw new PackageRejectedException("A file of rows in the package is empty.");
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void Dispose() => _zip.Dispose();
}

/// <summary>A station's signing key: 32 random bytes, kept as Base64.</summary>
public static class StationKey
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static byte[] Parse(string key)
    {
        try
        {
            var bytes = Convert.FromBase64String(key);
            if (bytes.Length >= 16) return bytes;
        }
        catch (FormatException)
        {
        }
        throw new PackageRejectedException("The station key is not valid.");
    }
}
