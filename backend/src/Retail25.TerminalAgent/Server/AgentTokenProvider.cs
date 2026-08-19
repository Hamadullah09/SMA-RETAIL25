using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Retail25.TerminalAgent.Server;

/// <summary>
/// Turns the agent's client secret into an access token the API will actually accept.
/// <para>
/// This closes a real gap. The agent used to present <c>Agent:BootstrapSecret</c> directly as a
/// bearer token, on both the HTTP client and the SignalR connection. The API validates bearer tokens
/// through OpenIddict, which expects a signed token and rejects an opaque string — so every call the
/// agent made was refused, and the symptom was not an authentication error anyone would notice: the
/// agent simply never received its device profile, fell back to the built-in Simulator default, and
/// sat there reading imaginary tags from 127.0.0.1 while the real reader was ignored.
/// </para>
/// <para>
/// The secret is exchanged for a token via <c>client_credentials</c>, which is the grant the
/// <c>retail25-agent</c> client is registered for. Confidential, machine-to-machine, no user
/// involved — a till has no one to prompt at four in the morning.
/// </para>
/// </summary>
public sealed class AgentTokenProvider : IDisposable
{
    /// <summary>
    /// Renew this far before expiry, so a request never leaves with a token that dies in flight.
    /// </summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _factory;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentTokenProvider> _logger;

    // One in-flight request at a time. Without this, a reconnect storm — the agent's HTTP client and
    // its hub connection both discovering an expired token at once — would fire a burst of identical
    // token requests at an API that is very likely already struggling.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    private readonly AgentCredentialStore _credentials;

    public AgentTokenProvider(
        IHttpClientFactory factory,
        IOptions<AgentOptions> options,
        AgentCredentialStore credentials,
        ILogger<AgentTokenProvider> logger)
    {
        _factory = factory;
        _options = options.Value;
        _credentials = credentials;
        _logger = logger;
    }

    /// <summary>
    /// The credential this machine authenticates with.
    /// <para>
    /// What it was given at enrolment wins over what is in its configuration file, and that order is
    /// the migration: a machine installed from a generated package holds no bootstrap secret at all,
    /// while one installed the old way keeps working untouched until somebody re-enrols it. Neither
    /// needs a flag to say which it is.
    /// </para>
    /// <para>
    /// Read each time rather than captured, because enrolment completes after this class is
    /// constructed — a value read once at startup would be the empty one for the life of the process.
    /// </para>
    /// </summary>
    private string Secret => _credentials.Current?.Secret is { Length: > 0 } enrolled
        ? enrolled
        : _options.BootstrapSecret ?? string.Empty;

    /// <summary>
    /// The current access token, fetching or renewing it if needed.
    /// </summary>
    /// <returns>
    /// Null when no secret is configured, or when the server could not be reached. Null rather than
    /// an exception because the caller's answer to "no token" is to spool and retry, not to unwind —
    /// a till whose reader stops because the network blipped is worse than one that quietly queues.
    /// </returns>
    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapSecret))
        {
            return null;
        }

        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
        {
            return _token;
        }

        await _gate.WaitAsync(ct);

        try
        {
            // Re-checked inside the gate: whoever was ahead of us has very likely just renewed it.
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
            {
                return _token;
            }

            return await RequestAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Discards the cached token, so the next call fetches a fresh one. Used on a 401.</summary>
    public void Invalidate() => _token = null;

    private async Task<string?> RequestAsync(CancellationToken ct)
    {
        // The bare client, not the "server" one — that client attaches this very token, and asking it
        // to fetch the token would be circular.
        using var client = _factory.CreateClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "retail25-agent",
            ["client_secret"] = Secret,
            ["scope"] = "retail25.terminal",
        });

        try
        {
            using var response = await client.PostAsync(
                new Uri($"{_options.ApiUrl.TrimEnd('/')}/connect/token"),
                form,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                // The body is logged: for a client-credentials failure it carries `error` and
                // `error_description`, which is the difference between "wrong secret" and "the client
                // was never registered" — and guessing between those costs an afternoon.
                var body = await response.Content.ReadAsStringAsync(ct);

                _logger.LogError(
                    "The server refused the agent's credentials ({Status}). {Body}",
                    (int)response.StatusCode,
                    body);

                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

            if (token?.AccessToken is null)
            {
                _logger.LogError("The server returned a token response with no access token.");
                return null;
            }

            _token = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            _logger.LogInformation("Agent authenticated; token valid for {Seconds}s", token.ExpiresIn);

            return _token;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // The server is not there. Normal on a till that boots before the network does.
            _logger.LogWarning("Could not reach the server to authenticate: {Message}", error.Message);
            return null;
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}

/// <summary>
/// Attaches the agent's token to every outbound call, and retries once on a 401.
/// <para>
/// The retry matters more than it looks. A token can be rejected for reasons the agent cannot see —
/// the API restarted and rotated its signing keys, the clock drifted — and without a retry the till
/// would sit refusing to work until someone restarted it.
/// </para>
/// </summary>
public sealed class AgentAuthHandler : DelegatingHandler
{
    private readonly AgentTokenProvider _tokens;

    public AgentAuthHandler(AgentTokenProvider tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync(cancellationToken);

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _tokens.Invalidate();

        var refreshed = await _tokens.GetAsync(cancellationToken);

        if (refreshed is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", refreshed);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
