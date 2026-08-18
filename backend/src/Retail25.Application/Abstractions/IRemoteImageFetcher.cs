using Retail25.Domain.Common;

namespace Retail25.Application.Abstractions;

/// <summary>A picture that has been fetched and found to be a picture.</summary>
public sealed record RemoteImage(byte[] Content, string ContentType);

/// <summary>
/// Fetches a product picture named by a URL in an imported file.
/// <para>
/// A port rather than an <c>HttpClient</c> here, for the usual reason — Application names no
/// transport — but also because the interesting part of this is not the fetching. It is refusing to
/// fetch. The address comes out of a file somebody uploaded, so it is the file's author, not the
/// shop, who decides where this server sends a request. Everything that makes that safe lives in
/// the Infrastructure implementation and is testable in isolation because of this seam.
/// </para>
/// </summary>
public interface IRemoteImageFetcher
{
    /// <summary>
    /// Returns the image, or a failure naming why it was refused. Never throws for an unreachable
    /// or hostile address: an import of a thousand rows must not stop because one shop's CDN is
    /// down, and the row's problem is reported alongside the rest.
    /// </summary>
    Task<Result<RemoteImage>> FetchAsync(string url, CancellationToken ct);
}
