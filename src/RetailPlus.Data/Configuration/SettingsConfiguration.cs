using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;
using RetailPlus.Data.Import;

namespace RetailPlus.Data.Configuration;

internal sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    public void Configure(EntityTypeBuilder<StoreSettings> e)
    {
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.CompanyName).HasMaxLength(30);
        e.Property(x => x.Address1).HasMaxLength(30);
        e.Property(x => x.Address2).HasMaxLength(30);
        e.Property(x => x.City).HasMaxLength(18);
        e.Property(x => x.State).HasMaxLength(15);
        e.Property(x => x.Zip).HasMaxLength(10);
        e.Property(x => x.Country).HasMaxLength(20);
        e.Property(x => x.Phone).HasMaxLength(16);
        e.Property(x => x.Fax).HasMaxLength(16);
        e.Property(x => x.TaxRegistrationNumber).HasMaxLength(25);
        e.Property(x => x.Tax1Name).HasMaxLength(20);
        e.Property(x => x.Tax2Name).HasMaxLength(20);
        e.Property(x => x.Tax1Rate).Rate();
        e.Property(x => x.Tax2Rate).Rate();
        e.Property(x => x.CashDiscountPercent).Rate();
        e.Property(x => x.ThcLimit).Quantity();
        e.Property(x => x.PointsPerDollar).Rate();
        e.Property(x => x.RewardPercent).Rate();
        e.Property(x => x.InterestRatePerMonth).Rate();
        e.ToTable(t => t.HasCheckConstraint("CK_StoreSettings_SingleRow", "[Id] = 1"));
    }
}

internal sealed class StoreSettingExtraConfiguration : IEntityTypeConfiguration<StoreSettingExtra>
{
    public void Configure(EntityTypeBuilder<StoreSettingExtra> e)
    {
        e.Property(x => x.Name).HasMaxLength(20);
        e.Property(x => x.Value).HasMaxLength(255);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> e)
    {
        e.Property(x => x.Name).HasMaxLength(50);
        e.HasIndex(x => x.Number).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_Station_Number", "[Number] BETWEEN 1 AND 999"));
    }
}

internal sealed class StationSettingConfiguration : IEntityTypeConfiguration<StationSetting>
{
    public void Configure(EntityTypeBuilder<StationSetting> e)
    {
        e.Property(x => x.Section).HasMaxLength(50);
        e.Property(x => x.Key).HasMaxLength(100);
        e.Property(x => x.Value).HasMaxLength(1000);
        e.HasOne<Station>().WithMany(x => x.Settings).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.StationId, x.Section, x.Key }).IsUnique();
    }
}

internal sealed class DocumentTextConfiguration : IEntityTypeConfiguration<DocumentText>
{
    public void Configure(EntityTypeBuilder<DocumentText> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        // One store-wide text per kind (NULL location) and one per location. No filter: SQL Server then treats
        // NULL as a single value, which is what keeps the store-wide text unique.
        e.HasIndex(x => new { x.Kind, x.LocationId }).IsUnique().HasFilter(null);
    }
}

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> e)
    {
        e.Property(x => x.UserName).HasMaxLength(15);
        e.Property(x => x.PasswordHash).HasMaxLength(200);
        e.HasIndex(x => x.UserName).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_UserAccount_AccessLevel", "[AccessLevel] BETWEEN 0 AND 4"));
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> e)
    {
        e.Property(x => x.SourcePath).HasMaxLength(260);
        e.Property(x => x.ToolVersion).HasMaxLength(50);
        e.Property(x => x.Status).HasMaxLength(20);
    }
}

internal sealed class ImportSourceRowConfiguration : IEntityTypeConfiguration<ImportSourceRow>
{
    public void Configure(EntityTypeBuilder<ImportSourceRow> e)
    {
        e.Property(x => x.Entity).HasMaxLength(50);
        e.Property(x => x.SourceFile).HasMaxLength(260);
        e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.Entity, x.EntityId });
    }
}
