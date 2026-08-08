using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Receipts;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;

namespace Retail25.Application.Sales.Commands;

/// <summary>
/// Reprints a sales document (guide p.12, F7 and F8).
/// <para>
/// This exists because printers jam mid-queue, and it works because the document is rebuilt from the
/// sale's own frozen snapshot rather than recalculated: the reprint shows the taxes and prices that
/// were in force on the day, not today's (guide p.56).
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Reprint)]
public sealed record ReprintTransactionCommand(
    long TransactionId,
    ReceiptFormat Format = ReceiptFormat.Slip40,
    int Copies = 1,
    long? StationId = null,
    bool SendToPrinter = true) : IRequest<Result<ReceiptDocument>>;

/// <summary>The last sale rung at a station — what F7 actually reaches for (guide p.12).</summary>
[RequiresPermission(PermissionKeys.Pos.Reprint)]
public sealed record ReprintLastSaleCommand(
    long StationId,
    ReceiptFormat Format = ReceiptFormat.Slip40,
    int Copies = 1) : IRequest<Result<ReceiptDocument>>;

public sealed class ReprintTransactionHandler
    : IRequestHandler<ReprintTransactionCommand, Result<ReceiptDocument>>,
      IRequestHandler<ReprintLastSaleCommand, Result<ReceiptDocument>>
{
    public static readonly Error NothingToReprint = new("sale.nothing_to_reprint", "No sale has been rung at this station yet.");

    private readonly IApplicationDbContext _db;
    private readonly ReceiptBuilder _receipts;
    private readonly ITerminalNotifier _terminals;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public ReprintTransactionHandler(
        IApplicationDbContext db,
        ReceiptBuilder receipts,
        ITerminalNotifier terminals,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _receipts = receipts;
        _terminals = terminals;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ReceiptDocument>> Handle(ReprintTransactionCommand request, CancellationToken ct)
    {
        var transaction = await _db.SalesTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct);
        if (transaction is null)
        {
            return Result.Failure<ReceiptDocument>(VoidSaleHandler.NotFound.With("transactionId", request.TransactionId));
        }

        var document = await _receipts.BuildAsync(transaction.Id, request.Format, isReprint: true, ct);
        if (document is null)
        {
            return Result.Failure<ReceiptDocument>(VoidSaleHandler.NotFound.With("transactionId", request.TransactionId));
        }

        transaction.RecordReprint(_clock.Now);
        await _db.SaveChangesAsync(ct);

        var stationId = request.StationId ?? _currentUser.StationId ?? transaction.StationId;
        if (request.SendToPrinter)
        {
            await _terminals.PrintReceiptAsync(stationId, document, Math.Max(1, request.Copies), ct);
        }

        return Result.Success(document);
    }

    public async Task<Result<ReceiptDocument>> Handle(ReprintLastSaleCommand request, CancellationToken ct)
    {
        var lastId = await _db.SalesTransactions.AsNoTracking()
            .Where(t => t.StationId == request.StationId)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (lastId is not { } transactionId)
        {
            return Result.Failure<ReceiptDocument>(NothingToReprint.With("stationId", request.StationId));
        }

        return await Handle(
            new ReprintTransactionCommand(transactionId, request.Format, request.Copies, request.StationId),
            ct);
    }
}
