using MediatR;
using Microsoft.Extensions.Logging;

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

        await store.StoreResponseAsync(key, response, cancellationToken);

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
