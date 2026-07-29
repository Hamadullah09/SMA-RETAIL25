using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// The item-detail window (guide p.6): quantity, price, discount, price level, the two tax flags and
/// the line note, in the legacy tab order.
/// <para>
/// Every field is nullable and <see cref="Clear"/> names the ones being reset, because "leave this
/// alone" and "set this back to automatic" are different intentions and a single null cannot carry
/// both.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record UpdateCartLineCommand(
    Guid CartId,
    Guid LineId,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscountPct = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    LineType? LineType = null,
    bool? ReturnToStock = null,
    string? Note = null,
    IReadOnlyList<string>? Clear = null) : IRequest<Result<CartDto>>;

public sealed class UpdateCartLineHandler : IRequestHandler<UpdateCartLineCommand, Result<CartDto>>
{
    public static readonly Error LineNotFound = new("cart.line_not_found", "That line is no longer on the cart.");

    private readonly CartWorkflow _workflow;
    private readonly CartLineFactory _lineFactory;

    public UpdateCartLineHandler(CartWorkflow workflow, CartLineFactory lineFactory)
    {
        _workflow = workflow;
        _lineFactory = lineFactory;
    }

    public Task<Result<CartDto>> Handle(UpdateCartLineCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, (snapshot, context, _) =>
        {
            var line = snapshot.Lines.FirstOrDefault(l => l.Id == request.LineId);
            if (line is null)
            {
                return Task.FromResult(Result.Failure(LineNotFound.With("lineId", request.LineId)));
            }

            var gate = _lineFactory.CheckOverrides(
                context,
                new CartLineRequest(
                    request.Quantity,
                    request.ManualPrice,
                    request.ManualDiscountPct,
                    request.PriceLevel,
                    request.Tax1Override,
                    request.Tax2Override));

            if (gate.IsFailure)
            {
                return Task.FromResult(gate);
            }

            if (request.Quantity is { } quantity)
            {
                if (quantity <= 0m)
                {
                    return Task.FromResult(Result.Failure(new Error("cart.quantity_invalid", "Quantity must be greater than zero.")));
                }

                // A serialized unit is one physical thing; its quantity is not the cashier's to change.
                if (line.SerializedUnitId is not null && quantity != 1m)
                {
                    return Task.FromResult(Result.Failure(new Error(
                        "cart.serialized_quantity_fixed",
                        "A serialized or tagged unit is always quantity one.")));
                }

                line.Quantity = quantity;
            }

            var clear = request.Clear ?? [];

            line.ManualUnitPrice = clear.Contains("price", StringComparer.OrdinalIgnoreCase) ? null : request.ManualPrice ?? line.ManualUnitPrice;
            line.ManualDiscountPct = clear.Contains("discount", StringComparer.OrdinalIgnoreCase) ? null : request.ManualDiscountPct ?? line.ManualDiscountPct;
            line.RequestedPriceLevel = clear.Contains("level", StringComparer.OrdinalIgnoreCase) ? null : request.PriceLevel ?? line.RequestedPriceLevel;
            line.Tax1Override = clear.Contains("tax1", StringComparer.OrdinalIgnoreCase) ? null : request.Tax1Override ?? line.Tax1Override;
            line.Tax2Override = clear.Contains("tax2", StringComparer.OrdinalIgnoreCase) ? null : request.Tax2Override ?? line.Tax2Override;

            if (request.LineType is { } lineType)
            {
                line.LineType = lineType;
            }

            if (request.ReturnToStock is { } returnToStock)
            {
                line.ReturnToStock = returnToStock;
            }

            if (request.Note is not null)
            {
                line.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            }

            return Task.FromResult(Result.Success());
        }, ct);
}
