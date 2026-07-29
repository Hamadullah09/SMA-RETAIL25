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
/// The legacy F11-F2 "unknown item" (guide p.11): sell something that is not in the catalogue.
/// <para>
/// A queue does not wait for data entry. The line rings immediately against a placeholder product;
/// <see cref="CreateProduct"/> optionally promotes it to a real catalogue row so the second customer
/// buying the same thing scans it normally. Without that, unknown items accumulate as untraceable
/// revenue, which is exactly the hole the legacy feature left.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.UnknownItem)]
public sealed record AddUnknownItemCommand(
    Guid CartId,
    string Description,
    decimal UnitPrice,
    decimal Quantity = 1m,
    bool Tax1Applies = true,
    bool Tax2Applies = true,
    bool CreateProduct = false,
    string? StockCode = null,
    Guid? DepartmentId = null) : IRequest<Result<CartDto>>;

public sealed class AddUnknownItemHandler : IRequestHandler<AddUnknownItemCommand, Result<CartDto>>
{
    /// <summary>The catalogue row every un-promoted unknown item rings against.</summary>
    public const string PlaceholderStockCode = "UNKNOWN";

    public static readonly Error DescriptionRequired = new("unknown_item.description_required", "An unknown item needs a description.");
    public static readonly Error PriceRequired = new("unknown_item.price_required", "An unknown item needs a price above zero.");

    private readonly CartWorkflow _workflow;
    private readonly IApplicationDbContext _db;
    private readonly CartLineFactory _lineFactory;

    public AddUnknownItemHandler(CartWorkflow workflow, IApplicationDbContext db, CartLineFactory lineFactory)
    {
        _workflow = workflow;
        _db = db;
        _lineFactory = lineFactory;
    }

    public Task<Result<CartDto>> Handle(AddUnknownItemCommand request, CancellationToken ct)
        => _workflow.MutateAsync(request.CartId, async (snapshot, context, token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return Result.Failure(DescriptionRequired);
            }

            if (request.UnitPrice <= 0m)
            {
                return Result.Failure(PriceRequired);
            }

            var productResult = request.CreateProduct
                ? await CreateCatalogueEntryAsync(request, snapshot.Cart.LocationId, token)
                : await GetOrCreatePlaceholderAsync(snapshot.Cart.LocationId, token);

            if (productResult.IsFailure)
            {
                return Result.Failure(productResult.Error);
            }

            var product = productResult.Value;

            var add = await _lineFactory.AddAsync(
                snapshot,
                context,
                new ResolvedItem(product, null, null, LineSource.Unknown, null),
                new CartLineRequest(
                    request.Quantity,
                    request.UnitPrice,
                    null,
                    null,
                    request.Tax1Applies ? null : false,
                    request.Tax2Applies ? null : false,
                    Note: request.Description.Trim()),
                token);

            if (add.IsFailure)
            {
                return add;
            }

            // The placeholder's own name would read "Unknown item" on the receipt; the cashier's
            // description is what the customer needs to see.
            var line = snapshot.Lines[^1];
            line.NameSnapshot = request.Description.Trim();
            return Result.Success();
        }, ct);

    private async Task<Result<Product>> GetOrCreatePlaceholderAsync(Guid locationId, CancellationToken ct)
    {
        var existing = await _db.Products
            .FirstOrDefaultAsync(p => p.LocationId == locationId && p.StockCode == PlaceholderStockCode, ct);

        if (existing is not null)
        {
            return Result.Success(existing);
        }

        var created = Product.Create(locationId, PlaceholderStockCode, "Unknown item", ProductType.NonStock, 0m);
        if (created.IsFailure)
        {
            return created;
        }

        _db.Products.Add(created.Value);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<Result<Product>> CreateCatalogueEntryAsync(AddUnknownItemCommand request, Guid locationId, CancellationToken ct)
    {
        var stockCode = string.IsNullOrWhiteSpace(request.StockCode)
            ? await NextGeneratedCodeAsync(locationId, ct)
            : request.StockCode.Trim().ToUpperInvariant();

        var duplicate = await _db.Products.AnyAsync(p => p.LocationId == locationId && p.StockCode == stockCode, ct);
        if (duplicate)
        {
            return Result.Failure<Product>(Product.DuplicateStockCode.With("stockCode", stockCode));
        }

        var created = Product.Create(
            locationId,
            stockCode,
            request.Description.Trim(),
            ProductType.Standard,
            request.UnitPrice,
            request.Tax1Applies,
            request.Tax2Applies);

        if (created.IsFailure)
        {
            return created;
        }

        if (request.DepartmentId is { } departmentId)
        {
            created.Value.SetDepartment(departmentId);
        }

        _db.Products.Add(created.Value);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>A predictable, sortable code so promoted items are easy to find and tidy up later.</summary>
    private async Task<string> NextGeneratedCodeAsync(Guid locationId, CancellationToken ct)
    {
        const string prefix = "NEW";

        var count = await _db.Products.CountAsync(p => p.LocationId == locationId && p.StockCode.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:D5}";
    }
}
