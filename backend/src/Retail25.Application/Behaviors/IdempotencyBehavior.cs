using MediatR;
using Microsoft.Extensions.Logging;
using Retail25.Domain.Common;

namespace Retail25.Application.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse>(IIdempotencyStore store, ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand idempotentCommand)
        {
            return await next();
        }

        var key = idempotentCommand.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return await next();
        }

        var existing = await store.GetResponseAsync<TResponse>(key, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Replaying idempotent response for key {Key}", key);
            return existing;
        }

        var response = await next();

        // A failed Result<T>'s Value getter throws by design (Result.cs) — the default JSON
        // serializer touches every public property regardless, so caching a failure here crashed
        // the whole request with a generic 500 instead of surfacing the actual business error
        // (e.g. "drawer not open"). Skipping the cache on failure is also the semantically correct
        // behavior, not just a technical workaround: a failure is a verdict on the state that
        // existed when the command ran, and replaying it verbatim on a later retry — after that
        // state has since changed — would incorrectly block a request that should now succeed.
        if (response is not Result { IsFailure: true })
        {
            await store.StoreResponseAsync(key, response, cancellationToken);
        }

        return response;
    }
}

public interface IIdempotentCommand
{
    string IdempotencyKey { get; }
}

public interface IIdempotencyStore
{
    Task<T?> GetResponseAsync<T>(string key, CancellationToken ct = default);

    Task StoreResponseAsync<T>(string key, T response, CancellationToken ct = default);
}
