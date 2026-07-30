using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Receivables;

namespace Retail25.Application.Receivables;

public sealed record GiftCardDto(
    Guid Id,
    string SerialNumber,
    decimal OriginalValue,
    decimal RemainingValue,
    Guid? IssuedToCustomerId,
    DateOnly IssuedOn,
    DateOnly? ExpiresOn,
    bool IsActive);

/// <summary>
/// Issues a new gift card. A caller-supplied serial (a physical card's printed number) is honoured
/// as-is; leaving it blank generates one — the till has no printed card to read a number off of when
/// the "card" is really just a line on the receipt.
/// </summary>
[RequiresPermission(PermissionKeys.Ar.Payment)]
public sealed record IssueGiftCardCommand(decimal Value, string? SerialNumber = null, Guid? CustomerId = null, DateOnly? ExpiresOn = null)
    : IRequest<Result<GiftCardDto>>;

[RequiresPermission(PermissionKeys.Ar.Read)]
public sealed record GiftCardBalanceQuery(string SerialNumber) : IRequest<Result<GiftCardDto>>;

public sealed class GiftCardHandlers :
    IRequestHandler<IssueGiftCardCommand, Result<GiftCardDto>>,
    IRequestHandler<GiftCardBalanceQuery, Result<GiftCardDto>>
{
    public static readonly Error DuplicateSerial = new("gift_card.duplicate_serial", "A gift card with this serial number already exists.");
    public static readonly Error NotFound = new("gift_card.not_found", "No gift card with that serial number.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GiftCardHandlers(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<GiftCardDto>> Handle(IssueGiftCardCommand request, CancellationToken ct)
    {
        var serial = string.IsNullOrWhiteSpace(request.SerialNumber)
            ? GenerateSerial()
            : request.SerialNumber.Trim().ToUpperInvariant();

        if (await _db.GiftCards.AsNoTracking().AnyAsync(g => g.SerialNumber == serial, ct))
        {
            return Result.Failure<GiftCardDto>(DuplicateSerial.With("serialNumber", serial));
        }

        var created = GiftCard.Issue(serial, request.Value, _clock.Today(), request.CustomerId, request.ExpiresOn);
        if (created.IsFailure)
        {
            return Result.Failure<GiftCardDto>(created.Error);
        }

        var card = created.Value;
        card.CreatedAt = _clock.Now;

        _db.GiftCards.Add(card);
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(card));
    }

    public async Task<Result<GiftCardDto>> Handle(GiftCardBalanceQuery request, CancellationToken ct)
    {
        var serial = request.SerialNumber.Trim().ToUpperInvariant();
        var card = await _db.GiftCards.AsNoTracking().FirstOrDefaultAsync(g => g.SerialNumber == serial, ct);

        return card is null
            ? Result.Failure<GiftCardDto>(NotFound.With("serialNumber", serial))
            : Result.Success(ToDto(card));
    }

    /// <summary>
    /// 12 characters from an unambiguous alphabet (no 0/O, 1/I/L) — read aloud over a phone or typed
    /// by hand at the till without the usual look-alike mistakes. Cryptographically random, not
    /// sequential: a gift card's number is also its bearer credential.
    /// </summary>
    private static string GenerateSerial()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        Span<char> chars = stackalloc char[12];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }

    private static GiftCardDto ToDto(GiftCard card) => new(
        card.Id,
        card.SerialNumber,
        card.OriginalValue,
        card.RemainingValue,
        card.IssuedToCustomerId,
        card.IssuedOn,
        card.ExpiresOn,
        card.IsActive);
}
