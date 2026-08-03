using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

/// <summary>The bytes, and what they are, for serving straight back to a browser.</summary>
public sealed record ProductImageDto(byte[] Content, string ContentType, string ETag);

/// <summary>
/// Attaches or replaces an item's picture.
/// <para>
/// One image per product, so this is an upsert rather than an add — a second upload is somebody
/// correcting the first, not building a gallery.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record SetProductImageCommand(Guid ProductId, byte[] Content, string ContentType)
    : IRequest<Result>;

[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record RemoveProductImageCommand(Guid ProductId) : IRequest<Result>;

/// <summary>
/// Reads an item's picture.
/// <para>
/// Only <c>catalog.read</c>: a till showing a product grid needs the pictures, and a cashier holds
/// that permission where they would not hold <c>catalog.write</c>.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record GetProductImageQuery(Guid ProductId) : IRequest<Result<ProductImageDto>>;

public sealed class ProductImageHandlers :
    IRequestHandler<SetProductImageCommand, Result>,
    IRequestHandler<RemoveProductImageCommand, Result>,
    IRequestHandler<GetProductImageQuery, Result<ProductImageDto>>
{
    public static readonly Error ProductNotFound = new("product.not_found", "No such item.");
    public static readonly Error NoImage = new("image.not_found", "This item has no picture.");

    private readonly IApplicationDbContext _db;

    public ProductImageHandlers(IApplicationDbContext db) => _db = db;

    public async Task<Result> Handle(SetProductImageCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(
            p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure(ProductNotFound.With("productId", request.ProductId));
        }

        var existing = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);

        if (existing is null)
        {
            var created = ProductImage.Create(request.ProductId, request.Content, request.ContentType);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.ProductImages.Add(created.Value);
        }
        else
        {
            var replaced = existing.Replace(request.Content, request.ContentType);
            if (replaced.IsFailure)
            {
                return replaced;
            }
        }

        // The flag the grid reads. Written in the same transaction as the bytes, so the two cannot
        // disagree — a product claiming an image it does not have is a broken tile on every till.
        product.SetHasImage(true);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> Handle(RemoveProductImageCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(
            p => p.Id == request.ProductId && !p.IsDeleted, ct);

        if (product is null)
        {
            return Result.Failure(ProductNotFound.With("productId", request.ProductId));
        }

        var existing = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);

        if (existing is not null)
        {
            _db.ProductImages.Remove(existing);
        }

        product.SetHasImage(false);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ProductImageDto>> Handle(GetProductImageQuery request, CancellationToken ct)
    {
        var image = await _db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == request.ProductId)
            .Select(i => new ProductImageDto(i.Content, i.ContentType, i.ETag))
            .FirstOrDefaultAsync(ct);

        return image is null
            ? Result.Failure<ProductImageDto>(NoImage.With("productId", request.ProductId))
            : Result.Success(image);
    }
}
