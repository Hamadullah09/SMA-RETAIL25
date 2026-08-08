using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>
/// What the system must do when a tender is taken. Behaviour is selected by data, so adding a new
/// way to pay is an administrative act, not a release.
/// </summary>
public enum TenderBehaviour
{
    /// <summary>Physical money: opens the drawer, accepts over-tender, gives change, rounds to the smallest coin.</summary>
    Cash = 0,

    /// <summary>Routed to the payment gateway for authorisation; returns an auth code and masked card.</summary>
    Card = 1,

    /// <summary>Balance held on a gift card; reduces the card balance and prints the remainder on the receipt.</summary>
    GiftCard = 2,

    /// <summary>A serial-numbered paper certificate redeemed at face value.</summary>
    GiftCertificate = 3,

    /// <summary>Charged to the customer's account, creating an accounts-receivable invoice.</summary>
    OnAccount = 4,

    /// <summary>Recorded with a reference number; no online authorisation.</summary>
    Manual = 5,
}

/// <summary>
/// A way of paying. The legacy system let merchants edit this list (user guide p.17) and required
/// the names to match the accounting system exactly (p.110); both needs are met by keeping tenders
/// as rows with an explicit external mapping key.
/// </summary>
public sealed class TenderType : AggregateRoot, IAuditable, ISoftDeletable
{
    public static readonly Error NameRequired = new("tender_type.name_required", "A tender type needs a name.");
    public static readonly Error CodeRequired = new("tender_type.code_required", "A tender type needs a stable code.");

    private TenderType()
    {
    }

    /// <summary>Stable machine key, e.g. <c>CASH</c>. Used by seed data, imports and integrations.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the cashier sees on the button.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public TenderBehaviour Behaviour { get; private set; }

    /// <summary>Ordering of the payment buttons at the till. Lower sorts first.</summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Icon key resolved by the front end against its own icon map. Storing a key rather than
    /// markup keeps presentation out of the database while still being configurable.
    /// </summary>
    public string? IconKey { get; private set; }

    /// <summary>Pop the cash drawer when this tender is used.</summary>
    public bool OpensCashDrawer { get; private set; }

    /// <summary>Accept more than the amount due and return the difference as change.</summary>
    public bool AllowsOverTender { get; private set; }

    /// <summary>Round the amount to the currency's smallest coin (cash behaviour).</summary>
    public bool RoundsToMinimumTender { get; private set; }

    /// <summary>Counted as cash when the drawer is reconciled at close.</summary>
    public bool CountsTowardsDrawerCash { get; private set; }

    /// <summary>Require a reference (cheque number, authorisation code) before the tender is accepted.</summary>
    public bool RequiresReference { get; private set; }

    /// <summary>Print an extra signature copy of the receipt (legacy card behaviour, p.79).</summary>
    public bool PrintsSignatureCopy { get; private set; }

    /// <summary>Available for refunds as well as sales.</summary>
    public bool AllowedForRefunds { get; private set; } = true;

    /// <summary>Only this currency may be tendered; null means the location's base currency.</summary>
    public string? CurrencyCode { get; private set; }

    /// <summary>Name or id this tender maps to in the accounting system (p.110).</summary>
    public string? ExternalAccountingKey { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public long? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static Result<TenderType> Create(
        string code,
        string displayName,
        TenderBehaviour behaviour,
        int sortOrder,
        string? iconKey = null,
        string? currencyCode = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<TenderType>(CodeRequired);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<TenderType>(NameRequired);
        }

        var tender = new TenderType
        {
            Code = code.Trim().ToUpperInvariant(),
            DisplayName = displayName.Trim(),
            Behaviour = behaviour,
            SortOrder = sortOrder,
            IconKey = iconKey,
            CurrencyCode = currencyCode?.ToUpperInvariant(),
        };

        tender.ApplyBehaviourDefaults();
        return Result.Success(tender);
    }

    /// <summary>
    /// Sets the capability switches that normally accompany a behaviour. These are starting values
    /// an administrator can override individually — the engine reads the switches, never the enum.
    /// </summary>
    private void ApplyBehaviourDefaults()
    {
        switch (Behaviour)
        {
            case TenderBehaviour.Cash:
                OpensCashDrawer = true;
                AllowsOverTender = true;
                RoundsToMinimumTender = true;
                CountsTowardsDrawerCash = true;
                break;

            case TenderBehaviour.Card:
                PrintsSignatureCopy = true;
                break;

            case TenderBehaviour.GiftCard:
            case TenderBehaviour.GiftCertificate:
                RequiresReference = true;
                break;

            case TenderBehaviour.OnAccount:
                AllowedForRefunds = false;
                break;

            case TenderBehaviour.Manual:
                RequiresReference = true;
                break;

            default:
                break;
        }
    }

    public void UpdateCapabilities(
        bool opensCashDrawer,
        bool allowsOverTender,
        bool roundsToMinimumTender,
        bool countsTowardsDrawerCash,
        bool requiresReference,
        bool printsSignatureCopy,
        bool allowedForRefunds)
    {
        OpensCashDrawer = opensCashDrawer;
        AllowsOverTender = allowsOverTender;
        RoundsToMinimumTender = roundsToMinimumTender;
        CountsTowardsDrawerCash = countsTowardsDrawerCash;
        RequiresReference = requiresReference;
        PrintsSignatureCopy = printsSignatureCopy;
        AllowedForRefunds = allowedForRefunds;
    }

    public void UpdatePresentation(string displayName, int sortOrder, string? iconKey)
    {
        DisplayName = displayName.Trim();
        SortOrder = sortOrder;
        IconKey = iconKey;
    }

    public void MapToAccounting(string? externalKey) => ExternalAccountingKey = externalKey;

    public void SetActive(bool isActive) => IsActive = isActive;
}
