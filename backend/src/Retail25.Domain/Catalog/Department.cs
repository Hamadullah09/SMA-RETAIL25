using Retail25.Domain.Common;

namespace Retail25.Domain.Catalog;

/// <summary>
/// User-defined department grouping (guide p.31). Products are assigned to exactly one department
/// for reporting and tax purposes. Departments are editable reference data, not an enum.
/// </summary>
public sealed class Department : AggregateRoot, IAuditable, ISoftDeletable
{
    private Department()
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

    public static Result<Department> Create(long locationId, string name, string? code = null, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Department>(new Error("department.name_required", "A department name is required."));

        return Result.Success(new Department
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
