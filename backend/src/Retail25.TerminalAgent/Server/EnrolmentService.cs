using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Retail25.TerminalAgent.Server;

/// <summary>
/// Exchanges the one-time code for this machine's own credential, once.
/// <para>
/// Runs before anything that needs to authenticate. An agent installed from a generated package has
/// a code and no secret; it presents the code, is told which machine it is and what to authenticate
/// with, and writes that down. Every start after the first finds the credential already there and
/// does nothing.
/// </para>
/// <para>
/// The code is spent by a successful redemption, so this deliberately does not retry on a refusal
/// that names the code — expired or already-used means a person must generate another, and hammering
/// the endpoint would neither fix it nor be polite. A network failure is different and is retried,
/// because a till that boots before its switch does is the ordinary case rather than an error.
/// </para>
/// </summary>
public sealed class EnrolmentService : BackgroundService
{
    private static readonly TimeSpan RetryAfterNetworkFailure = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _factory;
    private readonly AgentCredentialStore _store;
    private readonly AgentOptions _options;
    private readonly ILogger<EnrolmentService> _logger;

    public EnrolmentService(
        IHttpClientFactory factory,
        AgentCredentialStore store,
        IOptions<AgentOptions> options,
        ILogger<EnrolmentService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _factory = factory;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_store.HasCredential)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrolmentCode))
        {
            // Nothing to do, and not an error. An agent configured the old way carries a bootstrap
            // secret directly, and saying otherwise on every start would be noise on every till in
            // the estate that has not been migrated.
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var outcome = await TryEnrolAsync(stoppingToken);

            if (outcome != EnrolmentOutcome.NetworkFailure)
            {
                return;
            }

            try
            {
                await Task.Delay(RetryAfterNetworkFailure, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private enum EnrolmentOutcome
    {
        Enrolled,
        Refused,
        NetworkFailure,
    }

    private async Task<EnrolmentOutcome> TryEnrolAsync(CancellationToken ct)
    {
        try
        {
            // The bare client: the "server" one attaches a bearer token, and this call exists
            // precisely because there is nothing yet to get a token with.
            using var client = _factory.CreateClient();

            var url = new Uri($"{_options.ApiUrl.TrimEnd('/')}/api/v1/rfid-topology/enrolments/redeem");

            using var response = await client.PostAsJsonAsync(
                url,
                new
                {
                    enrolmentCode = _options.EnrolmentCode,
                    hostname = Environment.MachineName,
                    operatingSystem = Environment.OSVersion.VersionString,
                    agentVersion = AgentVersion.Current,
                },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                // Named plainly, because the two refusals a person can act on are expired and
                // already-used, and both need somebody to generate another code rather than to wait.
                var detail = await response.Content.ReadAsStringAsync(ct);

                _logger.LogError(
                    "Enrolment was refused ({Status}). Generate a new code from Administration, Settings, RFID. {Detail}",
                    (int)response.StatusCode,
                    detail);

                return EnrolmentOutcome.Refused;
            }

            var result = await response.Content.ReadFromJsonAsync<RedeemResponse>(cancellationToken: ct);

            if (result is null || string.IsNullOrWhiteSpace(result.AgentSecret))
            {
                _logger.LogError("Enrolment succeeded but returned no credential; this agent cannot authenticate");
                return EnrolmentOutcome.Refused;
            }

            await _store.SaveAsync(
                new StoredAgentCredential(result.DeviceKey, result.DeviceId, result.LocationId, result.AgentSecret),
                ct);

            return EnrolmentOutcome.Enrolled;
        }
        catch (OperationCanceledException)
        {
            return EnrolmentOutcome.Refused;
        }
        catch (Exception ex)
        {
            // A till that boots before its switch is the ordinary case, not a fault. Retried, with
            // one line rather than a stack trace, because this will repeat until the network arrives.
            _logger.LogWarning(
                "Could not reach the server to enrol ({Reason}); retrying in {Seconds}s",
                ex.Message,
                RetryAfterNetworkFailure.TotalSeconds);

            return EnrolmentOutcome.NetworkFailure;
        }
    }

    private sealed record RedeemResponse(long DeviceId, string DeviceKey, long LocationId, string AgentSecret);
}
