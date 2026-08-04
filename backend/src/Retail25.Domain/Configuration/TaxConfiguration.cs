using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Configuration;

/// <summary>Whether sticker prices already contain tax (user guide p.77).</summary>
public enum TaxationType
{
    /// <summary>Tax is added at the till. Typical in Canada and the United States.</summary>
    Exclusive = 0,

    /// <summary>Tax is inside the shelf price and is backed out for reporting. Typical VAT behaviour.</summary>
    Inclusive = 1,
}

/// <summary>
/// The two sales taxes and the optional percentage add-on charge, exactly as the legacy Setup
/// screen models them (user guide p.76–77), but <b>effective-dated per location</b>.
/// <para>
/// Nothing here is compiled in: names, rates, whether tax 2 compounds on tax 1, whether the add-on
/// charge is itself taxable, and inclusive-versus-exclusive pricing are all rows. A rate change
/// creates a new row rather than editing history, which — together with the tax snapshot written
/// onto every sale — is what makes a reprint show the taxes that were in force at the time
/// (user guide p.56).
/// </para>
/// </summary>
public sealed class TaxConfiguration : AggregateRoot, IAuditable
{
    public static readonly Error RateInvalid = new("tax.rate_invalid", "A tax rate cannot be negative.");
    public static readonly Error NameRequired = new("tax.name_required", "An enabled tax must have a name.");

    private TaxConfiguration()
    {
    }

    public long LocationId { get; private set; }

    /// <summary>First date this configuration applies to. Sales before it use the previous row.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Last date this configuration applies to; null while it is the current row.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public bool Tax1Enabled { get; private set; }

    public string Tax1Name { get; private set; } = string.Empty;

    public Percentage Tax1Rate { get; private set; } = Percentage.Zero;

    public bool Tax2Enabled { get; private set; }

    public string Tax2Name { get; private set; } = string.Empty;

    public Percentage Tax2Rate { get; private set; } = Percentage.Zero;

    /// <summary>
    /// When true, tax 1 forms part of the base on which tax 2 is charged. The legacy guide notes
    /// this is unusual but required in some jurisdictions (p.77).
    /// </summary>
    public bool Tax2Compound { get; private set; }

    public bool AddOnChargeEnabled { get; private set; }

    /// <summary>Name of the percentage add-on, e.g. a service charge or eco fee.</summary>
    public string AddOnChargeName { get; private set; } = string.Empty;

    public Percentage AddOnChargeRate { get; private set; } = Percentage.Zero;

    /// <summary>Whether the add-on charge is itself subject to the sales taxes.</summary>
    public bool AddOnChargeTaxable { get; private set; }

    public TaxationType TaxationType { get; private set; } = TaxationType.Exclusive;

    /// <summary>Printed on invoices and sales slips where the jurisdiction requires it.</summary>
    public string? RegistrationNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public bool IsCurrentOn(DateOnly businessDate)
        => businessDate >= EffectiveFrom && (EffectiveTo is null || businessDate <= EffectiveTo);

    public static Result<TaxConfiguration> Create(
        long locationId,
        DateOnly effectiveFrom,
        bool tax1Enabled,
        string tax1Name,
        Percentage tax1Rate,
        bool tax2Enabled,
        string tax2Name,
        Percentage tax2Rate,
        bool tax2Compound,
        bool addOnChargeEnabled,
        string addOnChargeName,
        Percentage addOnChargeRate,
        bool addOnChargeTaxable,
        TaxationType taxationType,
        string? registrationNumber)
    {
        if (tax1Rate.Value < 0m || tax2Rate.Value < 0m || addOnChargeRate.Value < 0m)
        {
            return Result.Failure<TaxConfiguration>(RateInvalid);
        }

        if ((tax1Enabled && string.IsNullOrWhiteSpace(tax1Name))
            || (tax2Enabled && string.IsNullOrWhiteSpace(tax2Name))
            || (addOnChargeEnabled && string.IsNullOrWhiteSpace(addOnChargeName)))
        {
            return Result.Failure<TaxConfiguration>(NameRequired);
        }

        return Result.Success(new TaxConfiguration
        {
            LocationId = locationId,
            EffectiveFrom = effectiveFrom,
            Tax1Enabled = tax1Enabled,
            Tax1Name = tax1Name.Trim(),
            Tax1Rate = tax1Rate,
            Tax2Enabled = tax2Enabled,
            Tax2Name = tax2Name.Trim(),
            Tax2Rate = tax2Rate,
            Tax2Compound = tax2Compound,
            AddOnChargeEnabled = addOnChargeEnabled,
            AddOnChargeName = addOnChargeName.Trim(),
            AddOnChargeRate = addOnChargeRate,
            AddOnChargeTaxable = addOnChargeTaxable,
            TaxationType = taxationType,
            RegistrationNumber = registrationNumber,
        });
    }

    /// <summary>
    /// Closes this row the day before <paramref name="supersededFrom"/>. Called when an
    /// administrator schedules a rate change; the old row keeps serving historical documents.
    /// </summary>
    public Result Supersede(DateOnly supersededFrom)
    {
        if (supersededFrom <= EffectiveFrom)
        {
            return Result.Failure(new Error(
                "tax.supersede_before_effective",
                "A replacement tax configuration must start after the one it replaces."));
        }

        EffectiveTo = supersededFrom.AddDays(-1);
        return Result.Success();
    }
}
