using Retail25.Domain.Common;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.Configuration;

/// <summary>
/// A store, warehouse or sales counter that holds stock and makes sales. The legacy system called
/// these "inventories" or "Locations" and identified each by a three-character code
/// (user guide p.3, p.44); that code is preserved as <see cref="LegacyCode"/> so migrated data,
/// old purchase-order file names and printed reports still line up.
/// </summary>
public sealed class Location : AggregateRoot, IAuditable, ISoftDeletable
{
    public static readonly Error LegacyCodeInvalid = new(
        "location.legacy_code_invalid",
        "A location code must be one to three alphanumeric characters.");

    private Location()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Three-character code inherited from Retail Plus, e.g. <c>TST</c>.</summary>
    public string LegacyCode { get; private set; } = string.Empty;

    public Address Address { get; private set; } = Address.Empty;

    public ContactDetails Contact { get; private set; } = ContactDetails.Empty;

    /// <summary>
    /// IANA time zone used to derive the business date from a UTC timestamp. A sale rung at 00:30
    /// belongs to the business day the store says it does, not to whatever the server's clock says.
    /// </summary>
    public string TimeZoneId { get; private set; } = "UTC";

    /// <summary>
    /// Local time at which the business day rolls over. Stores that trade past midnight set this
    /// to their closing time so drawer totals and daily reports group the way staff expect.
    /// </summary>
    public TimeOnly BusinessDayStart { get; private set; } = TimeOnly.MinValue;

    public string BaseCurrencyCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Result<Location> Create(
        string name,
        string legacyCode,
        string baseCurrencyCode,
        string timeZoneId,
        TimeOnly businessDayStart)
    {
        if (string.IsNullOrWhiteSpace(legacyCode) || legacyCode.Trim().Length > 3
            || !legacyCode.Trim().All(char.IsLetterOrDigit))
        {
            return Result.Failure<Location>(LegacyCodeInvalid.With("value", legacyCode));
        }

        var location = new Location
        {
            Name = name.Trim(),
            LegacyCode = legacyCode.Trim().ToUpperInvariant(),
            BaseCurrencyCode = baseCurrencyCode.Trim().ToUpperInvariant(),
            TimeZoneId = timeZoneId,
            BusinessDayStart = businessDayStart,
        };

        return Result.Success(location);
    }

    public void UpdateDetails(string name, Address address, ContactDetails contact)
    {
        Name = name.Trim();
        Address = address;
        Contact = contact;
    }

    public void UpdateCalendar(string timeZoneId, TimeOnly businessDayStart)
    {
        TimeZoneId = timeZoneId;
        BusinessDayStart = businessDayStart;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// Converts an instant to the business date for this location, honouring both its time zone
    /// and its configured day-start.
    /// </summary>
    public DateOnly BusinessDateFor(DateTimeOffset instant)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var shifted = local - BusinessDayStart.ToTimeSpan();
        return DateOnly.FromDateTime(shifted.DateTime);
    }
}
