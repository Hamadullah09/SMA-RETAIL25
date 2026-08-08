using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// User-defined category grouping (guide p.31). Products can be assigned to a category for
/// filtering and reporting. Categories are editable reference data.
/// </summary>
public sealed class Category : AggregateRoot, IAuditable, ISoftDeletable
{
    private Category()
    {
    }

    public long LocationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Code { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public long? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<Category> Create(long locationId, string name, string? code = null, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(new Error("category.name_required", "A category name is required."));

        return Result.Success(new Category
        {
            LocationId = locationId,
            Name = name.Trim(),
            Code = code?.Trim(),
            SortOrder = sortOrder,
        });
    }

    public void Update(string name, string? code, int sortOrder)
    {
        Name = name.Trim();
        Code = code?.Trim();
        SortOrder = sortOrder;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
