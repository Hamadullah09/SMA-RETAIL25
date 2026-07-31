using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Inventory;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.ArchivedNetSales).HasPrecision(19, 2);

        // One record per year per store. Two rows for 2026 would mean two answers to "is it closed".
        builder.HasIndex(e => new { e.LocationId, e.Year }).IsUnique();
        builder.HasIndex(e => new { e.LocationId, e.StartsOn });
    }
}

public sealed class SalesHistoryArchiveConfiguration : IEntityTypeConfiguration<SalesHistoryArchive>
{
    public void Configure(EntityTypeBuilder<SalesHistoryArchive> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StockCodeSnapshot).HasMaxLength(30);
        builder.Property(e => e.NameSnapshot).HasMaxLength(200);
        builder.Property(e => e.QuantitySold).HasPrecision(18, 4);
        builder.Property(e => e.NetSales).HasPrecision(19, 2);
        builder.Property(e => e.CostOfGoodsSold).HasPrecision(19, 3);

        builder.Ignore(e => e.GrossMargin);

        builder.HasIndex(e => e.FiscalYearId);

        // The question this table exists to answer is "how did this line do in that month", so that
        // is the index — and it is unique, which is what makes re-running a close idempotent rather
        // than doubling every figure.
        builder.HasIndex(e => new { e.LocationId, e.Year, e.Month, e.ProductId }).IsUnique();
    }
}
