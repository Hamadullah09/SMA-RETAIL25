using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Terminals;
using Retail25.Domain.Trolleys;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class TrolleyConfiguration : IEntityTypeConfiguration<Trolley>
{
    public void Configure(EntityTypeBuilder<Trolley> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).IsRequired().HasMaxLength(6);
        builder.Property(t => t.Label).HasMaxLength(100);

        // Codes are printed on handles and only ever read inside one shop, so they are unique per
        // location rather than globally — two branches may both have a trolley 482.
        builder.HasIndex(t => new { t.LocationId, t.Code }).IsUnique();

        // A station backs exactly one trolley. Without this, two trolleys could be pointed at the same
        // station row, and since a station holds one active cart the second shopper to claim would be
        // handed the first one's basket.
        builder.HasIndex(t => t.StationId).IsUnique();

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(t => t.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(t => t.DomainEvents);
    }
}

public class TrolleySessionConfiguration : IEntityTypeConfiguration<TrolleySession>
{
    public void Configure(EntityTypeBuilder<TrolleySession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.State)
            .HasConversion<string>()
            .HasMaxLength(20);

        // One live session per trolley, decided by the database rather than by a handler.
        //
        // Two phones typing 482 in the same second both read "no live session" and both insert; a
        // check-then-insert in application code cannot see the other transaction's uncommitted row.
        // The loser of that race would silently join the winner's shopping trip and watch their items
        // appear on someone else's bill. A filtered unique index turns it into a constraint violation
        // the pairing handler catches and reports as "someone is already shopping with this trolley".
        builder.HasIndex(s => new { s.TrolleyId, s.State })
            .IsUnique()
            .HasFilter("[state] = 'Shopping'");

        // And one live session per shopper, for the same reason in the other direction: a shopper who
        // walks away and claims a second trolley must be told, not quietly given two open baskets
        // neither of which shows the other's items.
        builder.HasIndex(s => new { s.ShopperId, s.State })
            .IsUnique()
            .HasFilter("[state] = 'Shopping'");

        // The abandonment sweep's query: live sessions ordered by how long they have been quiet.
        builder.HasIndex(s => new { s.State, s.LastActivityAt });

        // How a shopper's own cart is found on every request they make.
        builder.HasIndex(s => s.CartId);

        builder.Ignore(s => s.DomainEvents);
    }
}
