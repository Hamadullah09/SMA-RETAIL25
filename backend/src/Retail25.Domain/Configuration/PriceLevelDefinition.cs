using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>
/// Names one of the four price levels (guide p.34, p.52).
/// <para>
/// The legacy system numbered its prices and left the meaning in the shopkeeper's head. The same
/// four columns serve two purposes at once: a customer assigned level 3 always pays the level 3
/// price, and anyone buying past a break point drops to it as well. Naming the levels — Daily
/// Customer, Retailer, Wholesaler, Distributor — makes both the customer record and the price grid
/// readable without changing how either behaves.
/// </para>
/// <para>These are rows, so renaming a segment is a settings change, not a release.</para>
/// </summary>
public sealed class PriceLevelDefinition : AggregateRoot, IAuditable
{
    public const int MinLevel = 1;
    public const int MaxLevel = 4;

    public static readonly Error LevelOutOfRange = new(
        "price_level.out_of_range",
        "A price level must be between 1 and 4.");

    public static readonly Error NameRequired = new(
        "price_level.name_required",
        "A price level needs a name.");

    private PriceLevelDefinition()
    {
    }

    /// <summary>1 to 4, matching the legacy <c>UNITPRICE1..4</c> columns.</summary>
    public int Level { get; private set; }

    /// <summary>What staff see, e.g. "Wholesaler".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional note explaining who this price is for.</summary>
    public string? Description { get; private set; }

    /// <summary>Order the levels appear in on the price grid and the level picker.</summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Whether a cashier may select this level at the till. A wholesale price can be assigned to a
    /// customer record while still being unavailable as a one-off choice at the counter.
    /// </summary>
    public bool SelectableAtPos { get; private set; } = true;

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<PriceLevelDefinition> Create(
        int level,
        string name,
        string? description = null,
        bool selectableAtPos = true)
    {
        if (level is < MinLevel or > MaxLevel)
        {
            return Result.Failure<PriceLevelDefinition>(LevelOutOfRange.With("value", level));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<PriceLevelDefinition>(NameRequired);
        }

        return Result.Success(new PriceLevelDefinition
        {
            Level = level,
            Name = name.Trim(),
            Description = description,
            SortOrder = level,
            SelectableAtPos = selectableAtPos,
        });
    }

    public void Rename(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        Description = description;
    }

    public void SetSelectableAtPos(bool selectable) => SelectableAtPos = selectable;

    public void SetActive(bool isActive) => IsActive = isActive;
}
