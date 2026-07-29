using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(s => s.Prefix)
            .HasMaxLength(10);

        // One row per kind per location. Two rows for the same counter would mean the sequence's
        // starting point depended on which one was read first.
        builder.HasIndex(s => new { s.LocationId, s.Kind })
            .IsUnique();
    }
}
