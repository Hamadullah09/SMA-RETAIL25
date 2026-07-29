using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Group).IsRequired().HasMaxLength(40);
        builder.HasIndex(p => p.Key).IsUnique();
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rp => rp.Id);
        builder.Property(rp => rp.PermissionKey).IsRequired().HasMaxLength(60);

        // A grant is a set membership, not a list: granting the same permission twice is a bug, and
        // the constraint makes a re-run of the seeder harmless rather than duplicating rows.
        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionKey }).IsUnique();
    }
}

/// <summary>
/// The audit trail (doc 07 §Audit).
/// <para>
/// Indexed for the three questions a review actually asks: what happened in this window, what did
/// this person do, and what happened to this record. The correlation index is what makes "show me
/// everything that one request did" a single seek rather than a scan.
/// </para>
/// </summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(24);
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(80);
        builder.Property(e => e.EntityId).HasMaxLength(64);
        builder.Property(e => e.Operation).HasMaxLength(120);
        builder.Property(e => e.ActorName).HasMaxLength(200);
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.CorrelationId).HasMaxLength(128);
        builder.Property(e => e.Reason).HasMaxLength(500);

        // JSONB so a diff can be queried, not just displayed: "which sales had their price
        // overridden last month" is a real question and it should not need a table scan of strings.
        builder.Property(e => e.BeforeJson).HasColumnType("jsonb");
        builder.Property(e => e.AfterJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.ActorStaffId, e.OccurredAt });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.CorrelationId);
    }
}

public sealed class SupervisorApprovalConfiguration : IEntityTypeConfiguration<SupervisorApproval>
{
    public void Configure(EntityTypeBuilder<SupervisorApproval> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Permission).IsRequired().HasMaxLength(60);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(120);
        builder.Property(a => a.Context).HasMaxLength(500);
        builder.Property(a => a.DenialReason).HasMaxLength(500);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // The supervisor's screen asks one question — what is waiting here right now — so that is
        // the index that exists.
        builder.HasIndex(a => new { a.LocationId, a.Status, a.ExpiresAt });
    }
}

public sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StaffCode).IsRequired().HasMaxLength(8);
        builder.Property(s => s.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.LastName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.PinHash).HasMaxLength(256);

        builder.Ignore(s => s.FullName);

        builder.HasIndex(s => s.StaffCode).IsUnique();
        builder.HasIndex(s => s.UserId).IsUnique();
    }
}
