using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Application.Accounting;

namespace Retail25.Infrastructure.Jobs;

/// <summary>
/// Posts yesterday's takings to the accounting system, per location, once a night (guide p.111).
/// <para>
/// Runs against the connector directly rather than through the mediator for the same reason the
/// late-charge job does: a scheduled task has no signed-in user, so there is no principal for the
/// authorisation behaviour to check.
/// </para>
/// <para>
/// A failure is logged and the next location still runs. Accounting is downstream of selling, and a
/// bookkeeping outage must never be able to stop a till opening in the morning.
/// </para>
/// </summary>
public sealed class PostPosRevenueToAccountingJob
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingConnector _connector;
    private readonly IDateTime _clock;
    private readonly ILogger<PostPosRevenueToAccountingJob> _logger;

    public PostPosRevenueToAccountingJob(
        IApplicationDbContext db,
        IAccountingConnector connector,
        IDateTime clock,
        ILogger<PostPosRevenueToAccountingJob> logger)
    {
        _db = db;
        _connector = connector;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Yesterday: the job runs in the small hours, and today's trading is not finished.
        var businessDate = _clock.Today().AddDays(-1);

        var locationIds = await _db.Locations.AsNoTracking().Select(l => l.Id).ToListAsync(ct);

        foreach (var locationId in locationIds)
        {
            try
            {
                var result = await _connector.PostPosRevenueAsync(locationId, businessDate, ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Posted {Count} drawer session(s) of {Date} revenue for location {LocationId}",
                        result.RecordCount, businessDate, locationId);
                }
                else
                {
                    _logger.LogWarning(
                        "Posting {Date} revenue for location {LocationId} failed: {Error}",
                        businessDate, locationId, result.Error);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Posting {Date} revenue for location {LocationId} threw; continuing with the next location",
                    businessDate, locationId);
            }
        }
    }
}
