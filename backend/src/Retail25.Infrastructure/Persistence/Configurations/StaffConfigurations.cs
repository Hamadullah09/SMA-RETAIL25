using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Staff;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class TimeClockEntryConfiguration : IEntityTypeConfiguration<TimeClockEntry>
{
    public void Configure(EntityTypeBuilder<TimeClockEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.HoursWorked).HasPrecision(9, 4);

        // The hours report reads a window per person, and the open-shift lookup reads the one row
        // with no clock-out. Both are this index.
        builder.HasIndex(e => new { e.StaffId, e.ClockIn });
        builder.HasIndex(e => new { e.LocationId, e.ClockIn });
    }
}

public sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CommissionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Value).HasPrecision(9, 4);
        builder.Property(e => e.MaxCommission).HasPrecision(19, 2);

        builder.Ignore(e => e.Specificity);

        builder.HasIndex(e => e.StaffId);

        // One rule per (person, scope). Two rules for the same item would make what someone earns
        // depend on which row the query happened to return first.
        builder.HasIndex(e => new { e.StaffId, e.ProductId, e.DepartmentId }).IsUnique();
    }
}

public sealed class CommissionLedgerEntryConfiguration : IEntityTypeConfiguration<CommissionLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CommissionLedgerEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CommissionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.StockCodeSnapshot).HasMaxLength(30);
        builder.Property(e => e.RateApplied).HasPrecision(9, 4);
        builder.Property(e => e.LineNet).HasPrecision(19, 2);
        builder.Property(e => e.LineCost).HasPrecision(19, 3);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Amount).HasPrecision(19, 2);

        // The commissions report is always "this person, this period", so that is the index.
        builder.HasIndex(e => new { e.StaffId, e.BusinessDate });
        builder.HasIndex(e => new { e.LocationId, e.BusinessDate });
        builder.HasIndex(e => e.TransactionId);
    }
}
