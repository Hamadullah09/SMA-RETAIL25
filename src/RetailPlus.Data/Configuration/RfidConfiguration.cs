using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class RfidReaderConfiguration : IEntityTypeConfiguration<RfidReader>
{
    public void Configure(EntityTypeBuilder<RfidReader> e)
    {
        e.Property(x => x.Name).HasMaxLength(40);
        e.Property(x => x.HubKey).HasMaxLength(20);
        e.Property(x => x.Location).HasMaxLength(60);
        e.Property(x => x.Transport).HasConversion<string>().HasMaxLength(8);
        e.Property(x => x.Host).HasMaxLength(100);
        e.Property(x => x.SerialPort).HasMaxLength(20);
        e.Property(x => x.Region).HasConversion<string>().HasMaxLength(8);
        e.Property(x => x.BeeperMode).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.LinkProfile).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.FirmwareVersion).HasMaxLength(20);
        e.Property(x => x.Notes).HasMaxLength(500);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.Ignore(x => x.Endpoint);
        e.HasMany(x => x.Antennas).WithOne().HasForeignKey(a => a.ReaderId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasIndex(x => x.HubKey);
    }
}

internal sealed class RfidAntennaConfiguration : IEntityTypeConfiguration<RfidAntenna>
{
    public void Configure(EntityTypeBuilder<RfidAntenna> e)
    {
        e.Property(x => x.Name).HasMaxLength(40);
        e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(16);
        e.HasIndex(x => new { x.ReaderId, x.Port }).IsUnique();
        e.HasIndex(x => x.StationNumber);
    }
}

internal sealed class ItemTagConfiguration : IEntityTypeConfiguration<ItemTag>
{
    public void Configure(EntityTypeBuilder<ItemTag> e)
    {
        e.Property(x => x.Epc).HasMaxLength(128).IsUnicode(false);
        e.Property(x => x.SerialNumber).HasMaxLength(50);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        // Every lookup at the counter is by EPC; one tag belongs to one item.
        e.HasIndex(x => x.Epc).IsUnique();
        e.HasIndex(x => new { x.ItemId, x.Status });
    }
}
