using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceKey).IsRequired().HasMaxLength(40);
        builder.Property(d => d.Name).HasMaxLength(120);
        builder.Property(d => d.Hostname).HasMaxLength(200);
        builder.Property(d => d.OperatingSystem).HasMaxLength(200);
        builder.Property(d => d.AgentVersion).HasMaxLength(40);

        // Several, comma separated: a machine on both wired and wireless has two, and which one it
        // reaches a reader on is not knowable from here. Stored for a human to read, never parsed.
        builder.Property(d => d.LocalIpAddresses).HasMaxLength(400);

        // Unique per location rather than globally: two shops may both call a machine PC-001, and
        // making them collide would mean renaming one shop's estate to onboard another.
        builder.HasIndex(d => new { d.LocationId, d.DeviceKey }).IsUnique();

        builder.Ignore(d => d.DomainEvents);
    }
}

public sealed class RfidReaderConfiguration : IEntityTypeConfiguration<RfidReader>
{
    public void Configure(EntityTypeBuilder<RfidReader> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReaderKey).IsRequired().HasMaxLength(40);
        builder.Property(r => r.SerialNumber).HasMaxLength(80);
        builder.Property(r => r.Model).HasMaxLength(120);
        builder.Property(r => r.Host).HasMaxLength(200);
        builder.Property(r => r.Protocol).HasConversion<int>();

        builder.HasIndex(r => new { r.LocationId, r.ReaderKey }).IsUnique();

        // The serial is the hardware's own identity, so two readers claiming the same one is a
        // configuration error worth refusing rather than discovering later through crossed reads.
        // Filtered, because the protocols that do not report a serial leave it null and many nulls
        // are not a collision.
        builder.HasIndex(r => r.SerialNumber)
            .IsUnique()
            .HasFilter("[serial_number] IS NOT NULL");

        // Restrict, not cascade: deleting a machine must not silently take its readers — and with
        // them their antenna assignments and therefore their stations — out of the system.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(r => r.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(r => r.DomainEvents);
    }
}

public sealed class ReaderAntennaAssignmentConfiguration : IEntityTypeConfiguration<ReaderAntennaAssignment>
{
    public void Configure(EntityTypeBuilder<ReaderAntennaAssignment> builder)
    {
        builder.HasKey(a => a.Id);

        // The constraint the whole model rests on.
        //
        // One physical antenna feeds exactly one station. Enforced here rather than in a handler
        // because a check-then-save in application code does not survive two administrators saving
        // at the same moment, and the failure it would let through — one antenna quietly feeding two
        // tills — is silent: both tills ring the same garment and neither looks wrong.
        builder.HasIndex(a => new { a.ReaderId, a.AntennaNumber }).IsUnique();

        // Routing reads this on every batch, keyed by reader.
        builder.HasIndex(a => a.ReaderId);

        builder.HasOne<RfidReader>()
            .WithMany()
            .HasForeignKey(a => a.ReaderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a station that an antenna still points at must not be deleted out from under it,
        // or reads would resolve to nothing and be reported as unassigned when they are misconfigured.
        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(a => a.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(a => a.DomainEvents);
    }
}
