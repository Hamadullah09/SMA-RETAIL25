using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RetailPlus.Core.Model;

namespace RetailPlus.Rfid;

/// <summary>A store's readers as the SMA RETAIL server sends them to an RFID hub (<c>GET /api/rfid/readers</c>).</summary>
public sealed record RfidServerReaderList(Guid StoreId, string StoreCode, string StoreName, string? Hub, IReadOnlyList<RfidReader> Readers);

/// <summary>
/// The reader list of an RFID hub that runs from the SMA RETAIL server instead of a database: the readers and antennas
/// the store's tills set up and sent to the server, read with a hub key the administrator made for the store. The last
/// list is kept in a file, so a hub that starts while the server cannot be reached still serves the readers it had.
/// </summary>
public sealed class RfidServerReaders : IDisposable
{
    /// <summary>The header the key goes in.</summary>
    public const string KeyHeader = "X-RfidHub-Key";

    /// <summary>The server's JSON: camelCase names, enums as their names.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Where the list is kept when no file is given: RetailPlus\RfidHub\readers.json in the local application data of the account the hub runs as.</summary>
    public static string DefaultSavedListFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailPlus", "RfidHub", "readers.json");

    private readonly HttpClient _http;
    private readonly string _hub;
    private readonly string? _savedListFile;
    /// <summary>A fingerprint of the server, hub and key: a kept list is used only by the hub that kept it.</summary>
    private readonly string _source;
    private byte[]? _lastSaved;
    private bool _loaded;

    /// <param name="serverUrl">The server's address, e.g. https://pos.example.com/.</param>
    /// <param name="key">The store's hub key (<c>rfh_…</c>).</param>
    /// <param name="hub">Which readers to serve: those whose hub name is this (the reader's <see cref="RfidReader.HubKey"/>).</param>
    /// <param name="savedListFile">Where to keep the last list; null keeps none.</param>
    /// <param name="handler">The HTTP handler to use (the tests' in-process server); null for the network.</param>
    public RfidServerReaders(string serverUrl, string key, string hub = "main", string? savedListFile = null, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        BaseAddress = new Uri(serverUrl.EndsWith('/') ? serverUrl : serverUrl + "/");
        _hub = string.IsNullOrWhiteSpace(hub) ? "main" : hub.Trim();
        _savedListFile = savedListFile;
        _source = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{BaseAddress}\n{_hub}\n{key.Trim()}")));
        _http = handler is null ? new HttpClient() : new HttpClient(handler, false);
        _http.BaseAddress = BaseAddress;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add(KeyHeader, key.Trim());
    }

    public Uri BaseAddress { get; }

    /// <summary>Diagnostic messages (the server not reached, the list not kept); the hub service writes them to its log.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>The store whose readers these are, once the server (or the kept list) said so.</summary>
    public string? StoreName { get; private set; }

    /// <summary>
    /// The store's readers for this hub, from the server. When the server cannot be reached before any list was read, the
    /// kept list is used; after that a failure is thrown, and the hub goes on with the readers it has. A key the server
    /// refuses (wrong, or revoked) throws, and the kept list is deleted.
    /// </summary>
    public async Task<IReadOnlyList<RfidReader>> LoadAsync(CancellationToken cancellationToken = default)
    {
        RfidServerReaderList list;
        try
        {
            list = await FetchAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            if (_loaded || ReadSaved() is not { } saved) throw;
            Log?.Invoke($"The server could not be reached ({ex.Message}); using the reader list kept at "
                        + saved.SavedAtUtc.ToString("u", CultureInfo.InvariantCulture) + ".");
            _loaded = true;
            StoreName = saved.List.StoreName;
            return saved.List.Readers;
        }
        _loaded = true;
        StoreName = list.StoreName;
        Save(list);
        return list.Readers;
    }

    private async Task<RfidServerReaderList> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(new Uri("api/rfid/readers?hub=" + Uri.EscapeDataString(_hub), UriKind.Relative), cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Forget();
            throw new RfidException("The server does not accept this hub's key: it is wrong or was revoked. Make a new key for the store and start the hub with it.");
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<RfidServerReaderList>(Json, cancellationToken);
        return list is { Readers: not null } ? list : throw new JsonException("The server sent no reader list.");
    }

    // The kept list ----------------------------------------------------------------------------------------------

    private sealed record Saved(string Source, DateTime SavedAtUtc, RfidServerReaderList List);

    private void Save(RfidServerReaderList list)
    {
        if (_savedListFile is null) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(list, Json);
        if (_lastSaved is not null && bytes.AsSpan().SequenceEqual(_lastSaved)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_savedListFile))!);
            var temporary = _savedListFile + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Saved(_source, DateTime.UtcNow, list), Json));
            File.Move(temporary, _savedListFile, overwrite: true);
            _lastSaved = bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke($"The reader list could not be kept in {_savedListFile}: {ex.Message}");
        }
    }

    private Saved? ReadSaved()
    {
        if (_savedListFile is null || !File.Exists(_savedListFile)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllBytes(_savedListFile), Json);
            return saved is { List.Readers: not null } && saved.Source == _source ? saved : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Forget()
    {
        _lastSaved = null;
        if (_savedListFile is null) return;
        try
        {
            File.Delete(_savedListFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke($"The kept reader list {_savedListFile} could not be deleted: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}
