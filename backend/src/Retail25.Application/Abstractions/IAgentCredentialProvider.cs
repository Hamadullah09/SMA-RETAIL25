namespace Retail25.Application.Abstractions;

/// <summary>
/// Where an enrolling agent should connect, and the credential it should use once it has proved
/// which machine it is.
/// <para>
/// A port because both values are deployment facts rather than domain ones — the shop's public URL
/// and a secret held in the server's own configuration — and Application is not allowed to read
/// configuration any more than it is allowed to open a socket.
/// </para>
/// <para>
/// Today the secret is the one every agent shares. Behind this seam it can become one per device
/// without changing a single caller, which is the point of putting a seam here rather than reaching
/// for IConfiguration.
/// </para>
/// </summary>
public interface IAgentCredentialProvider
{
    /// <summary>The base address an agent should talk to, as the outside world sees it.</summary>
    string ServerUrl { get; }

    /// <summary>The credential handed to an agent at enrolment, over TLS, once and never in a file.</summary>
    string AgentSecret { get; }
}
