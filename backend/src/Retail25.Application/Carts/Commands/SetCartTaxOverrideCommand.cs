using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// F11 Special → F6 Taxes: suspends or forces a tax for the rest of this sale (guide p.11).
/// <para>
/// The override is stamped with the cart's next sequence, so it reaches only lines rung after it.
/// The guide is unambiguous that this is the legacy behaviour, and it is the right behaviour: a
/// cashier who has already read a total out to a customer should not have it change under them.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.TaxOverride)]
public sealed record SetCartTaxOverrideCommand(long CartId, bool? Tax1, bool? Tax2) : IRequest<Result<CartDto>>;

/// <summary>Attaches a customer, which re-prices the whole cart against their level and discount (guide p.52).</summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record AssignCartCustomerCommand(long CartId, long? CustomerId) : IRequest<Result<CartDto>>;

public sealed class CartContextHandlers
    : IRequestHandler<SetCartTaxOverrideCommand, Result<CartDto>>,
      IRequestHandler<AssignCartCustomerCommand, Result<CartDto>>
{
    public static readonly Error CustomerNotFound = new("customer.not_found", "No such customer.");

    private readonly CartWorkflow _workflow;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CartContextHandlers(CartWorkflow workflow, IApplicationDbContext db, ICurrentUser currentUser)
    {
        _workflow = workflow;
        _db = db;
        _currentUser = currentUser;
    }

    public Task<Result<CartDto>> Handle(SetCartTaxOverrideCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, (snapshot, context, _) =>
        {
            if (!context.Policy.AllowTaxOverride)
            {
                return Task.FromResult(Result.Failure(CartTaxOverride.NotAllowed));
            }

            if (request.Tax1 is null && request.Tax2 is null)
            {
                snapshot.TaxOverride = null;
                return Task.FromResult(Result.Success());
            }

            snapshot.TaxOverride = CartTaxOverride.Create(
                snapshot.Cart.Id,
                request.Tax1,
                request.Tax2,
                snapshot.Cart.NextLineSequence,
                _currentUser.StaffId ?? snapshot.Cart.StaffId,
                _workflow.Clock.Now);

            return Task.FromResult(Result.Success());
        }, ct);

    public Task<Result<CartDto>> Handle(AssignCartCustomerCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, _, token) =>
        {
            if (request.CustomerId is not { } customerId)
            {
                snapshot.Cart.CustomerId = null;
                return Result.Success();
            }

            var exists = await _db.Customers.AnyAsync(c => c.Id == customerId && !c.IsDeleted, token);
            if (!exists)
            {
                return Result.Failure(CustomerNotFound.With("customerId", customerId));
            }

            snapshot.Cart.CustomerId = customerId;
            return Result.Success();
        }, ct);
}
