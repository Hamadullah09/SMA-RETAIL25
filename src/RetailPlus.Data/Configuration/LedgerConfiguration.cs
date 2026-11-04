using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> e)
    {
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Role).HasMaxLength(40);
        e.Property(x => x.Description).HasMaxLength(400);
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => x.Role).IsUnique().HasFilter("[Role] IS NOT NULL");
        e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Source).HasMaxLength(100);
        e.Property(x => x.Memo).HasMaxLength(200);
        e.Property(x => x.Reference).HasMaxLength(40);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.PostedBy).HasMaxLength(50);
        e.HasIndex(x => x.Source).IsUnique().HasFilter("[Source] IS NOT NULL");
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.Date, x.Status });
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> e)
    {
        e.Property(x => x.Debit).HasPrecision(18, 2);
        e.Property(x => x.Credit).HasPrecision(18, 2);
        e.Property(x => x.Memo).HasMaxLength(200);
        e.Property(x => x.PartyKind).HasConversion<string>().HasMaxLength(10);
        e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.AccountId, x.StoreId });
        e.HasIndex(x => new { x.PartyKind, x.PartyId });
    }
}

internal sealed class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> e)
    {
        e.Property(x => x.Name).HasMaxLength(20);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.ClosedBy).HasMaxLength(50);
        e.HasIndex(x => x.StartDate).IsUnique();
        e.HasMany(x => x.Periods).WithOne().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> e)
    {
        e.Property(x => x.Name).HasMaxLength(20);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.HasIndex(x => new { x.FiscalYearId, x.Number }).IsUnique();
        e.HasIndex(x => x.StartDate);
    }
}

internal sealed class LedgerSettingsConfiguration : IEntityTypeConfiguration<LedgerSettings>
{
    public void Configure(EntityTypeBuilder<LedgerSettings> e)
    {
        e.Property(x => x.BaseCurrency).HasMaxLength(3);
        e.Property(x => x.Country).HasMaxLength(2);
        e.ToTable(t => t.HasCheckConstraint("CK_LedgerSettings_Month", "[FiscalYearStartMonth] BETWEEN 1 AND 12"));
    }
}
