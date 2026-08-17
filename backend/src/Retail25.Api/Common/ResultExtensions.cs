using Microsoft.AspNetCore.Mvc;
using Retail25.Domain.Common;

namespace Retail25.Api.Common;

/// <summary>
/// Turns a domain <see cref="Result"/> into an HTTP response.
/// <para>
/// The status code is derived from the error's own machine-readable code rather than chosen at each
/// call site. A till needs to tell "the tag is already sold" (409, retry with a different tag) apart
/// from "you may not override tax" (403, fetch a supervisor) apart from "a supervisor could approve
/// this" (428, raise a step-up prompt) — and it needs the same answer from every endpoint.
/// </para>
/// </summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.Ok(result.Value)
            : Problem(result.Error, controller);
    }

    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.NoContent()
            : Problem(result.Error, controller);
    }

    public static IActionResult Problem(Error error, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(controller);

        var status = StatusFor(error.Code);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = error.Message,
            Type = $"https://retail25.local/errors/{error.Code}",
        };

        // The code and its arguments are the machine-readable part: the UI translates the code and
        // interpolates the arguments, so no business rule ever hard-codes English.
        problem.Extensions["code"] = error.Code;

        if (error.Arguments is { Count: > 0 })
        {
            problem.Extensions["arguments"] = error.Arguments;
        }

        return controller.StatusCode(status, problem);
    }

    private static int StatusFor(string code) => code switch
    {
        "sale.requires_supervisor" => StatusCodes.Status428PreconditionRequired,

        "tax.override_not_allowed" => StatusCodes.Status403Forbidden,
        "discount.not_permitted" => StatusCodes.Status403Forbidden,
        "price.override_not_permitted" => StatusCodes.Status403Forbidden,
        "price.level_not_permitted" => StatusCodes.Status403Forbidden,

        // The phone app. A shopper who mistyped their password has to be told something different
        // from a shopper whose token expired mid-shop, and the app decides which by status: 401 means
        // "sign in again", and it must never be confused with a 400 about the shape of the request.
        "shopper.credentials_invalid" => StatusCodes.Status401Unauthorized,
        "shopper.not_signed_in" => StatusCodes.Status401Unauthorized,
        "shopper_device.token_rejected" => StatusCodes.Status401Unauthorized,
        "shopper_device.not_recognised" => StatusCodes.Status401Unauthorized,
        "shopper.deactivated" => StatusCodes.Status403Forbidden,
        "shopper.email_taken" => StatusCodes.Status409Conflict,

        "trolley.not_found" => StatusCodes.Status404NotFound,

        // Not a fault and not the shopper's mistake: every self-checkout counter is genuinely in use.
        // 503 rather than 409, because the answer is "wait", and it is the one shopper error a retry
        // can actually resolve.
        "trolley.none_free" => StatusCodes.Status503ServiceUnavailable,
        "trolley_session.none" => StatusCodes.Status404NotFound,
        "trolley.not_a_shopper_station" => StatusCodes.Status403Forbidden,
        "trolley.out_of_service" => StatusCodes.Status409Conflict,
        "trolley.already_claimed" => StatusCodes.Status409Conflict,
        "trolley_session.already_shopping" => StatusCodes.Status409Conflict,
        "trolley_session.not_shopping" => StatusCodes.Status409Conflict,
        "trolley_session.not_yours" => StatusCodes.Status403Forbidden,

        "epc.unknown" => StatusCodes.Status404NotFound,
        "product.not_found" => StatusCodes.Status404NotFound,
        "customer.not_found" => StatusCodes.Status404NotFound,
        "sale.not_found" => StatusCodes.Status404NotFound,
        "station.not_found" => StatusCodes.Status404NotFound,
        "cart.line_not_found" => StatusCodes.Status404NotFound,
        "adjustment.not_found" => StatusCodes.Status404NotFound,
        "gift_certificate.unknown" => StatusCodes.Status404NotFound,
        "sale.nothing_to_reprint" => StatusCodes.Status404NotFound,

        "epc.already_sold" => StatusCodes.Status409Conflict,
        "epc.claimed_by_other_station" => StatusCodes.Status409Conflict,
        "epc.already_mapped" => StatusCodes.Status409Conflict,
        "epc.wrong_location" => StatusCodes.Status409Conflict,
        "epc.not_available" => StatusCodes.Status409Conflict,
        "stock.insufficient" => StatusCodes.Status409Conflict,
        "cart.revision_conflict" => StatusCodes.Status409Conflict,
        "cart.station_busy" => StatusCodes.Status409Conflict,
        "credit.limit_exceeded" => StatusCodes.Status409Conflict,
        "drawer.already_open" => StatusCodes.Status409Conflict,
        "drawer.already_closed" => StatusCodes.Status409Conflict,
        "product.duplicate_stock_code" => StatusCodes.Status409Conflict,
        "gift_certificate.already_redeemed" => StatusCodes.Status409Conflict,
        "payment.declined" => StatusCodes.Status402PaymentRequired,

        "cart.not_active" => StatusCodes.Status409Conflict,
        "cart.not_suspended" => StatusCodes.Status409Conflict,
        "drawer.not_open" => StatusCodes.Status409Conflict,

        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status403Forbidden => "Not permitted",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status402PaymentRequired => "Payment declined",
        StatusCodes.Status428PreconditionRequired => "Supervisor approval required",
        _ => "Request rejected",
    };
}
