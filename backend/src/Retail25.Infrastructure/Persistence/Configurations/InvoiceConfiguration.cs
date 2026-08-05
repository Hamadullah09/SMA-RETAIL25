using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Receivables;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceTotal)
            .HasPrecision(19, 4);

        builder.Property(i => i.PenaltyAccrued)
            .HasPrecision(19, 4);

        builder.Property(i => i.BalanceDue)
            .HasPrecision(19, 4);

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(i => new { i.CustomerId, i.Status })
            .HasFilter("[status] = 'Open'");

        builder.HasIndex(i => i.InvoiceNumber);

        builder.Ignore(i => i.DomainEvents);
    }
}
