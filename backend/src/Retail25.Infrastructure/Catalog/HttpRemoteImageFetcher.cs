using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Infrastructure.Catalog;

/// <summary>
/// Fetches a picture named by an imported file, refusing everything that is not plainly a picture
/// on the public internet.
/// <para>
/// The address is supplied by whoever wrote the CSV, which makes this a server-side request forgery
/// surface: left unguarded it lets an uploaded file point the server at its own network and report
/// back what it found. On this deployment the API sits beside the database and the shop's LAN, so
/// "fetch this URL" would otherwise be a way to reach both from outside.
/// </para>
/// <para>
/// Five guards, each closing a different door:
/// </para>
/// <list type="bullet">
/// <item>Only <c>http</c> and <c>https</c>. Nothing reaches <c>file://</c>, <c>ftp://</c> or the
/// cloud metadata schemes.</item>
/// <item>Every address the host resolves to must be public. Not just the first — a name that
/// resolves to one public and one private address must not slip through on the public one.</item>
/// <item>Redirects are followed by hand, at most three, revalidating the destination each time.
/// Automatic redirects are the standard bypass: a public URL answering 302 to 169.254.169.254.</item>
/// <item>The response must declare an image content type this system stores.</item>
/// <item>The body is read through a cap, so a URL answering an endless stream cannot exhaust
/// memory, and the whole attempt is bounded by a timeout.</item>
/// </list>
/// </summary>
public sealed class HttpRemoteImageFetcher : IRemoteImageFetcher
{
    public static readonly Error NotAUrl = new("image.url_invalid", "That is not a web address.");
    public static readonly Error SchemeRefused = new("image.url_scheme", "Only http and https addresses are fetched.");
    public static readonly Error AddressRefused = new("image.url_private", "That address is on a private network and was not fetched.");
    public static readonly Error Unreachable = new("image.url_unreachable", "The address could not be reached.");
    public static readonly Error NotAnImage = new("image.url_not_an_image", "What came back was not a picture.");
    public static readonly Error TooLarge = new("image.url_too_large", "The picture is larger than this system stores.");

    private const int MaximumRedirects = 3;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/webp",
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpRemoteImageFetcher> _logger;

    public HttpRemoteImageFetcher(HttpClient http, ILogger<HttpRemoteImageFetcher> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<RemoteImage>> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var target))
        {
            return Result.Failure<RemoteImage>(NotAUrl.With("url", url ?? string.Empty));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            return await FollowAsync(target, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<RemoteImage>(Unreachable.With("url", target.ToString()));
        }
        catch (HttpRequestException ex)
        {
            // Logged rather than surfaced: the caller gets a stable code, and the detail of why a
            // stranger's web server refused us belongs in the log, not on a shopkeeper's screen.
            _logger.LogDebug(ex, "Could not fetch product image from {Url}", target);

            return Result.Failure<RemoteImage>(Unreachable.With("url", target.ToString()));
        }
    }

    private async Task<Result<RemoteImage>> FollowAsync(Uri target, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaximumRedirects; hop++)
        {
            var allowed = await IsPubliclyRoutableAsync(target, ct);
            if (allowed.IsFailure)
            {
                return Result.Failure<RemoteImage>(allowed.Error);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            using (response)
            {
                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    // Revalidated on the next turn of the loop, which is the whole point of doing
                    // this by hand.
                    target = location.IsAbsoluteUri ? location : new Uri(target, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<RemoteImage>(
                        Unreachable.With("url", target.ToString()).With("status", (int)response.StatusCode));
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                if (!AllowedTypes.Contains(contentType))
                {
                    return Result.Failure<RemoteImage>(NotAnImage.With("contentType", contentType));
                }

                // Trusted only as an early exit. A server may understate or omit it, so the read
                // below is capped regardless.
                if (response.Content.Headers.ContentLength > ProductImage.MaximumBytes)
                {
                    return Result.Failure<RemoteImage>(TooLarge);
                }

                var content = await ReadCappedAsync(response, ct);

                return content is null
                    ? Result.Failure<RemoteImage>(TooLarge)
                    : Result.Success(new RemoteImage(content, Normalise(contentType)));
            }
        }

        return Result.Failure<RemoteImage>(Unreachable.With("url", target.ToString()).With("reason", "too many redirects"));
    }

    /// <summary>Reads at most one byte past the limit, so exceeding it is detectable without holding it.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > ProductImage.MaximumBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Whether every address this host resolves to is on the public internet.
    /// <para>
    /// Every one, not the first: a hostname under the file author's control can answer with both a
    /// public address and a private one, and checking only the first is the bypass that makes the
    /// check theatre. A literal IP in the URL is checked the same way, since that is the simplest
    /// version of the same attack.
    /// </para>
    /// </summary>
    private async Task<Result> IsPubliclyRoutableAsync(Uri target, CancellationToken ct)
    {
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure(SchemeRefused.With("scheme", target.Scheme));
        }

        IPAddress[] addresses;

        try
        {
            addresses = IPAddress.TryParse(target.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(target.Host, ct);
        }
        catch (SocketException)
        {
            return Result.Failure(Unreachable.With("host", target.Host));
        }

        if (addresses.Length == 0)
        {
            return Result.Failure(Unreachable.With("host", target.Host));
        }

        foreach (var address in addresses)
        {
            if (!IsPublic(address))
            {
                _logger.LogWarning(
                    "Refused a product image URL resolving to the non-public address {Address}: {Host}",
                    address,
                    target.Host);

                return Result.Failure(AddressRefused.With("host", target.Host));
            }
        }

        return Result.Success();
    }

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Mapped v4 is checked as v4: ::ffff:127.0.0.1 is loopback however it is spelled.
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }

            return !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && !address.IsIPv6Multicast
                && !IsUniqueLocal(address)
                && !address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            0 => false,                                   // this network
            10 => false,                                  // private
            127 => false,                                 // loopback
            169 when octets[1] == 254 => false,            // link-local, and the cloud metadata address
            172 when octets[1] >= 16 && octets[1] <= 31 => false,
            192 when octets[1] == 168 => false,
            100 when octets[1] >= 64 && octets[1] <= 127 => false, // carrier-grade NAT
            >= 224 => false,                              // multicast and reserved
            _ => true,
        };
    }

    /// <summary>fc00::/7 — the v6 equivalent of a private range.</summary>
    private static bool IsUniqueLocal(IPAddress address) => (address.GetAddressBytes()[0] & 0xFE) == 0xFC;

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>image/jpg is not a real media type, but plenty of servers send it.</summary>
    private static string Normalise(string contentType)
        => contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : contentType.ToLowerInvariant();
}
