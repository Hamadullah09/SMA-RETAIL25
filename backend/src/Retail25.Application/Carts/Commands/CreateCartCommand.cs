using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Carts.Commands;

public sealed record CreateCartCommand(Guid StationId, Guid StaffId) : IRequest<CreateCartResult>;

public sealed record CreateCartResult(Guid CartId, int Revision);

public class CreateCartHandler : IRequestHandler<CreateCartCommand, CreateCartResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;

    public CreateCartHandler(ICartStore cartStore, IApplicationDbContext db)
    {
        _cartStore = cartStore;
        _db = db;
    }

    public async Task<CreateCartResult> Handle(CreateCartCommand request, CancellationToken ct)
    {
        // Check for existing active cart on this station.
        var existing = await _cartStore.GetByStationAsync(request.StationId, ct);
        if (existing is not null && existing.Status == CartStatus.Active)
        {
            return new CreateCartResult(existing.Id, existing.Revision);
        }

        var station = await _db.Stations.FindAsync(request.StationId);
        var locationId = station?.LocationId ?? Guid.Empty;

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            StationId = request.StationId,
            LocationId = locationId,
            StaffId = request.StaffId,
            Status = CartStatus.Active,
            NextLineSequence = 1,
            Revision = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
        };

        await _cartStore.SetAsync(cart, ct);
        return new CreateCartResult(cart.Id, cart.Revision);
    }
}
