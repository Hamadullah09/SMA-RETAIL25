using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace RetailPlus.Sync;

/// <summary>A request to join a till to the server with a one-time enrolment code.</summary>
public sealed record EnrollRequest(string Code, string? AppVersion, string? MachineName);

/// <summary>What the server tells a till that joins: its store, its station number, its key and its first package (Base64).</summary>
public sealed record EnrollResponse(Guid StoreId, string StoreCode, string StoreName, int StationNumber, string StationKey, string Package);

/// <summary>What the server did with a package.</summary>
public sealed record UploadResponse(string Status, Guid BatchId, int Inserted, int Updated, int Deleted, IReadOnlyList<string> Notes, string? Error);

/// <summary>The server refused a package for good (damaged, wrong key, a row it cannot take); sending it again will not help.</summary>
public sealed class SyncRefusedException(string message) : Exception(message);

/// <summary>The server does not accept this till (unknown, retired, or its key changed).</summary>
public sealed class SyncAuthException(string message) : Exception(message);

/// <summary>
/// Signing a till's requests: <c>X-Station</c> (number), <c>X-Station-Time</c> (Unix seconds) and
/// <c>X-Station-Signature</c>, an HMAC-SHA256 with the station's key of the method, path and query, time and the
/// SHA-256 of the body. The server accepts a signature for 15 minutes either side of its clock.
/// </summary>
public static class StationAuth
{
    public const string StationHeader = "X-Station";
    public const string TimeHeader = "X-Station-Time";
    public const string SignatureHeader = "X-Station-Signature";
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static string Sign(byte[] key, string method, string pathAndQuery, long unixSeconds, string bodySha256) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
            $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{unixSeconds.ToString(CultureInfo.InvariantCulture)}\n{bodySha256}")));

    public static bool Verify(byte[] key, string method, string pathAndQuery, long unixSeconds, string bodySha256, string signature)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Sign(key, method, pathAndQuery, unixSeconds, bodySha256)), Convert.FromBase64String(signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string EmptyBodySha256 { get; } = SyncPackage.Sha256(ReadOnlySpan<byte>.Empty);
}

/// <summary>The till's side of the server's sync API.</summary>
public sealed class SyncClient(HttpClient http)
{
    public HttpClient Http { get; } = http;

    /// <summary>Joins this till to the server with an enrolment code (no key needed yet).</summary>
    public async Task<EnrollResponse> EnrollAsync(string code, string? appVersion, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(new Uri("api/sync/enroll", UriKind.Relative), new EnrollRequest(code.Trim(), appVersion, Environment.MachineName), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Gone)
            throw new SyncRefusedException(await ErrorAsync(response, "The enrolment code is not valid or has expired. Ask for a new one."));
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<EnrollResponse>(cancellationToken) ?? throw new SyncRefusedException("The server sent no answer.");
    }

    /// <summary>Sends one package file.</summary>
    public async Task<UploadResponse> UploadAsync(int station, byte[] key, string packageFile, CancellationToken cancellationToken = default)
    {
        var body = await File.ReadAllBytesAsync(packageFile, cancellationToken);
        using var request = Signed(HttpMethod.Post, "api/sync/upload", station, key, body);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            throw new SyncRefusedException(await ErrorAsync(response, "The server refused the package."));
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken) ?? throw new SyncRefusedException("The server sent no answer.");
    }

    /// <summary>Fetches the package of what changed on the server after <paramref name="cursor"/> into a file.</summary>
    public async Task DownloadAsync(int station, byte[] key, long cursor, string targetFile, CancellationToken cancellationToken = default)
    {
        using var request = Signed(HttpMethod.Get, "api/sync/download?cursor=" + cursor.ToString(CultureInfo.InvariantCulture), station, key, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureAsync(response);
        await using var target = File.Create(targetFile);
        await response.Content.CopyToAsync(target, cancellationToken);
    }

    private HttpRequestMessage Signed(HttpMethod method, string relative, int station, byte[] key, byte[]? body)
    {
        var uri = new Uri(Http.BaseAddress ?? throw new InvalidOperationException("No server address."), relative);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(StationAuth.StationHeader, station.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(StationAuth.TimeHeader, now.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(StationAuth.SignatureHeader,
            StationAuth.Sign(key, method.Method, uri.PathAndQuery, now, body is null ? StationAuth.EmptyBodySha256 : SyncPackage.Sha256(body)));
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SyncAuthException(await ErrorAsync(response, "The server does not accept this till. Join it again with a new enrolment code."));
        throw new HttpRequestException(await ErrorAsync(response, $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}."), null, response.StatusCode);
    }

    /// <summary>The server's error text (problem details "detail", or "error"), or the fallback.</summary>
    private static async Task<string> ErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            foreach (var name in new[] { "error", "detail", "title" })
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } message)
                    return message;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return fallback;
    }
}
