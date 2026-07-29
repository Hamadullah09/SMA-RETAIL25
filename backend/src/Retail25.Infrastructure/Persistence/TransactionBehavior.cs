using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Retail25.Application.Behaviors;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Wraps a command in one database transaction (doc 05).
/// <para>
/// It lives in Infrastructure because it needs the real DbContext. A sale writes a transaction, its
/// lines, stock ledger entries, stock levels, a drawer entry, a loyalty entry and possibly an
/// invoice; any of those landing without the others would leave the books wrong in a way no report
/// could explain. Queries skip this entirely.
/// </para>
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(ApplicationDbContext db, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICommand and not ITransactionalCommand and not IIdempotentCommand)
        {
            return await next();
        }

        // A handler that opened its own transaction has already taken responsibility for the scope.
        if (_db.Database.CurrentTransaction is not null)
        {
            return await next();
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var response = await next();

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rolling back {RequestName}", typeof(TRequest).Name);
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
