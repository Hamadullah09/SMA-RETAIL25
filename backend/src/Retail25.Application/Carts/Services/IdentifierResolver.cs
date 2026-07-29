using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Identification;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Services;

/// <summary>What an identifier resolved to, ready to be turned into a cart line.</summary>
public sealed record ResolvedItem(
    Product Product,
    ProductVariant? Variant,
    SerializedUnit? Unit,
    LineSource Source,
    decimal? EmbeddedPrice);

/// <summary>
/// Turns anything a cashier can present — an EPC, a stock code, a UPC, a Code 39 scan, a Type 2
/// weighed barcode, a variant code or a serial number — into a product (doc 05, guide p.5).
/// <para>
/// RFID is the fast path but never the only one. A store with no RFID hardware at all must be able
/// to run this system, so every legacy identification route stays first class and the EPC branch is
/// simply tried first because it cannot be confused with anything else.
/// </para>
/// </summary>
public sealed class IdentifierResolver
{
    public static readonly Error NotFound = new("product.not_found", "No item matches that identifier.");
    public static readonly Error SerialSelectionRequired = new("serial.selection_required", "This item is tracked by serial number. Pick which unit is being sold.");
    public static readonly Error VariantSelectionRequired = new("variant.selection_required", "This item comes in variants. Pick which one is being sold.");
    public static readonly Error EpcUnknown = new("epc.unknown", "That tag is not associated with any item.");
    public static readonly Error EpcAlreadySold = new("epc.already_sold", "That tag has already been sold. It may be a shelf read or a returned tag.");
    public static readonly Error EpcWrongLocation = new("epc.wrong_location", "That tag belongs to another location.");
    public static readonly Error EpcNotAvailable = new("epc.not_available", "That tag is not in a sellable state.");

    private readonly IApplicationDbContext _db;

    public IdentifierResolver(IApplicationDbContext db) => _db = db;

    public async Task<Result<ResolvedItem>> ResolveAsync(
        string identifier,
        Guid locationId,
        bool scanRandomWeightBarcodes,
        CancellationToken ct)
    {
        var classification = IdentifierClassifier.Classify(identifier, scanRandomWeightBarcodes);

        return classification.Kind switch
        {
            IdentifierKind.Empty => Result.Failure<ResolvedItem>(NotFound),
            IdentifierKind.Epc => await ResolveEpcAsync(classification.Value, locationId, ct),
            IdentifierKind.RandomWeight => await ResolveWeighedAsync(classification, locationId, ct),
            _ => await ResolveCodeAsync(classification.Value, locationId, ct),
        };
    }

    /// <summary>
    /// An EPC is one physical unit. An unmapped tag is surfaced as an actionable row rather than
    /// dropped, because silently ignoring a tag is indistinguishable from a broken reader (doc 06 §1).
    /// </summary>
    public async Task<Result<ResolvedItem>> ResolveEpcAsync(string epc, Guid locationId, CancellationToken ct)
    {
        var normalised = epc.Trim().ToUpperInvariant();

        var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Epc == normalised, ct);
        if (unit is null)
        {
            return Result.Failure<ResolvedItem>(EpcUnknown.With("epc", normalised));
        }

        if (unit.LocationId != locationId)
        {
            return Result.Failure<ResolvedItem>(EpcWrongLocation.With("epc", normalised));
        }

        if (unit.State is SerializedUnitState.Sold or SerializedUnitState.Void)
        {
            return Result.Failure<ResolvedItem>(EpcAlreadySold.With("epc", normalised));
        }

        if (unit.State != SerializedUnitState.InStock)
        {
            return Result.Failure<ResolvedItem>(EpcNotAvailable.With("epc", normalised).With("state", unit.State.ToString()));
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == unit.ProductId && !p.IsDeleted, ct);
        if (product is null)
        {
            return Result.Failure<ResolvedItem>(EpcUnknown.With("epc", normalised));
        }

        var variant = unit.VariantId is { } variantId
            ? await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
            : null;

        return Result.Success(new ResolvedItem(product, variant, unit, LineSource.Rfid, null));
    }

    private async Task<Result<ResolvedItem>> ResolveWeighedAsync(
        IdentifierClassification classification,
        Guid locationId,
        CancellationToken ct)
    {
        var stockCode = classification.StockCode!;

        // The legacy code is five digits, zero-padded. Match the stored code either way so a store
        // that types "1234" and a scale that prints "01234" both find the same item.
        var product = await _db.Products
            .FirstOrDefaultAsync(
                p => p.LocationId == locationId && !p.IsDeleted && (p.StockCode == stockCode || p.StockCode == stockCode.TrimStart('0')),
                ct);

        return product is null
            ? Result.Failure<ResolvedItem>(NotFound.With("stockCode", stockCode))
            : Result.Success(new ResolvedItem(product, null, null, LineSource.RandomWeight, classification.EmbeddedPrice));
    }

    private async Task<Result<ResolvedItem>> ResolveCodeAsync(string code, Guid locationId, CancellationToken ct)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.LocationId == locationId && !p.IsDeleted && (p.StockCode == code || p.Upc == code), ct);

        if (product is not null)
        {
            // A matrix or serialized parent identifies a group, not a thing. Ringing it has to open a
            // picker: guessing which shirt or which serial left the shop is worse than asking, because
            // the wrong answer is only discovered at a warranty claim or a stock count.
            var ambiguity = RequiresSelection(product);
            if (ambiguity is not null)
            {
                return Result.Failure<ResolvedItem>(ambiguity.With("productId", product.Id));
            }

            var source = product.Upc == code ? LineSource.Barcode : LineSource.StockCode;
            return Result.Success(new ResolvedItem(product, null, null, source, null));
        }

        // A matrix item is usually scanned at the variant, not the parent (guide p.39–40).
        var variant = await _db.ProductVariants
            .FirstOrDefaultAsync(v => v.IsActive && (v.VariantCode == code || v.Upc == code), ct);

        if (variant is not null)
        {
            var parent = await _db.Products.FirstOrDefaultAsync(p => p.Id == variant.ProductId && !p.IsDeleted, ct);
            if (parent is not null && parent.LocationId == locationId)
            {
                return Result.Success(new ResolvedItem(parent, variant, null, LineSource.Variant, null));
            }
        }

        // Serial-number picking, for stores that track units without RFID (guide p.42).
        var unit = await _db.SerializedUnits
            .FirstOrDefaultAsync(u => u.SerialNumber == code && u.LocationId == locationId, ct);

        if (unit is null)
        {
            return Result.Failure<ResolvedItem>(NotFound.With("identifier", code));
        }

        if (unit.State != SerializedUnitState.InStock)
        {
            return Result.Failure<ResolvedItem>(EpcNotAvailable.With("serial", code).With("state", unit.State.ToString()));
        }

        var serialProduct = await _db.Products.FirstOrDefaultAsync(p => p.Id == unit.ProductId && !p.IsDeleted, ct);
        return serialProduct is null
            ? Result.Failure<ResolvedItem>(NotFound.With("identifier", code))
            : Result.Success(new ResolvedItem(serialProduct, null, unit, LineSource.Serial, null));
    }

    /// <summary>
    /// Whether ringing this product's own code is ambiguous. Scanning a variant's own barcode or a
    /// tag resolves to a specific thing and skips this entirely — it is only the parent code that
    /// needs a choice.
    /// </summary>
    private static Error? RequiresSelection(Product product) => product.Type switch
    {
        ProductType.Serialized => SerialSelectionRequired,
        ProductType.Matrix => VariantSelectionRequired,
        _ => null,
    };

    /// <summary>Resolves an explicit variant the cashier picked, bypassing the ambiguity check.</summary>
    public async Task<Result<ResolvedItem>> ResolveVariantAsync(Guid variantId, Guid locationId, CancellationToken ct)
    {
        var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId && v.IsActive, ct);
        if (variant is null)
        {
            return Result.Failure<ResolvedItem>(NotFound.With("variantId", variantId));
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == variant.ProductId && !p.IsDeleted, ct);

        return product is null || product.LocationId != locationId
            ? Result.Failure<ResolvedItem>(NotFound.With("variantId", variantId))
            : Result.Success(new ResolvedItem(product, variant, null, LineSource.Variant, null));
    }

    /// <summary>Resolves a specific serialized unit the cashier picked.</summary>
    public async Task<Result<ResolvedItem>> ResolveUnitAsync(Guid unitId, Guid locationId, CancellationToken ct)
    {
        var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null)
        {
            return Result.Failure<ResolvedItem>(NotFound.With("unitId", unitId));
        }

        if (unit.LocationId != locationId)
        {
            return Result.Failure<ResolvedItem>(EpcWrongLocation.With("unitId", unitId));
        }

        if (unit.State != SerializedUnitState.InStock)
        {
            return Result.Failure<ResolvedItem>(
                EpcNotAvailable.With("unitId", unitId).With("state", unit.State.ToString()));
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == unit.ProductId && !p.IsDeleted, ct);
        if (product is null)
        {
            return Result.Failure<ResolvedItem>(NotFound.With("unitId", unitId));
        }

        var variant = unit.VariantId is { } variantId
            ? await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
            : null;

        return Result.Success(new ResolvedItem(
            product,
            variant,
            unit,
            unit.Epc is null ? LineSource.Serial : LineSource.Rfid,
            null));
    }

    /// <summary>
    /// The F2 pick list: a prefix search over code, UPC and name, capped so a cashier gets a list
    /// rather than a catalogue (guide p.5).
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(string term, Guid locationId, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var normalised = term.Trim();

        return await _db.Products.AsNoTracking()
            .Where(p => p.LocationId == locationId && !p.IsDeleted)
            .Where(p => p.StockCode.StartsWith(normalised) || p.Upc == normalised || p.Name.Contains(normalised))
            .OrderBy(p => p.StockCode)
            .Take(limit)
            .ToListAsync(ct);
    }
}
