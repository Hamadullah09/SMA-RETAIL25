using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class TombstoneConfiguration : IEntityTypeConfiguration<Tombstone>
{
    public void Configure(EntityTypeBuilder<Tombstone> e)
    {
        e.Property(x => x.EntityName).HasMaxLength(100);
        e.Property(x => x.DeletedAtUtc).HasPrecision(3);
        e.HasIndex(x => new { x.EntityName, x.RowId });
    }
}

internal sealed class SyncBatchConfiguration : IEntityTypeConfiguration<SyncBatch>
{
    public void Configure(EntityTypeBuilder<SyncBatch> e)
    {
        e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.FileName).HasMaxLength(260);
        e.Property(x => x.FileSha256).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.AppVersion).HasMaxLength(40);
        e.Property(x => x.ReceivedAtUtc).HasPrecision(3);
        e.Property(x => x.AppliedAtUtc).HasPrecision(3);
        e.HasIndex(x => x.BatchId);
        e.HasIndex(x => new { x.StoreId, x.StationNumber, x.ReceivedAtUtc });
        e.HasIndex(x => x.Status);
    }
}

internal sealed class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> e)
    {
        e.Property(x => x.PackagedThroughUtc).HasPrecision(3);
        e.Property(x => x.SentThroughUtc).HasPrecision(3);
        e.Property(x => x.LastSentAtUtc).HasPrecision(3);
        e.Property(x => x.LastReceivedAtUtc).HasPrecision(3);
        e.Property(x => x.LastAttemptAtUtc).HasPrecision(3);
    }
}

internal sealed class StationLinkConfiguration : IEntityTypeConfiguration<StationLink>
{
    public void Configure(EntityTypeBuilder<StationLink> e)
    {
        e.Property(x => x.ProtectedKey).HasMaxLength(1000).IsUnicode(false);
        e.Property(x => x.EnrollmentCodeHash).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.AppVersion).HasMaxLength(40);
        e.HasOne<Station>().WithOne().HasForeignKey<StationLink>(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.StationNumber).IsUnique();
        e.HasIndex(x => x.EnrollmentCodeHash);
    }
}
