using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> e)
    {
        e.Property(x => x.AtUtc).HasPrecision(3);
        e.Property(x => x.UserName).HasMaxLength(50);
        e.Property(x => x.Action).HasMaxLength(40);
        e.Property(x => x.EntityName).HasMaxLength(60);
        e.Property(x => x.Details).HasMaxLength(400);
        // No foreign key to the user: the log outlives deleted users and an import that replaces them.
        e.HasIndex(x => x.AtUtc);
        e.HasIndex(x => new { x.UserId, x.AtUtc });
        e.HasIndex(x => new { x.Action, x.AtUtc });
        e.HasIndex(x => new { x.EntityName, x.EntityId });
    }
}

internal sealed class TimeClockEntryConfiguration : IEntityTypeConfiguration<TimeClockEntry>
{
    public void Configure(EntityTypeBuilder<TimeClockEntry> e)
    {
        e.Property(x => x.UserName).HasMaxLength(50);
        e.Property(x => x.EditReason).HasMaxLength(200);
        e.Property(x => x.EditedBy).HasMaxLength(50);
        e.Ignore(x => x.IsOpen);
        e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.UserId, x.ClockIn });
        e.HasIndex(x => x.ClockIn);
    }
}

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Basis).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Rate).Rate();
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
