using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;
using RetailPlus.Data.Import;

namespace RetailPlus.Data.Configuration;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> e)
    {
        e.Property(x => x.Code).HasMaxLength(10);
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.TimeZoneId).HasMaxLength(64);
        e.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    public void Configure(EntityTypeBuilder<StoreSettings> e)
    {
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
        // One settings row per store.
        e.HasIndex(x => x.StoreId).IsUnique();
    }
}

internal sealed class StoreSettingExtraConfiguration : IEntityTypeConfiguration<StoreSettingExtra>
{
    public void Configure(EntityTypeBuilder<StoreSettingExtra> e)
    {
        e.Property(x => x.Name).HasMaxLength(20);
        e.Property(x => x.Value).HasMaxLength(255);
        e.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();
    }
}

internal sealed class NumberCounterConfiguration : IEntityTypeConfiguration<NumberCounter>
{
    public void Configure(EntityTypeBuilder<NumberCounter> e)
    {
        e.Property(x => x.Kind).HasMaxLength(30);
        e.HasIndex(x => new { x.Kind, x.StationNumber }).IsUnique();
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
        // One store-wide text per kind (no location) and one per location. Two filtered indexes, because SQLite treats
        // NULLs in a unique index as all different.
        e.HasIndex(x => new { x.StoreId, x.Kind }).IsUnique().HasFilter("[LocationId] IS NULL");
        e.HasIndex(x => new { x.Kind, x.LocationId }).IsUnique().HasFilter("[LocationId] IS NOT NULL");
    }
}

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> e)
    {
        e.Property(x => x.UserName).HasMaxLength(50);
        e.Property(x => x.FullName).HasMaxLength(100);
        e.Property(x => x.PasswordHash).HasMaxLength(200);
        e.Property(x => x.StaffCode).HasMaxLength(10);
        e.Property(x => x.PinHash).HasMaxLength(200);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.HasIndex(x => x.UserName).IsUnique();
        e.HasIndex(x => x.StaffCode).IsUnique().HasFilter("[StaffCode] IS NOT NULL");
        e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_UserAccount_AccessLevel", "[AccessLevel] BETWEEN 0 AND 4"));
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> e)
    {
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.Description).HasMaxLength(200);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        e.ToTable(t => t.HasCheckConstraint("CK_Role_Level", "[Level] BETWEEN 0 AND 4"));
    }
}

internal sealed class RoleGrantConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> e)
    {
        e.Property(x => x.Permission).HasMaxLength(50);
        e.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();
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
