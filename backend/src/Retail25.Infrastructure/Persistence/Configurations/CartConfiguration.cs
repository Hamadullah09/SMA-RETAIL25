using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Sales;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.HeldName)
            .HasMaxLength(100);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(c => new { c.StationId, c.Status })
            .HasFilter("status = 'Active'");

        builder.Ignore(c => c.DomainEvents);
    }
}
