using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Drawer.Commands;

/// <summary>
/// Opens a drawer for a shift with its starting float (guide p.10, "F2 Float").
/// </summary>
/// <param name="StationId">Till the drawer belongs to.</param>
/// <param name="StaffId">Who counted the float in.</param>
/// <param name="OpeningFloat">Cash placed in the drawer.</param>
public sealed record OpenDrawerSessionCommand(Guid StationId, Guid StaffId, decimal OpeningFloat)
    : IRequest<DrawerResult>;

/// <summary>
/// Takes cash out of, or puts cash into, an open drawer (guide p.10, "F6 Pay Out" / "F7 Pay In").
/// </summary>
/// <param name="DrawerSessionId">Which drawer.</param>
/// <param name="StaffId">Who did it.</param>
/// <param name="Amount">How much, always positive — the direction comes from <paramref name="IsPayIn"/>.</param>
/// <param name="IsPayIn">True to put cash in, false to take it out.</param>
/// <param name="Reason">Why. Required: an unexplained movement is indistinguishable from theft.</param>
public sealed record RecordDrawerMovementCommand(
    Guid DrawerSessionId,
    Guid StaffId,
    decimal Amount,
    bool IsPayIn,
    string Reason) : IRequest<DrawerResult>;

/// <summary>
/// Closes the drawer against a physical count and records the variance (guide p.10, "F5 Save").
/// </summary>
/// <param name="DrawerSessionId">Which drawer.</param>
/// <param name="CountedCash">What was physically counted.</param>
public sealed record CloseDrawerSessionCommand(Guid DrawerSessionId, decimal CountedCash)
    : IRequest<DrawerResult>;

/// <summary>
/// The state of a drawer after a command.
/// </summary>
/// <param name="Success">Whether the command was applied.</param>
/// <param name="Error">Stable error key when it was not.</param>
/// <param name="DrawerSessionId">The session affected.</param>
/// <param name="ExpectedCash">What the drawer should contain.</param>
/// <param name="CountedCash">What was counted, once closed.</param>
/// <param name="Variance">Counted less expected. Negative means short.</param>
public sealed record DrawerResult(
    bool Success,
    string? Error,
    Guid? DrawerSessionId = null,
    decimal ExpectedCash = 0m,
    decimal? CountedCash = null,
    decimal Variance = 0m)
{
    public static DrawerResult Failed(string error) => new(false, error);
}

public class DrawerCommandHandlers :
    IRequestHandler<OpenDrawerSessionCommand, DrawerResult>,
    IRequestHandler<RecordDrawerMovementCommand, DrawerResult>,
    IRequestHandler<CloseDrawerSessionCommand, DrawerResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public DrawerCommandHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DrawerResult> Handle(OpenDrawerSessionCommand request, CancellationToken ct)
    {
        // One open drawer per till. Two would make "expected cash" meaningless, because a sale
        // would have no unambiguous drawer to land in.
        var alreadyOpen = await _db.DrawerSessions
            .AnyAsync(d => d.StationId == request.StationId && d.Status == DrawerSessionStatus.Open, ct);

        if (alreadyOpen)
        {
            return DrawerResult.Failed("drawer.already_open");
        }

        var opened = DrawerSession.Open(request.StationId, request.StaffId, request.OpeningFloat, _clock.Now);
        if (opened.IsFailure)
        {
            return DrawerResult.Failed(opened.Error.Code);
        }

        var session = opened.Value;
        _db.DrawerSessions.Add(session);

        _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
            session.Id,
            DrawerEntryType.OpeningFloat,
            request.OpeningFloat,
            request.StaffId,
            _clock.Now,
            "Opening float"));

        await _db.SaveChangesAsync(ct);

        return new DrawerResult(true, null, session.Id, session.ExpectedCash);
    }

    public async Task<DrawerResult> Handle(RecordDrawerMovementCommand request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return DrawerResult.Failed("drawer.amount_invalid");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return DrawerResult.Failed("drawer.reason_required");
        }

        var session = await _db.DrawerSessions.FirstOrDefaultAsync(d => d.Id == request.DrawerSessionId, ct);
        if (session is null)
        {
            return DrawerResult.Failed("drawer.not_found");
        }

        var signed = request.IsPayIn ? request.Amount : -request.Amount;

        var applied = session.RecordCashMovement(signed);
        if (applied.IsFailure)
        {
            return DrawerResult.Failed(applied.Error.Code);
        }

        _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
            session.Id,
            request.IsPayIn ? DrawerEntryType.PayIn : DrawerEntryType.PayOut,
            signed,
            request.StaffId,
            _clock.Now,
            request.Reason));

        await _db.SaveChangesAsync(ct);

        return new DrawerResult(true, null, session.Id, session.ExpectedCash);
    }

    public async Task<DrawerResult> Handle(CloseDrawerSessionCommand request, CancellationToken ct)
    {
        var session = await _db.DrawerSessions.FirstOrDefaultAsync(d => d.Id == request.DrawerSessionId, ct);
        if (session is null)
        {
            return DrawerResult.Failed("drawer.not_found");
        }

        var closed = session.Close(request.CountedCash, _clock.Now);
        if (closed.IsFailure)
        {
            return DrawerResult.Failed(closed.Error.Code);
        }

        await _db.SaveChangesAsync(ct);

        return new DrawerResult(
            true,
            null,
            session.Id,
            session.ExpectedCash,
            session.CountedCash,
            session.Variance);
    }
}
