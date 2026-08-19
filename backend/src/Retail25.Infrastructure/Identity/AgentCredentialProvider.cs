using Microsoft.Extensions.Configuration;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Reads the agent's connection details from the server's own configuration.
/// <para>
/// The URL comes from <c>Auth:WebOrigin</c> rather than from the request, because an enrolment
/// package is often generated on one machine and installed on another: the address the administrator
/// happens to be browsing from is not necessarily the address a till should call, and a package that
/// worked from the back office and failed on the shop floor would be a miserable thing to debug.
/// </para>
/// </summary>
internal sealed class AgentCredentialProvider : IAgentCredentialProvider
{
    private readonly IConfiguration _configuration;

    public AgentCredentialProvider(IConfiguration configuration) => _configuration = configuration;

    public string ServerUrl =>
        _configuration["Agent:PublicApiUrl"]
        ?? _configuration["Auth:WebOrigin"]?.TrimEnd('/') + "/backend"
        ?? string.Empty;

    /// <summary>
    /// The shared agent secret, for now.
    /// <para>
    /// Every agent uses the same one, which is the weakness enrolment begins to unpick: it cannot be
    /// rotated for a single till, and one compromised machine is every machine. Issuing a per-device
    /// secret is a change behind this property and nowhere else, which is why the seam exists before
    /// the improvement does.
    /// </para>
    /// </summary>
    public string AgentSecret => _configuration["Auth:AgentClientSecret"] ?? string.Empty;
}
