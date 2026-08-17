using Retail25.Application.Carts.Dtos;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Dtos;

/// <summary>
/// A shopping trip as the phone app sees it: which trolley, and what is currently in it.
/// <para>
/// The cart is the untouched <see cref="CartDto"/> the till uses. Deliberately not a reduced
/// "customer view" of it — the shopper is entitled to see exactly the figures they are about to be
/// charged, and a second projection of the totals is a second thing that can disagree with the
/// receipt.
/// </para>
/// </summary>
public sealed record ShopperCartDto(
    long SessionId,
    long TrolleyId,
    string TrolleyCode,
    TrolleySessionState State,
    CartDto Cart);
