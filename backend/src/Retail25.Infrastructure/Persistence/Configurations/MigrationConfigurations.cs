using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Migration;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class MigrationBatchConfiguration : IEntityTypeConfiguration<MigrationBatch>
{
    public void Configure(EntityTypeBuilder<MigrationBatch> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Stage).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SourceFileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.Entity).HasMaxLength(30).IsRequired();
        builder.Property(e => e.SourceHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.Property(e => e.AnalysisJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ValidationJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ReconciliationJson).HasColumnType("nvarchar(max)");

        builder.Ignore(e => e.CanImport);

        builder.HasIndex(e => new { e.LocationId, e.Stage });

        // Not unique: re-uploading the same file after fixing something in the source is legitimate.
        // The index is here so "have we seen this file before" is a cheap question to ask.
        builder.HasIndex(e => e.SourceHash);
    }
}

public sealed class MigrationStagingRowConfiguration : IEntityTypeConfiguration<MigrationStagingRow>
{
    public void Configure(EntityTypeBuilder<MigrationStagingRow> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.LegacyKey).HasMaxLength(60);
        builder.Property(e => e.Problems).HasMaxLength(2000);
        builder.Property(e => e.Outcome).HasMaxLength(200);

        builder.HasIndex(e => new { e.BatchId, e.RowNumber });

        // Duplicate-key detection reads this, and so does the row-addressable error report.
        builder.HasIndex(e => new { e.BatchId, e.LegacyKey });
    }
}
