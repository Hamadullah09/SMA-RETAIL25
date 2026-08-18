using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Domain.Security;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests.Terminals;

/// <summary>
/// A till reporting that it is alive is not a change anybody made.
/// <para>
/// Each agent heartbeats every few seconds, and each heartbeat writes the station's last-seen time.
/// Audited as an edit, that is one row per till every few seconds: the live log became page after
/// page of "Updated Station" four seconds apart, burying the sign-ins, refusals and price changes
/// the log exists to hold. An audit trail nobody can read is one nobody checks, which makes this a
/// correctness problem rather than a tidiness one.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class HeartbeatAuditTests
{
    private readonly CommerceApiFixture _api;

    public HeartbeatAuditTests(CommerceApiFixture api) => _api = api;

    [Fact]
    public async Task A_heartbeat_leaves_no_audit_row()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var station = await db.Stations.FirstAsync();
        var before = await db.AuditLogEntries.CountAsync();

        station.Heartbeat("9.9.9.9", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.CountAsync())
            .Should().Be(before, "a heartbeat is telemetry, not an edit");
    }

    /// <summary>
    /// The guard is per column rather than per entity, so a real change is still recorded — and the
    /// heartbeat riding along in the same save neither hides it nor appears in its diff.
    /// </summary>
    [Fact]
    public async Task A_real_change_is_still_recorded_even_alongside_a_heartbeat()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var station = await db.Stations.FirstAsync();
        var before = await db.AuditLogEntries.CountAsync();

        station.Name = $"Renamed {Guid.NewGuid():N}"[..20];
        station.Heartbeat("9.9.9.9", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.CountAsync()).Should().Be(before + 1);

        var row = await db.AuditLogEntries
            .Where(a => a.EntityType == "Station" && a.Action == AuditAction.Updated)
            .OrderByDescending(a => a.Id)
            .FirstAsync();

        row.AfterJson.Should().Contain("Name");
        row.AfterJson.Should().NotContain("LastHeartbeat", "liveness is not an edit even when it rides along with one");
    }
}
