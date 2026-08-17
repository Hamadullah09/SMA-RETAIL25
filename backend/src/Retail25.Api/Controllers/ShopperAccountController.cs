using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Retail25.Api.Common;
using Retail25.Application.Shoppers.Commands;

namespace Retail25.Api.Controllers;

/// <summary>
/// Sign-up and sign-in for the phone app.
/// <para>
/// Anonymous by necessity — this is where a shopper who has nothing becomes a shopper who has a
/// token — and therefore rate limited, on the same policy as staff account recovery. An unthrottled
/// anonymous endpoint that runs Argon2 is a free denial-of-service, and an unthrottled sign-in is a
/// free password guesser.
/// </para>
/// <para>
/// Separate from <see cref="RegistrationController"/> on purpose, even though both create accounts.
/// That one creates staff, in the table that carries access levels and permissions. This one creates
/// members of the public, in a table that has no such column. Sharing a controller would be one
/// mistaken route away from sharing the consequence.
/// </para>
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/v1/shopper/account")]
[EnableRateLimiting("auth")]
[Produces("application/json")]
public sealed class ShopperAccountController : ControllerBase
{
    private readonly ISender _sender;

    public ShopperAccountController(ISender sender) => _sender = sender;

    /// <summary>Creates the account and signs it in, so sign-up is one round trip, not two.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterShopperRequest request)
        => (await _sender.Send(new RegisterShopperCommand(
            request.FirstName,
            request.LastName,
            request.Phone,
            request.Email,
            request.Password,
            request.DeviceId,
            request.DeviceName,
            request.EnableBiometric))).ToActionResult(this);

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn([FromBody] SignInShopperRequest request)
        => (await _sender.Send(new SignInShopperCommand(
            request.Email,
            request.Password,
            request.DeviceId,
            request.DeviceName,
            request.EnableBiometric))).ToActionResult(this);

    /// <summary>
    /// The endpoint a fingerprint unlocks. See <see cref="RefreshShopperSessionCommand"/> — the sensor
    /// is a local gate on the token, and the server only ever sees the token.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshShopperSessionRequest request)
        => (await _sender.Send(new RefreshShopperSessionCommand(
            request.RefreshToken,
            request.DeviceName))).ToActionResult(this);
}

public sealed record RegisterShopperRequest(
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Password,
    string? DeviceId = null,
    string? DeviceName = null,
    bool EnableBiometric = false);

public sealed record SignInShopperRequest(
    string? Email,
    string? Password,
    string? DeviceId = null,
    string? DeviceName = null,
    bool EnableBiometric = false);

public sealed record RefreshShopperSessionRequest(string? RefreshToken, string? DeviceName = null);
