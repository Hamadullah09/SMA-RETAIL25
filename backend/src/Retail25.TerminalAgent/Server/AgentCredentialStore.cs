using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Retail25.TerminalAgent.Server;

/// <summary>What this machine was given when it enrolled.</summary>
public sealed record StoredAgentCredential(string DeviceKey, long DeviceId, long LocationId, string Secret);

/// <summary>
/// Keeps the credential this machine was handed at enrolment.
/// <para>
/// It has to outlive the process, and it has to outlive the enrolment code, because that code is
/// single-use: an agent that forgot its secret could not simply enrol again — somebody would have to
/// walk to the till with a new code. So this is written to disk on first success and read on every
/// start after it.
/// </para>
/// <para>
/// Under ProgramData rather than beside the binaries: Program Files is not writable by a service
/// account without elevation, and a credential the agent cannot rewrite is one that cannot be
/// rotated.
/// </para>
/// </summary>
public sealed class AgentCredentialStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<AgentCredentialStore> _logger;

    private StoredAgentCredential? _credential;

    public AgentCredentialStore(ILogger<AgentCredentialStore> logger)
    {
        _logger = logger;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Retail25",
            "TerminalAgent");

        _path = Path.Combine(directory, "credentials.json");

        Load();
    }

    public StoredAgentCredential? Current => _credential;

    public bool HasCredential => _credential is { Secret.Length: > 0 };

    public async Task SaveAsync(StoredAgentCredential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credential);

        _credential = credential;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Written to a temporary file and moved into place. A half-written credential file is
            // worse than none: the agent would read a truncated secret, fail to authenticate, and
            // have no code left to enrol with — and a power cut mid-write is exactly the scenario a
            // till is subject to.
            var temporary = _path + ".tmp";

            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(credential, Json), ct);
            File.Move(temporary, _path, overwrite: true);

            _logger.LogInformation("Enrolled as {DeviceKey}; credential stored at {Path}", credential.DeviceKey, _path);
        }
        catch (Exception ex)
        {
            // Kept in memory regardless, so this run works even if the disk does not. The next start
            // will try to enrol again — which fails on a spent code and says so, which is a better
            // outcome than an agent that silently cannot persist anything.
            _logger.LogError(ex, "Enrolled, but could not write the credential to {Path}", _path);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            _credential = JsonSerializer.Deserialize<StoredAgentCredential>(File.ReadAllText(_path), Json);
        }
        catch (Exception ex)
        {
            // Treated as absent rather than fatal. A corrupt file should leave the agent asking to be
            // enrolled, not refusing to start — a till that will not run is worse than one that says
            // it needs a code.
            _logger.LogWarning(ex, "Could not read the stored credential at {Path}; treating this machine as unenrolled", _path);
        }
    }
}
