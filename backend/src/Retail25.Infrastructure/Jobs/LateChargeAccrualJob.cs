using Microsoft.Extensions.Logging;
using Retail25.Application.Receivables;

namespace Retail25.Infrastructure.Jobs;

/// <summary>
/// The nightly late-charge run <see cref="Domain.Receivables.LateChargePolicy"/>'s own doc comment
/// names ("applied by a nightly Hangfire job").
/// <para>
/// Calls <see cref="ReceivablesHandlers"/> directly rather than through <c>ISender</c>: this is a
/// system-initiated process with no authenticated user behind it, so the authorization pipeline
/// behaviour — built to check a real request's permissions — does not apply here any more than it
/// would to a cron job. <see cref="AccrueLateChargesCommand"/> is still declared
/// <c>[RequiresPermission]</c> for the administrator-triggered manual run through the API.
/// </para>
/// </summary>
public sealed class LateChargeAccrualJob
{
    private readonly ReceivablesHandlers _handlers;
    private readonly ILogger<LateChargeAccrualJob> _logger;

    public LateChargeAccrualJob(ReceivablesHandlers handlers, ILogger<LateChargeAccrualJob> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var result = await _handlers.Handle(new AccrueLateChargesCommand(), ct);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Late-charge accrual posted {Count} charge(s)", result.Value);
        }
        else
        {
            _logger.LogWarning("Late-charge accrual run failed: {Code}", result.Error.Code);
        }
    }
}
