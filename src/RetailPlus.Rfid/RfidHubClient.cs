using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace RetailPlus.Rfid;

/// <summary>
/// Talks to an RFID hub service on another computer (RetailPlus.RfidHub) over HTTP. Tag reads arrive as a
/// server-sent event stream that reconnects by itself, so a hub restart only pauses the feed.
/// </summary>
public sealed class RfidHubClient : IRfidControl, IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly HttpClient _stream;

    public Uri BaseAddress { get; }

    public RfidHubClient(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        _http = handler is null ? new HttpClient() : new HttpClient(handler, false);
        _http.BaseAddress = BaseAddress;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _stream = handler is null ? new HttpClient() : new HttpClient(handler, false);
        _stream.BaseAddress = BaseAddress;
        _stream.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Add("X-SMA-Key", apiKey);
            _stream.DefaultRequestHeaders.Add("X-SMA-Key", apiKey);
        }
    }

    public IAsyncEnumerable<RfidTagEvent> ListenAsync(int stationNumber, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RfidTagEvent>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _ = Task.Run(() => PumpAsync(stationNumber, channel.Writer, cancellationToken), CancellationToken.None);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>Raised when the event stream connects or drops (true = connected).</summary>
    public event Action<bool>? StreamStateChanged;

    private async Task PumpAsync(int station, ChannelWriter<RfidTagEvent> writer, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await _stream.GetAsync($"stations/{station}/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                StreamStateChanged?.Invoke(true);
                backoff = TimeSpan.FromSeconds(1);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null) break;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var e = JsonSerializer.Deserialize<RfidTagEvent>(line.AsSpan(5).Trim(), Json);
                    if (e is not null) await writer.WriteAsync(e, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
            }
            StreamStateChanged?.Invoke(false);
            try
            {
                await Task.Delay(backoff, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            backoff = TimeSpan.FromSeconds(Math.Min(15, backoff.TotalSeconds * 2));
        }
        writer.TryComplete();
    }

    public async Task<IReadOnlyList<ReaderStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<List<ReaderStatus>>("readers", Json, cancellationToken) ?? [];

    public async Task ReloadAsync(CancellationToken cancellationToken = default) =>
        await PostAsync("reload", cancellationToken);

    public async Task<ReaderInfo> ReadInfoAsync(int readerId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"readers/{readerId}/info", cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ReaderInfo>(Json, cancellationToken) ?? new ReaderInfo();
    }

    public async Task<IReadOnlyList<string>> ApplySettingsAsync(int readerId, CancellationToken cancellationToken = default)
    {
        using var response = await PostAsync($"readers/{readerId}/apply", cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<string>>(Json, cancellationToken) ?? [];
    }

    public async Task ResetAsync(int readerId, CancellationToken cancellationToken = default) =>
        (await PostAsync($"readers/{readerId}/reset", cancellationToken)).Dispose();

    public async Task<int> MeasureReturnLossAsync(int readerId, int antenna, int frequencyIndex, CancellationToken cancellationToken = default)
    {
        using var response = await PostAsync($"readers/{readerId}/return-loss?antenna={antenna}&frequency={frequencyIndex}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<int>(Json, cancellationToken);
    }

    public async Task WriteGpioAsync(int readerId, int gpio, bool high, CancellationToken cancellationToken = default) =>
        (await PostAsync($"readers/{readerId}/gpio?gpio={gpio}&high={high}", cancellationToken)).Dispose();

    private async Task<HttpResponseMessage> PostAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsync(path, null, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return response;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        throw new RfidException(string.IsNullOrWhiteSpace(text) ? $"The RFID hub answered {(int)response.StatusCode}." : text.Trim('"'));
    }

    public void Dispose()
    {
        _http.Dispose();
        _stream.Dispose();
    }
}
