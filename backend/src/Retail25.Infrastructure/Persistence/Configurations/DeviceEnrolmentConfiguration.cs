using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Persistence.Configurations;

public sealed class DeviceEnrolmentConfiguration : IEntityTypeConfiguration<DeviceEnrolment>
{
    public void Configure(EntityTypeBuilder<DeviceEnrolment> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RedeemedByHostname).HasMaxLength(200);

        // Redemption looks the token up by hash and by nothing else, so this index is the whole
        // read path. Unique because two enrolments sharing a hash would mean the random source
        // repeated itself, which is worth a constraint violation rather than a coin toss over which
        // device the caller just enrolled as.
        builder.HasIndex(e => e.TokenHash).IsUnique();

        builder.HasIndex(e => e.DeviceId);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(e => e.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(e => e.DomainEvents);
    }
}
