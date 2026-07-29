using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>
/// Stable keys for the unit-price precedence ladder (doc 04 §2). The engine dispatches on these
/// strings; the order and enablement of each rule are rows, so a store that wants sale pricing to
/// beat break points reorders two rows rather than shipping a release (decision P1).
/// </summary>
public static class PricingRuleKeys
{
    public const string ManualOverride = "manual";
    public const string RandomWeight = "randomWeight";
    public const string BonusPricing = "bonus";
    public const string VolumeBreak = "break";
    public const string RequestedLevel = "requestedLevel";
    public const string ClientLevel = "clientLevel";
    public const string SaleWindow = "sale";
    public const string RegularPrice = "regular";

    /// <summary>
    /// The seeded order, which is exactly the ladder documented in doc 04 §2. Nothing in the engine
    /// assumes it — it is what <see cref="PricingRuleSetting.SeedDefaults"/> writes on first run.
    /// </summary>
    public static IReadOnlyList<string> DefaultOrder { get; } =
    [
        ManualOverride,
        RandomWeight,
        BonusPricing,
        VolumeBreak,
        RequestedLevel,
        ClientLevel,
        SaleWindow,
        RegularPrice,
    ];
}

/// <summary>
/// One rung of the unit-price precedence ladder, stored per location.
/// <para>
/// The standing build constraint is that no pricing precedence is compiled in. This row carries the
/// rule key, its position, whether it is on, and a free-form JSON parameter bag for rules that need
/// tuning (for example a minimum quantity floor on bonus pricing).
/// </para>
/// </summary>
public sealed class PricingRuleSetting : Entity, IAuditable
{
    public static readonly Error RuleKeyRequired = new("pricing_rule.key_required", "A pricing rule needs a key.");

    private PricingRuleSetting()
    {
    }

    public Guid LocationId { get; private set; }

    /// <summary>One of <see cref="PricingRuleKeys"/>. Unknown keys are ignored by the resolver.</summary>
    public string RuleKey { get; private set; } = string.Empty;

    /// <summary>Evaluation position. Lower runs first; the first rule that matches wins.</summary>
    public int Order { get; private set; }

    public bool Enabled { get; private set; } = true;

    /// <summary>Rule-specific parameters as JSONB. Null for rules that take none.</summary>
    public string? ParametersJson { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<PricingRuleSetting> Create(Guid locationId, string ruleKey, int order, bool enabled = true, string? parametersJson = null)
    {
        if (string.IsNullOrWhiteSpace(ruleKey))
        {
            return Result.Failure<PricingRuleSetting>(RuleKeyRequired);
        }

        return Result.Success(new PricingRuleSetting
        {
            LocationId = locationId,
            RuleKey = ruleKey.Trim(),
            Order = order,
            Enabled = enabled,
            ParametersJson = parametersJson,
        });
    }

    public void Reorder(int order) => Order = order;

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public void SetParameters(string? parametersJson) => ParametersJson = parametersJson;

    /// <summary>The working default ladder for a new location, in the documented order.</summary>
    public static IReadOnlyList<PricingRuleSetting> SeedDefaults(Guid locationId)
    {
        var settings = new List<PricingRuleSetting>(PricingRuleKeys.DefaultOrder.Count);
        for (var i = 0; i < PricingRuleKeys.DefaultOrder.Count; i++)
        {
            settings.Add(Create(locationId, PricingRuleKeys.DefaultOrder[i], (i + 1) * 10).Value);
        }

        return settings;
    }
}
