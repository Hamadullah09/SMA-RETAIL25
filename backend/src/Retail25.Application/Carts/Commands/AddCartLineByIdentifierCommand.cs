using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// The one way an item gets onto a cart by identifier: EPC, stock code, UPC, Code 39 scan, Type 2
/// weighed barcode, variant code or serial number (doc 05).
/// <para>
/// Having a single entry point matters because every one of those routes has to end up with the same
/// pricing, the same tax resolution and the same stock effects. A separate "add by barcode" path is
/// how two identification routes quietly drift apart.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record AddCartLineByIdentifierCommand(
    long CartId,
    string Identifier,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscountPct = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    LineType LineType = LineType.Sale,
    bool ReturnToStock = true,
    string? Note = null) : IRequest<Result<CartDto>>;

public sealed class AddCartLineByIdentifierHandler : IRequestHandler<AddCartLineByIdentifierCommand, Result<CartDto>>
{
    private readonly CartWorkflow _workflow;
    private readonly IdentifierResolver _resolver;
    private readonly CartLineFactory _lineFactory;

    public AddCartLineByIdentifierHandler(CartWorkflow workflow, IdentifierResolver resolver, CartLineFactory lineFactory)
    {
        _workflow = workflow;
        _resolver = resolver;
        _lineFactory = lineFactory;
    }

    public Task<Result<CartDto>> Handle(AddCartLineByIdentifierCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, context, token) =>
        {
            var resolved = await _resolver.ResolveAsync(
                request.Identifier,
                snapshot.Cart.LocationId,
                context.ScanRandomWeightBarcodes,
                token);

            if (resolved.IsFailure)
            {
                return Result.Failure(resolved.Error);
            }

            return await _lineFactory.AddAsync(
                snapshot,
                context,
                resolved.Value,
                new CartLineRequest(
                    request.Quantity,
                    request.ManualPrice,
                    request.ManualDiscountPct,
                    request.PriceLevel,
                    request.Tax1Override,
                    request.Tax2Override,
                    request.LineType,
                    request.ReturnToStock,
                    request.Note),
                token);
        }, ct);
}

/// <summary>The cashier's intent for a new line, separate from what the identifier resolved to.</summary>
public sealed record CartLineRequest(
    decimal? Quantity,
    decimal? ManualPrice,
    decimal? ManualDiscountPct,
    int? PriceLevel,
    bool? Tax1Override,
    bool? Tax2Override,
    LineType LineType = LineType.Sale,
    bool ReturnToStock = true,
    string? Note = null);

/// <summary>
/// Builds cart lines, applying the permission and policy gates that decide whether an override the
/// cashier typed is allowed to survive at all.
/// <para>
/// The gates live here rather than in the engine on purpose: the engine's job is to be a pure
/// function of its inputs, so "may this person discount?" has to be answered before the input is
/// constructed. A rejected override is dropped rather than silently honoured.
/// </para>
/// </summary>
public sealed class CartLineFactory
{
    public static readonly Error DiscountNotPermitted = new("discount.not_permitted", "You are not permitted to discount at this till.");
    public static readonly Error PriceOverrideNotPermitted = new("price.override_not_permitted", "You are not permitted to override prices.");
    public static readonly Error LevelSelectionNotPermitted = new("price.level_not_permitted", "You are not permitted to select a price level.");
    public static readonly Error KitHasNoComponents = new("kit.no_components", "This kit has no components configured.");

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITagDebouncer _debouncer;
    private readonly IPosNotifier _notifier;

    public CartLineFactory(IApplicationDbContext db, ICurrentUser currentUser, ITagDebouncer debouncer, IPosNotifier notifier)
    {
        _db = db;
        _currentUser = currentUser;
        _debouncer = debouncer;
        _notifier = notifier;
    }

    public async Task<Result> AddAsync(
        CartSnapshot snapshot,
        PosContext context,
        ResolvedItem item,
        CartLineRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        var gate = CheckOverrides(context, request);
        if (gate.IsFailure)
        {
            return gate;
        }

        // An RFID line is one physical unit, whatever quantity the caller asked for.
        var quantity = item.Unit is not null ? 1m : request.Quantity ?? 1m;
        if (quantity <= 0m)
        {
            return Result.Failure(new Error("cart.quantity_invalid", "Quantity must be greater than zero."));
        }

        var line = new CartLine
        {
            CartId = snapshot.Cart.Id,
            ProductId = item.Product.Id,
            VariantId = item.Variant?.Id,
            SerializedUnitId = item.Unit?.Id,
            Epc = item.Unit?.Epc,
            Source = item.Source,
            Quantity = quantity,
            ManualUnitPrice = request.ManualPrice,
            ManualDiscountPct = request.ManualDiscountPct,
            RequestedPriceLevel = request.PriceLevel,
            Tax1Override = request.Tax1Override,
            Tax2Override = request.Tax2Override,
            EmbeddedPrice = item.EmbeddedPrice,
            LineType = request.LineType,
            ReturnToStock = request.ReturnToStock,
            Note = request.Note,
            Sequence = snapshot.Cart.TakeNextSequence(),
            StockCodeSnapshot = item.Product.StockCode,
            NameSnapshot = item.Product.Name,
            UnitCostSnapshot = item.Product.AvgCost,
        };

        snapshot.Lines.Add(line);

        // Claim the tag so a neighbouring till cannot ring the same unit (doc 06 §2).
        if (item.Unit?.Epc is { Length: > 0 } epc)
        {
            await _debouncer.TryClaimAsync(epc, snapshot.Cart.StationId, TimeSpan.FromHours(12), ct);
            item.Unit.ClaimForCart();
        }

        if (!string.IsNullOrWhiteSpace(item.Product.PosMessage))
        {
            await _notifier.PosMessageAsync(snapshot.Cart.StationId, item.Product.Id, item.Product.PosMessage!, ct);
        }

        await AddTagAlongAsync(snapshot, item.Product, ct);

        return Result.Success();
    }

    /// <summary>
    /// An item can drag a companion onto the sale (guide p.42) — a deposit with a bottle, a case with
    /// a licence fee. Only one hop: a tag-along never pulls its own tag-along, which would let a
    /// mis-configured catalogue fill a cart from a single scan.
    /// </summary>
    private async Task AddTagAlongAsync(CartSnapshot snapshot, Product product, CancellationToken ct)
    {
        if (product.TagAlongProductId is not { } tagAlongId)
        {
            return;
        }

        var companion = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tagAlongId && !p.IsDeleted, ct);
        if (companion is null)
        {
            return;
        }

        snapshot.Lines.Add(new CartLine
        {
            CartId = snapshot.Cart.Id,
            ProductId = companion.Id,
            Source = LineSource.TagAlong,
            Quantity = 1m,
            LineType = LineType.Sale,
            Sequence = snapshot.Cart.TakeNextSequence(),
            StockCodeSnapshot = companion.StockCode,
            NameSnapshot = companion.Name,
            UnitCostSnapshot = companion.AvgCost,
        });
    }

    /// <summary>
    /// Rejects an override the actor may not make. A discount also needs the store to have turned on
    /// <c>StaffMayDiscount</c>, which is the legacy switch at guide p.77.
    /// </summary>
    public Result CheckOverrides(PosContext context, CartLineRequest request)
    {
        if (request.ManualDiscountPct is > 0m
            && !context.Policy.StaffMayDiscount
            && !_currentUser.HasPermission(PermissionKeys.Pos.Discount))
        {
            return Result.Failure(DiscountNotPermitted);
        }

        if (request.ManualPrice.HasValue && !_currentUser.HasPermission(PermissionKeys.Pos.PriceOverride))
        {
            return Result.Failure(PriceOverrideNotPermitted);
        }

        if (request.PriceLevel.HasValue && !_currentUser.HasPermission(PermissionKeys.Pos.SelectPriceLevel))
        {
            return Result.Failure(LevelSelectionNotPermitted);
        }

        if ((request.Tax1Override.HasValue || request.Tax2Override.HasValue) && !context.Policy.AllowTaxOverride)
        {
            return Result.Failure(Domain.Sales.CartTaxOverride.NotAllowed);
        }

        if ((request.Tax1Override.HasValue || request.Tax2Override.HasValue)
            && !_currentUser.HasPermission(PermissionKeys.Pos.TaxOverride))
        {
            return Result.Failure(Domain.Sales.CartTaxOverride.NotAllowed);
        }

        return Result.Success();
    }
}
