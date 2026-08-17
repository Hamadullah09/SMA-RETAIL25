using MediatR;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Common;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Common;

namespace Retail25.Application.Rfid.Commands;


/// <summary>
/// A tag that will not be sold, and why. The reason is shown to the cashier verbatim: "already sold"
/// and "claimed by till 2" call for completely different responses, and a generic failure teaches
/// staff to ignore the feed.
/// </summary>
public sealed record RejectedTag(string Epc, string Reason, string Message);

public sealed record RfidBatchResult(
    CartDto? Cart,
    IReadOnlyList<CartLineDto> Accepted,
    IReadOnlyList<RejectedTag> Rejected,
    int Considered);

/// <summary>
/// Adds a bulk read to the cart (doc 06 §2). The whole point of RFID at a till is that a basket of
/// thirty items becomes one action, so this takes the batch rather than one tag at a time.
/// <para>
/// Every tag is filtered by the reader profile, arbitrated in Redis so two adjacent tills cannot both
/// claim it, checked against the EPC state machine, and either added or rejected with a reason. No
/// tag is ever silently dropped.
/// </para>
/// <para>
/// The mechanics live in <see cref="Services.RfidCheckout"/>, shared with the shopper handheld's
/// tag submission — whose token can never satisfy the permission below, and which authorises by
/// owning a live trolley session instead. What this command adds is the staff half: the gate.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record AddRfidBatchCommand(long CartId, IReadOnlyList<TagRead> Tags) : IRequest<Result<RfidBatchResult>>;

public sealed class AddRfidBatchHandler : IRequestHandler<AddRfidBatchCommand, Result<RfidBatchResult>>
{
    // Referenced by callers and tests under these names; the values moved with the mechanics.
    public static readonly Error ClaimedElsewhere = Services.RfidCheckout.ClaimedElsewhere;
    public static readonly Error FilteredOut = Services.RfidCheckout.FilteredOut;
    public static readonly Error AlreadyOnCart = Services.RfidCheckout.AlreadyOnCart;

    private readonly Services.RfidCheckout _checkout;

    public AddRfidBatchHandler(Services.RfidCheckout checkout) => _checkout = checkout;

    public Task<Result<RfidBatchResult>> Handle(AddRfidBatchCommand request, CancellationToken ct)
        => _checkout.AddBatchAsync(request.CartId, request.Tags, ct);
}
