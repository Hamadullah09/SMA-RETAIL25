using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Retail25.Application.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
    int warningThresholdMs = 200)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = await next();

        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > warningThresholdMs)
        {
            logger.LogWarning(
                "Long-running request {RequestName}: {ElapsedMs}ms (threshold: {Threshold}ms)",
                typeof(TRequest).Name,
                stopwatch.ElapsedMilliseconds,
                warningThresholdMs);
        }

        return response;
    }
}
