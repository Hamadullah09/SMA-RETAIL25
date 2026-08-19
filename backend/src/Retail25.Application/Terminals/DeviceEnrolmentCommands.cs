using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Terminals;

/// <summary>
/// What an installer takes to the machine.
/// <para>
/// Everything the agent needs to find the server and prove which machine it is, and nothing that is
/// worth stealing afterwards: the enrolment code expires, works once, and is exchanged for the real
/// credential over TLS at first start. A file that could be emailed without care is the point.
/// </para>
/// </summary>
public sealed record AgentEnrolmentPackage(
    string DeviceKey,
    long LocationId,
    string ServerUrl,
    string EnrolmentCode,
    DateTimeOffset ExpiresAt,
    int HeartbeatSeconds,
    int ReaderRetrySeconds);

[RequiresPermission(PermissionKeys.Settings.Hardware)]
public sealed record GenerateAgentEnrolmentCommand(long LocationId, string DeviceKey, string? Name = null)
    : IRequest<Result<AgentEnrolmentPackage>>;

/// <summary>What the agent gets back once it has proved which machine it is.</summary>
public sealed record EnrolmentResult(long DeviceId, string DeviceKey, long LocationId, string AgentSecret);

/// <summary>
/// An agent presenting its enrolment code for the first time.
/// <para>
/// Deliberately not gated by <c>RequiresPermission</c>: the code is the credential. A machine being
/// installed has nothing else to authenticate with, which is the entire problem enrolment solves.
/// </para>
/// </summary>
public sealed record RedeemAgentEnrolmentCommand(
    string EnrolmentCode,
    string? Hostname,
    string? OperatingSystem,
    string? AgentVersion) : IRequest<Result<EnrolmentResult>>;

public sealed class DeviceEnrolmentHandlers
    : IRequestHandler<GenerateAgentEnrolmentCommand, Result<AgentEnrolmentPackage>>,
      IRequestHandler<RedeemAgentEnrolmentCommand, Result<EnrolmentResult>>
{
    /// <summary>
    /// Long enough to walk to the till, short enough that a code left in an inbox is worthless.
    /// </summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly IAgentCredentialProvider _credentials;

    public DeviceEnrolmentHandlers(
        IApplicationDbContext db,
        IDateTime clock,
        IAgentCredentialProvider credentials)
    {
        _db = db;
        _clock = clock;
        _credentials = credentials;
    }

    public async Task<Result<AgentEnrolmentPackage>> Handle(
        GenerateAgentEnrolmentCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = (request.DeviceKey ?? string.Empty).Trim().ToUpperInvariant();

        if (key.Length == 0)
        {
            return Result.Failure<AgentEnrolmentPackage>(Device.KeyRequired);
        }

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.LocationId == request.LocationId && d.DeviceKey == key, ct);

        if (device is null)
        {
            var created = Device.Create(request.LocationId, key, request.Name);

            if (created.IsFailure)
            {
                return Result.Failure<AgentEnrolmentPackage>(created.Error);
            }

            device = created.Value;
            _db.Devices.Add(device);
            await _db.SaveChangesAsync(ct);
        }

        // 256 bits from a cryptographic source. A code somebody could guess is a code that enrols
        // somebody else's machine into this estate.
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var expiresAt = _clock.Now.Add(ValidFor);

        _db.DeviceEnrolments.Add(DeviceEnrolment.Issue(device.Id, request.LocationId, Hash(code), expiresAt));
        await _db.SaveChangesAsync(ct);

        // Returned once, in this response, and never again: only its hash was stored. Losing it means
        // generating another, which costs nothing — unlike a registry of live codes, which is a list
        // of keys to every till in the estate.
        return Result.Success(new AgentEnrolmentPackage(
            device.DeviceKey,
            device.LocationId,
            _credentials.ServerUrl,
            code,
            expiresAt,
            HeartbeatSeconds: 5,
            ReaderRetrySeconds: 5));
    }

    public async Task<Result<EnrolmentResult>> Handle(RedeemAgentEnrolmentCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = (request.EnrolmentCode ?? string.Empty).Trim();

        if (code.Length == 0)
        {
            return Result.Failure<EnrolmentResult>(DeviceEnrolment.NotFound);
        }

        var hash = Hash(code);

        // Looked up by hash, which is the only form the server holds. A lookup by anything derived
        // from the caller's input would be the same thing said less clearly.
        var enrolment = await _db.DeviceEnrolments.FirstOrDefaultAsync(e => e.TokenHash == hash, ct);

        if (enrolment is null)
        {
            return Result.Failure<EnrolmentResult>(DeviceEnrolment.NotFound);
        }

        var redeemed = enrolment.Redeem(_clock.Now, request.Hostname);

        if (redeemed.IsFailure)
        {
            return Result.Failure<EnrolmentResult>(redeemed.Error);
        }

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == enrolment.DeviceId, ct);

        if (device is null)
        {
            return Result.Failure<EnrolmentResult>(DeviceEnrolment.NotFound);
        }

        device.Hostname = request.Hostname?.Trim();
        device.OperatingSystem = request.OperatingSystem?.Trim();
        device.AgentVersion = request.AgentVersion?.Trim();
        device.LastHeartbeat = _clock.Now;

        await _db.SaveChangesAsync(ct);

        // The durable credential crosses the wire here and only here, over TLS, into an agent that
        // has just proved which machine it is. It is what never appears in the file an installer
        // carries around.
        return Result.Success(new EnrolmentResult(
            device.Id,
            device.DeviceKey,
            device.LocationId,
            _credentials.AgentSecret));
    }

    /// <summary>
    /// SHA-256, unsalted, deliberately.
    /// <para>
    /// This is a 256-bit random value rather than a password: there is no dictionary to attack and no
    /// human memory to protect, so a slow salted hash would buy nothing and cost a round trip on
    /// every enrolment. Salting is for secrets people choose.
    /// </para>
    /// </summary>
    private static string Hash(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
