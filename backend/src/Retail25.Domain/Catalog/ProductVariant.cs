using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// A specific combination of matrix dimensions (guide p.39–40). For a shirt product with
/// dimensions "Colour" and "Size", one variant might be "Red / Large" with its own stock code,
/// stock level, and optionally its own EPC/serial numbers.
/// </summary>
public sealed class ProductVariant : Entity, IAuditable
{
    private ProductVariant()
    {
    }

    public Guid ProductId { get; private set; }

    /// <summary>Value for dimension 1, e.g. "Red".</summary>
    public string Dim1Value { get; private set; } = string.Empty;

    /// <summary>Value for dimension 2, e.g. "Large" (nullable if product has only 1 dimension).</summary>
    public string? Dim2Value { get; private set; }

    /// <summary>Value for dimension 3 (nullable if product has only 1–2 dimensions).</summary>
    public string? Dim3Value { get; private set; }

    /// <summary>Unique variant code within the product, e.g. "RED-L".</summary>
    public string VariantCode { get; private set; } = string.Empty;

    /// <summary>Optional UPC/EAN specific to this variant.</summary>
    public string? Upc { get; private set; }

    public decimal OnHand { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<ProductVariant> Create(Guid productId, string dim1Value, string variantCode, string? dim2Value = null, string? dim3Value = null)
    {
        if (string.IsNullOrWhiteSpace(dim1Value))
            return Result.Failure<ProductVariant>(new Error("variant.dim_required", "At least dimension 1 is required."));

        if (string.IsNullOrWhiteSpace(variantCode))
            return Result.Failure<ProductVariant>(new Error("variant.code_required", "A variant code is required."));

        return Result.Success(new ProductVariant
        {
            ProductId = productId,
            Dim1Value = dim1Value.Trim(),
            Dim2Value = dim2Value?.Trim(),
            Dim3Value = dim3Value?.Trim(),
            VariantCode = variantCode.Trim().ToUpperInvariant(),
        });
    }

    public void UpdateValues(string dim1, string? dim2, string? dim3)
    {
        Dim1Value = dim1.Trim();
        Dim2Value = dim2?.Trim();
        Dim3Value = dim3?.Trim();
    }

    public void UpdateStock(decimal onHand) => OnHand = onHand;

    public void SetUpc(string? upc) => Upc = string.IsNullOrWhiteSpace(upc) ? null : upc.Trim();

    /// <summary>
    /// Retires a variant without destroying it. A variant that has ever been sold is named by sale
    /// lines, so deleting it would orphan history; hiding it keeps the grid tidy and the past intact.
    /// </summary>
    public void SetActive(bool isActive) => IsActive = isActive;
}
