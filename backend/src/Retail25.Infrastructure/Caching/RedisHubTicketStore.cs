using System.Security.Cryptography;
using System.Text.Json;
using Retail25.Application.Abstractions;
using StackExchange.Redis;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// Hub tickets in Redis (doc 07 §Topology).
/// <para>
/// Redis rather than memory for two reasons. The TTL is the expiry — there is no sweeper to forget
/// to write — and <c>GETDEL</c> makes redemption atomic, so two connections racing on one ticket
/// cannot both win. Both matter once the API runs on more than one node.
/// </para>
/// </summary>
public sealed class RedisHubTicketStore : IHubTicketStore
{
    private const string KeyPrefix = "hubticket:";

    private readonly IConnectionMultiplexer _redis;

    public RedisHubTicketStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<string> IssueAsync(HubTicket ticket, TimeSpan lifetime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        // 32 bytes of CSPRNG output: a ticket that could be guessed inside its own lifetime would be
        // worse than the token it replaces.
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await _redis.GetDatabase().StringSetAsync(
            Key(value),
            JsonSerializer.Serialize(ticket),
            lifetime,
            When.NotExists);

        return value;
    }

    public async Task<HubTicket?> RedeemAsync(string ticket, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return null;
        }

        // Atomic read-and-delete: single use is a property of the operation, not of a follow-up
        // call that a crash could skip.
        var value = await _redis.GetDatabase().StringGetDeleteAsync(Key(ticket));

        // Explicit string cast: RedisValue converts implicitly to both string and
        // ReadOnlySpan<byte>, which is ambiguous against net10.0's UTF-8 Deserialize overload.
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<HubTicket>(((string?)value)!);
    }

    private static RedisKey Key(string ticket) => KeyPrefix + ticket;
}
