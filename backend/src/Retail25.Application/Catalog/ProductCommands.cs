using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;

namespace Retail25.Application.Catalog;

// --- Form sections ------------------------------------------------------------------------------
// The Form View saves the section the user actually touched. A null section is "unchanged", not
// "clear it" — a user who edits pricing and never opens the ordering tab must not have that tab's
// defaults written over the values already there.

public sealed record ProductGeneralSection(
    string StockCode,
    string Name,
    string? Description,
    ProductType Type,
    string? Upc,
    long? DepartmentId,
    long? CategoryId,
    string? BinLocation);

public sealed record ProductTaxSection(bool Tax1Applies, bool Tax2Applies);

public sealed record ProductPricingSection(
    decimal RegularPrice,
    decimal LastCost,
    IReadOnlyList<ProductPriceDto> Levels,
    IReadOnlyList<PriceBreakDto> Breaks,
    SalePricingDto? Sale,
    BonusPricingDto? Bonus);

public sealed record ProductOrderingSection(
    int BaseStock,
    int ReorderPoint,
    int ReorderQty,
    decimal CaseQty,
    decimal ShipWeight,
    IReadOnlyList<ProductSupplierDto> Suppliers);

public sealed record ProductMessagesSection(string? PosMessage, string? InvoiceMessage, string? Notes);

public sealed record ProductLinksSection(long? SubstituteProductId, long? TagAlongProductId, long? ParentProductId);

public sealed record ProductKitSection(IReadOnlyList<KitComponentDto> Components);

// --- Commands -----------------------------------------------------------------------------------

/// <summary>Creates an item from the Form View's "new" state (guide p.30).</summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record CreateProductCommand(
    long LocationId,
    ProductGeneralSection General,
    decimal RegularPrice,
    bool Tax1Applies = true,
    bool Tax2Applies = true) : IRequest<Result<ProductFormDto>>;

/// <summary>
/// Saves one or more sections of the Form View. Sections left null are untouched.
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record UpdateProductCommand(
    long ProductId,
    ProductGeneralSection? General = null,
    ProductTaxSection? Tax = null,
    ProductPricingSection? Pricing = null,
    ProductOrderingSection? Ordering = null,
    ProductMessagesSection? Messages = null,
    ProductLinksSection? Links = null,
    ProductKitSection? Kit = null) : IRequest<Result<ProductFormDto>>;

/// <summary>
/// Copies an item to a new stock code (guide p.30, "copy an existing item").
/// <para>
/// Everything that describes the item is copied; nothing that records its history is. Stock on hand,
/// costs and supplier history belong to the original — a clone that arrived with 40 units in stock
/// would be an inventory adjustment nobody made.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Write)]
public sealed record CloneProductCommand(long ProductId, string NewStockCode, string? NewName = null)
    : IRequest<Result<ProductFormDto>>;

/// <summary>Hides an item. Reversible through <see cref="RestoreProductCommand"/> (guide p.24).</summary>
[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record DeleteProductCommand(long ProductId) : IRequest<Result>;

/// <summary>The legacy "Undelete Items" command (guide p.24).</summary>
[RequiresPermission(PermissionKeys.Catalog.Delete)]
public sealed record RestoreProductCommand(long ProductId) : IRequest<Result>;

public sealed class ProductCommandHandlers
    : IRequestHandler<CreateProductCommand, Result<ProductFormDto>>,
      IRequestHandler<UpdateProductCommand, Result<ProductFormDto>>,
      IRequestHandler<CloneProductCommand, Result<ProductFormDto>>,
      IRequestHandler<DeleteProductCommand, Result>,
      IRequestHandler<RestoreProductCommand, Result>
{
    public static readonly Error NotFound = new("product.not_found", "No such item.");
    public static readonly Error StillInStock = new("product.still_in_stock", "This item still has stock on hand. Adjust it to zero before deleting.");
    public static readonly Error LinkToSelf = new("product.link_to_self", "An item cannot be its own substitute, tag-along or parent.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly IRequestHandler<GetProductFormQuery, Result<ProductFormDto>> _form;

    /// <summary>
    /// The form query handler is injected rather than dispatched through <c>IMediator</c>. Sending
    /// would re-enter the pipeline — a second authorisation check, a nested transaction scope and a
    /// second idempotency record — to read back the row this handler just wrote.
    /// </summary>
    public ProductCommandHandlers(
        IApplicationDbContext db,
        IPosNotifier notifier,
        IRequestHandler<GetProductFormQuery, Result<ProductFormDto>> form)
    {
        _db = db;
        _notifier = notifier;
        _form = form;
    }

    public async Task<Result<ProductFormDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var duplicate = await IsDuplicateAsync(request.LocationId, request.General.StockCode, null, ct);
        if (duplicate)
        {
            return Result.Failure<ProductFormDto>(Product.DuplicateStockCode.With("stockCode", request.General.StockCode));
        }

        var created = Product.Create(
            request.LocationId,
            request.General.StockCode,
            request.General.Name,
            request.General.Type,
            request.RegularPrice,
            request.Tax1Applies,
            request.Tax2Applies);

        if (created.IsFailure)
        {
            return Result.Failure<ProductFormDto>(created.Error);
        }

        var product = created.Value;
        product.UpdateDetails(request.General.Name, request.General.Description, request.General.Upc, request.General.BinLocation, null);
        product.SetDepartment(request.General.DepartmentId);
        product.SetCategory(request.General.CategoryId);

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        await PublishAsync(product, ct);

        return await FormAsync(product.Id, ct);
    }

    public async Task<Result<ProductFormDto>> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure<ProductFormDto>(NotFound.With("productId", request.ProductId));
        }

        if (request.General is { } general)
        {
            if (!string.Equals(general.StockCode.Trim(), product.StockCode, StringComparison.OrdinalIgnoreCase)
                && await IsDuplicateAsync(product.LocationId, general.StockCode, product.Id, ct))
            {
                return Result.Failure<ProductFormDto>(Product.DuplicateStockCode.With("stockCode", general.StockCode));
            }

            var renamed = product.SetStockCode(general.StockCode);
            if (renamed.IsFailure)
            {
                return Result.Failure<ProductFormDto>(renamed.Error);
            }

            product.SetType(general.Type);
            product.UpdateDetails(general.Name, general.Description, general.Upc, general.BinLocation, product.Notes);
            product.SetDepartment(general.DepartmentId);
            product.SetCategory(general.CategoryId);
        }

        if (request.Tax is { } tax)
        {
            product.SetTaxFlags(tax.Tax1Applies, tax.Tax2Applies);
        }

        if (request.Messages is { } messages)
        {
            product.UpdateMessages(messages.PosMessage, messages.InvoiceMessage);
            product.UpdateDetails(product.Name, product.Description, product.Upc, product.BinLocation, messages.Notes);
        }

        if (request.Links is { } links)
        {
            if (links.SubstituteProductId == product.Id
                || links.TagAlongProductId == product.Id
                || links.ParentProductId == product.Id)
            {
                return Result.Failure<ProductFormDto>(LinkToSelf);
            }

            product.SetLinks(links.SubstituteProductId, links.TagAlongProductId, links.ParentProductId);
        }

        if (request.Ordering is { } ordering)
        {
            product.UpdateOrdering(ordering.BaseStock, ordering.ReorderPoint, ordering.ReorderQty, ordering.CaseQty, ordering.ShipWeight);
            await ReplaceSuppliersAsync(product.Id, ordering.Suppliers, ct);
        }

        if (request.Pricing is { } pricing)
        {
            // The average cost is owned by the stock ledger, not by this form. Letting a user type
            // over it would make every margin figure downstream a fiction.
            product.UpdatePricing(pricing.RegularPrice, pricing.LastCost, product.AvgCost);

            var replaced = await ReplacePricingAsync(product.Id, pricing, ct);
            if (replaced.IsFailure)
            {
                return Result.Failure<ProductFormDto>(replaced.Error);
            }
        }

        if (request.Kit is { } kit)
        {
            var replaced = await ReplaceKitAsync(product.Id, kit.Components, ct);
            if (replaced.IsFailure)
            {
                return Result.Failure<ProductFormDto>(replaced.Error);
            }
        }

        await _db.SaveChangesAsync(ct);
        await PublishAsync(product, ct);

        return await FormAsync(product.Id, ct);
    }

    public async Task<Result<ProductFormDto>> Handle(CloneProductCommand request, CancellationToken ct)
    {
        var source = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (source is null)
        {
            return Result.Failure<ProductFormDto>(NotFound.With("productId", request.ProductId));
        }

        if (await IsDuplicateAsync(source.LocationId, request.NewStockCode, null, ct))
        {
            return Result.Failure<ProductFormDto>(Product.DuplicateStockCode.With("stockCode", request.NewStockCode));
        }

        var created = Product.Create(
            source.LocationId,
            request.NewStockCode,
            request.NewName ?? source.Name,
            source.Type,
            source.RegularPrice,
            source.Tax1Applies,
            source.Tax2Applies);

        if (created.IsFailure)
        {
            return Result.Failure<ProductFormDto>(created.Error);
        }

        var clone = created.Value;

        // Descriptive fields carry over; the UPC does not. A UPC identifies one physical product, and
        // two items sharing one would make every scan ambiguous.
        clone.UpdateDetails(clone.Name, source.Description, null, source.BinLocation, source.Notes);
        clone.UpdateMessages(source.PosMessage, source.InvoiceMessage);
        clone.UpdateOrdering(source.BaseStock, source.ReorderPoint, source.ReorderQty, source.CaseQty, source.ShipWeight);
        clone.UpdatePricing(source.RegularPrice, source.LastCost, 0m);
        clone.SetDepartment(source.DepartmentId);
        clone.SetCategory(source.CategoryId);
        clone.SetLinks(source.SubstituteProductId, source.TagAlongProductId, source.ParentProductId);

        _db.Products.Add(clone);

        foreach (var level in await _db.ProductPrices.AsNoTracking().Where(p => p.ProductId == source.Id).ToListAsync(ct))
        {
            var copy = ProductPrice.Create(clone.Id, level.Level, level.Price);
            if (copy.IsSuccess)
            {
                _db.ProductPrices.Add(copy.Value);
            }
        }

        foreach (var pricebreak in await _db.PriceBreaks.AsNoTracking().Where(b => b.ProductId == source.Id).ToListAsync(ct))
        {
            var copy = PriceBreak.Create(clone.Id, pricebreak.Level, pricebreak.MinQuantity);
            if (copy.IsSuccess)
            {
                _db.PriceBreaks.Add(copy.Value);
            }
        }

        foreach (var supplier in await _db.ProductSuppliers.AsNoTracking().Where(s => s.ProductId == source.Id).ToListAsync(ct))
        {
            var copy = ProductSupplier.Create(clone.Id, supplier.SupplierId, supplier.Rank, supplier.Cost, supplier.ReorderNumber);
            if (copy.IsSuccess)
            {
                copy.Value.Update(supplier.Rank, supplier.Cost, supplier.ReorderNumber, supplier.CaseQty, supplier.MinimumOrderQty);
                _db.ProductSuppliers.Add(copy.Value);
            }
        }

        await _db.SaveChangesAsync(ct);
        await PublishAsync(clone, ct);

        return await FormAsync(clone.Id, ct);
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure(NotFound.With("productId", request.ProductId));
        }

        if (product.OnHand != 0m)
        {
            // Deleting an item that still has stock would strand its value: the ledger says the store
            // owns forty of them and the catalogue says the item does not exist.
            return Result.Failure(StillInStock.With("onHand", product.OnHand));
        }

        // Links pointing at this item are cleared, not left dangling. A substitute that resolves to a
        // deleted item is a dead end the cashier discovers with a customer at the counter.
        var referrers = await _db.Products
            .Where(p => p.SubstituteProductId == product.Id
                || p.TagAlongProductId == product.Id
                || p.ParentProductId == product.Id)
            .ToListAsync(ct);

        foreach (var referrer in referrers)
        {
            referrer.SetLinks(
                referrer.SubstituteProductId == product.Id ? null : referrer.SubstituteProductId,
                referrer.TagAlongProductId == product.Id ? null : referrer.TagAlongProductId,
                referrer.ParentProductId == product.Id ? null : referrer.ParentProductId);
        }

        // Remove() is turned into a soft delete by the auditing interceptor, which also writes the
        // before/after row — so "who deleted this and when" is answerable without a separate log.
        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct);

        await _notifier.RowRemovedAsync(product.LocationId, GridKeys.Product, product.Id, ct);
        await _notifier.ProductDeletedAsync(product.LocationId, product.Id, ct);

        return Result.Success();
    }

    public async Task<Result> Handle(RestoreProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
        {
            return Result.Failure(NotFound.With("productId", request.ProductId));
        }

        if (!product.IsDeleted)
        {
            return Result.Success();
        }

        // A stock code freed after the delete may since have been reused. Restoring on top of it
        // would break the uniqueness the till depends on to resolve a scan.
        if (await IsDuplicateAsync(product.LocationId, product.StockCode, product.Id, ct))
        {
            return Result.Failure(Product.DuplicateStockCode.With("stockCode", product.StockCode));
        }

        product.Restore();
        await _db.SaveChangesAsync(ct);
        await PublishAsync(product, ct);

        return Result.Success();
    }

    private Task<bool> IsDuplicateAsync(long locationId, string stockCode, long? excluding, CancellationToken ct)
    {
        var normalized = stockCode.Trim().ToUpperInvariant();

        return _db.Products.AsNoTracking().AnyAsync(
            p => p.LocationId == locationId
                && !p.IsDeleted
                && p.StockCode == normalized
                && (excluding == null || p.Id != excluding),
            ct);
    }

    /// <summary>
    /// Replaces price levels, break points, the sale window and the bonus rule wholesale.
    /// <para>
    /// Replacement rather than a diff because these are small sets the user edits as a grid: deciding
    /// which of four rows was "the same row" after an edit is guesswork, and guessing wrong changes a
    /// price. None of them is referenced by history — sale lines snapshot the resolved price.
    /// </para>
    /// </summary>
    private async Task<Result> ReplacePricingAsync(long productId, ProductPricingSection pricing, CancellationToken ct)
    {
        _db.ProductPrices.RemoveRange(await _db.ProductPrices.Where(p => p.ProductId == productId).ToListAsync(ct));
        _db.PriceBreaks.RemoveRange(await _db.PriceBreaks.Where(b => b.ProductId == productId).ToListAsync(ct));
        _db.SalePricings.RemoveRange(await _db.SalePricings.Where(s => s.ProductId == productId).ToListAsync(ct));
        _db.BonusPricings.RemoveRange(await _db.BonusPricings.Where(b => b.ProductId == productId).ToListAsync(ct));

        // A rule that fails validation is reported, not skipped. Dropping it silently would answer
        // "Saved" and leave the item priced by a rule the user believes they just created — which
        // they would only discover from a customer being charged the wrong amount.
        foreach (var level in pricing.Levels ?? [])
        {
            var created = ProductPrice.Create(productId, level.Level, level.Price);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.ProductPrices.Add(created.Value);
        }

        foreach (var pricebreak in pricing.Breaks ?? [])
        {
            var created = PriceBreak.Create(productId, pricebreak.Level, pricebreak.MinQuantity);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.PriceBreaks.Add(created.Value);
        }

        if (pricing.Sale is { } sale)
        {
            var created = SalePricing.Create(productId, sale.DiscountPct, sale.StartsOn, sale.EndsOn);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.SalePricings.Add(created.Value);
        }

        if (pricing.Bonus is { } bonus)
        {
            var created = BonusPricing.Create(productId, bonus.BuyQty, bonus.FreeQty);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.BonusPricings.Add(created.Value);
        }

        return Result.Success();
    }

    private async Task ReplaceSuppliersAsync(long productId, IReadOnlyList<ProductSupplierDto> suppliers, CancellationToken ct)
    {
        var existing = await _db.ProductSuppliers.Where(s => s.ProductId == productId).ToListAsync(ct);
        var wanted = (suppliers ?? []).ToList();
        var keep = wanted.Select(s => s.SupplierId).ToHashSet();

        _db.ProductSuppliers.RemoveRange(existing.Where(s => !keep.Contains(s.SupplierId)));

        foreach (var supplier in wanted)
        {
            var row = existing.FirstOrDefault(s => s.SupplierId == supplier.SupplierId);

            if (row is null)
            {
                var created = ProductSupplier.Create(productId, supplier.SupplierId, supplier.Rank, supplier.Cost, supplier.ReorderNumber);
                if (created.IsFailure)
                {
                    continue;
                }

                row = created.Value;
                _db.ProductSuppliers.Add(row);
            }

            row.Update(supplier.Rank, supplier.Cost, supplier.ReorderNumber, supplier.CaseQty, supplier.MinimumOrderQty);
        }
    }

    private async Task<Result> ReplaceKitAsync(long productId, IReadOnlyList<KitComponentDto> components, CancellationToken ct)
    {
        _db.KitComponents.RemoveRange(await _db.KitComponents.Where(k => k.KitProductId == productId).ToListAsync(ct));

        foreach (var component in components ?? [])
        {
            var created = KitComponent.Create(productId, component.ComponentProductId, component.Quantity);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _db.KitComponents.Add(created.Value);
        }

        return Result.Success();
    }

    private async Task PublishAsync(Product product, CancellationToken ct)
    {
        var departments = product.DepartmentId is { } deptId
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == deptId).ToDictionaryAsync(d => d.Id, d => d.Name, ct)
            : [];

        var categories = product.CategoryId is { } catId
            ? await _db.Categories.AsNoTracking().Where(c => c.Id == catId).ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            : [];

        var row = BrowseProductsHandlers.ToRow(product, departments, categories);

        await _notifier.RowChangedAsync(product.LocationId, GridKeys.Product, product.Id, row, ct);
        await _notifier.ProductChangedAsync(product.LocationId, product.Id, ct);
    }

    private Task<Result<ProductFormDto>> FormAsync(long productId, CancellationToken ct)
        => _form.Handle(new GetProductFormQuery(productId), ct);
}
