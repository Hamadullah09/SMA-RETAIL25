using Retail25.Domain.Common;

namespace Retail25.Domain.Staff;

/// <summary>
/// Clock-in / clock-out entry (guide p.75–76). Hours are computed from the pair.
/// </summary>
public sealed class TimeClockEntry : Entity, IAuditable
{
    private TimeClockEntry()
    {
    }

    public Guid StaffId { get; set; }

    public Guid LocationId { get; set; }

    public DateTimeOffset ClockIn { get; set; }

    public DateTimeOffset? ClockOut { get; set; }

    /// <summary>Computed hours. Null while the staff member is still clocked in.</summary>
    public decimal? HoursWorked { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static TimeClockEntry ClockInAt(Guid staffId, Guid locationId, DateTimeOffset at)
    {
        return new TimeClockEntry
        {
            StaffId = staffId,
            LocationId = locationId,
            ClockIn = at,
        };
    }

    public void ClockOutAt(DateTimeOffset at)
    {
        ClockOut = at;
        HoursWorked = (decimal)(at - ClockIn).TotalHours;
    }
}
