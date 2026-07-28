using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class BusinessProfileConfiguration : IEntityTypeConfiguration<BusinessProfile>
{
    public void Configure(EntityTypeBuilder<BusinessProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(b => b.Id);

        builder.Property(b => b.BusinessName)
            .IsRequired()
            .HasMaxLength(200);

        builder.OwnsOne(b => b.Address, a => a.MapAddress());
        builder.OwnsOne(b => b.Contact, c => c.MapContact());

        builder.Ignore(b => b.DomainEvents);
    }
}
