using MediatR;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Adds a matrix variant the cashier chose from the picker (guide p.39–40).
/// <para>
/// Separate from the identifier path on purpose: that path refuses a matrix parent because it is
/// ambiguous, and this one takes the answer. Folding both into one command would mean the ambiguity
/// check could be bypassed by passing a parent id.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record AddCartLineByVariantCommand(
    long CartId,
    long VariantId,
    decimal Quantity = 1m,
    LineType LineType = LineType.Sale) : IRequest<Result<CartDto>>;

/// <summary>Adds the specific serialized unit the cashier picked (guide p.42).</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record AddCartLineByUnitCommand(
    long CartId,
    long UnitId,
    LineType LineType = LineType.Sale) : IRequest<Result<CartDto>>;

public sealed class AddCartLineBySelectionHandler
    : IRequestHandler<AddCartLineByVariantCommand, Result<CartDto>>,
      IRequestHandler<AddCartLineByUnitCommand, Result<CartDto>>
{
    private readonly CartWorkflow _workflow;
    private readonly IdentifierResolver _resolver;
    private readonly CartLineFactory _lineFactory;

    public AddCartLineBySelectionHandler(CartWorkflow workflow, IdentifierResolver resolver, CartLineFactory lineFactory)
    {
        _workflow = workflow;
        _resolver = resolver;
        _lineFactory = lineFactory;
    }

    public Task<Result<CartDto>> Handle(AddCartLineByVariantCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, context, token) =>
        {
            var resolved = await _resolver.ResolveVariantAsync(request.VariantId, snapshot.Cart.LocationId, token);
            if (resolved.IsFailure)
            {
                return Result.Failure(resolved.Error);
            }

            return await _lineFactory.AddAsync(
                snapshot,
                context,
                resolved.Value,
                new CartLineRequest(request.Quantity, null, null, null, null, null, request.LineType),
                token);
        }, ct);

    public Task<Result<CartDto>> Handle(AddCartLineByUnitCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, context, token) =>
        {
            var resolved = await _resolver.ResolveUnitAsync(request.UnitId, snapshot.Cart.LocationId, token);
            if (resolved.IsFailure)
            {
                return Result.Failure(resolved.Error);
            }

            // A serialized unit is one physical thing; the quantity is not the cashier's to set.
            return await _lineFactory.AddAsync(
                snapshot,
                context,
                resolved.Value,
                new CartLineRequest(1m, null, null, null, null, null, request.LineType),
                token);
        }, ct);
}
