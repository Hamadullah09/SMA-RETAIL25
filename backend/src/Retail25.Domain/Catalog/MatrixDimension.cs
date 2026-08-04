using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// A dimension for matrix products (guide p.39–40). Up to 3 user-named dimensions per product
/// (e.g. Colour, Size). Each dimension has a set of values that combine to form variants.
/// </summary>
public sealed class MatrixDimension : Entity
{
    private MatrixDimension()
    {
    }

    public long ProductId { get; private set; }

    /// <summary>Dimension order (1, 2, or 3).</summary>
    public int Position { get; private set; }

    /// <summary>User-chosen name, e.g. "Colour", "Size".</summary>
    public string Name { get; private set; } = string.Empty;

    public static Result<MatrixDimension> Create(long productId, int position, string name)
    {
        if (position is < 1 or > 3)
            return Result.Failure<MatrixDimension>(new Error("dimension.position_invalid", "Dimension position must be 1, 2, or 3."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<MatrixDimension>(new Error("dimension.name_required", "A dimension name is required."));

        return Result.Success(new MatrixDimension
        {
            ProductId = productId,
            Position = position,
            Name = name.Trim(),
        });
    }

    public void UpdateName(string name) => Name = name.Trim();
}
