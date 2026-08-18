using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Retail25.Infrastructure.Catalog;
using Xunit;

namespace Retail25.IntegrationTests.Catalog;

/// <summary>
/// The address guard on imported picture links.
/// <para>
/// This is the security-relevant half of the CSV import: the URL comes out of a file somebody
/// uploaded, so whoever wrote the file chooses where this server sends a request. On this
/// deployment the API sits beside the database and, on-premise, beside the shop's LAN — so an
/// unguarded fetch is a way to reach both from outside.
/// </para>
/// <para>
/// No network is touched by any of these: every case is refused before a socket is opened, which is
/// exactly the property being pinned. A test that had to reach the internet to prove a request was
/// refused would be proving the wrong thing.
/// </para>
/// </summary>
public sealed class RemoteImageFetcherTests
{
    private static HttpRemoteImageFetcher Fetcher()
        => new(
            new HttpClient(new RefusingHandler()),
            NullLogger<HttpRemoteImageFetcher>.Instance);

    [Theory]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("gopher://example.com/a.png")]
    public async Task Only_http_and_https_are_fetched(string url)
    {
        var result = await Fetcher().FetchAsync(url, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("image.url_scheme");
    }

    /// <summary>
    /// The addresses that make this an SSRF surface rather than a download. 169.254.169.254 is the
    /// cloud metadata endpoint and is the single most valuable target of the set.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1/a.png")]
    [InlineData("http://localhost/a.png")]
    [InlineData("http://10.0.0.5/a.png")]
    [InlineData("http://192.168.0.178/a.png")]
    [InlineData("http://172.16.4.4/a.png")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/a.png")]
    [InlineData("http://100.64.0.1/a.png")]
    public async Task A_private_or_loopback_address_is_refused(string url)
    {
        var result = await Fetcher().FetchAsync(url, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("image.url_private");
    }

    /// <summary>Loopback spelled as a mapped v6 address is still loopback.</summary>
    [Fact]
    public async Task Loopback_written_as_mapped_ipv6_is_refused()
    {
        var result = await Fetcher().FetchAsync("http://[::ffff:127.0.0.1]/a.png", default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("image.url_private");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public async Task Something_that_is_not_an_address_is_refused_rather_than_guessed_at(string url)
    {
        var result = await Fetcher().FetchAsync(url, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("image.url_invalid");
    }

    /// <summary>
    /// A bare path is refused, but which guard catches it depends on the platform, so the assertion
    /// is on the refusal rather than on the wording.
    /// <para>
    /// On Windows <c>/images/a.png</c> is not an absolute URI and never parses. On Linux it does —
    /// as <c>file:///images/a.png</c> — and is stopped one step later by the scheme check. This test
    /// originally demanded the first code and passed locally while failing in CI, which is the test
    /// being wrong rather than the code: both paths refuse it, and both refuse it before a socket is
    /// opened, which is the property that matters.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_bare_path_is_refused_however_the_platform_parses_it()
    {
        var result = await Fetcher().FetchAsync("/images/a.png", default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("image.url_invalid", "image.url_scheme");
    }

    /// <summary>
    /// Fails the test if a request ever leaves. Every case above must be refused by the address
    /// check, before any socket is opened.
    /// </summary>
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException(
                $"A request was sent to {request.RequestUri}. It should have been refused before this point.");
    }
}
