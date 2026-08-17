using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Shoppers.Queries;
using Retail25.Application.Trolleys.Commands;
using Retail25.Application.Trolleys.Queries;
using Retail25.Infrastructure.Identity.Shoppers;

namespace Retail25.Api.Controllers;

/// <summary>
/// What a shopper may do with their own trolley, and nothing else.
/// <para>
/// The authentication scheme is named explicitly, and that is the security boundary for this whole
/// feature. The application's default authorization policy requires the OpenIddict scheme, so a bare
/// <c>[Authorize]</c> anywhere means "a member of staff" — and a shopper's token can never satisfy
/// it. Naming <see cref="ShopperAuthentication.Scheme"/> here is the only way a shopper token is
/// accepted anywhere in the API, which makes the customer-facing surface exactly this file and
/// <see cref="ShopperAccountController"/>.
/// </para>
/// <para>
/// Note what is absent: no cart id in any route. Cart ids are sequential integers, so a route that
/// took one would let any shopper walk the whole shop's baskets. Every action here derives the cart
/// from the caller's own live session instead, so there is no identifier to tamper with.
/// </para>
/// </summary>
[Authorize(AuthenticationSchemes = ShopperAuthentication.Scheme)]
[ApiController]
[Route("api/v1/shopper")]
[Produces("application/json")]
public sealed class ShopperTrolleyController : ControllerBase
{
    private readonly ISender _sender;

    public ShopperTrolleyController(ISender sender) => _sender = sender;

    /// <summary>
    /// Gives the caller a self-checkout station and opens the basket on it. The phone calls this
    /// straight after signing in; the customer types nothing.
    /// <para>
    /// Safe to call repeatedly — a shopper already mid-trip gets that trip back rather than a second
    /// counter, so the app may call it on every launch without checking first.
    /// </para>
    /// </summary>
    [HttpPost("self-checkout")]
    public async Task<IActionResult> SelfCheckout([FromBody] SelfCheckoutRequest? request)
        => (await _sender.Send(new IssueSelfCheckoutStationCommand(request?.LocationId))).ToActionResult(this);

    /// <summary>Claims the trolley whose code is printed on the handle and opens the basket.</summary>
    [HttpPost("trolleys/claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimTrolleyRequest request)
        => (await _sender.Send(new ClaimTrolleyCommand(request.Code, request.LocationId))).ToActionResult(this);

    /// <summary>The caller's own basket, priced as of now.</summary>
    [HttpGet("cart")]
    public async Task<IActionResult> MyCart()
        => (await _sender.Send(new GetMyCartQuery())).ToActionResult(this);

    /// <summary>Gives the trolley back without paying.</summary>
    [HttpPost("trolleys/release")]
    public async Task<IActionResult> Release()
        => (await _sender.Send(new ReleaseTrolleyCommand())).ToActionResult(this);

    /// <summary>
    /// Tags the shopper's handheld read. Each accepted tag becomes a line on their own basket; each
    /// refusal comes back with its reason, exactly as the cashier's feed shows them.
    /// </summary>
    [HttpPost("cart/tags")]
    public async Task<IActionResult> SubmitTags([FromBody] SubmitTagsRequest request)
        => (await _sender.Send(new SubmitMyTagsCommand(request.Epcs ?? []))).ToActionResult(this);

    /// <summary>
    /// Takes an item back out of the basket — the customer changed their mind and reshelved it. The
    /// unit returns to stock and the tag is free to sell again.
    /// </summary>
    [HttpDelete("cart/lines/{sequence:int}")]
    public async Task<IActionResult> RemoveLine(int sequence)
        => (await _sender.Send(new RemoveMyLineCommand(sequence))).ToActionResult(this);

    /// <summary>
    /// The caller's own past visits, newest first. Their receipts and nobody else's — the shopper is
    /// taken from the token, never from the route.
    /// </summary>
    [HttpGet("sales")]
    public async Task<IActionResult> PreviousSales([FromQuery] int take = 20)
        => (await _sender.Send(new GetMyPreviousSalesQuery(take))).ToActionResult(this);

    /// <summary>
    /// The ticket for the live connection. The phone calls this, then opens
    /// <c>/hubs/pos?access_token={ticket}</c> and joins the returned cart.
    /// </summary>
    [HttpPost("hub-ticket")]
    public async Task<IActionResult> HubTicket()
        => (await _sender.Send(new IssueShopperHubTicketCommand())).ToActionResult(this);
}

public sealed record ClaimTrolleyRequest(string? Code, long? LocationId = null);

/// <summary>
/// Optional, and optional all the way down — the body may be absent entirely. A single-store shop has
/// nothing to say here; a chain names the branch so a customer is not issued a counter in another town.
/// </summary>
public sealed record SelfCheckoutRequest(long? LocationId = null);

/// <summary>What the handheld read: bare EPCs. See <c>SubmitMyTagsCommand</c> for why not TagReads.</summary>
public sealed record SubmitTagsRequest(IReadOnlyList<string>? Epcs);
