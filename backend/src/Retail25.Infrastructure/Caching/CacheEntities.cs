using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Retail25.Infrastructure.Caching;

/*
 * The four things Redis held, as tables.
 *
 * These are not domain aggregates and deliberately have no DbSet on ApplicationDbContext: nothing
 * should reach them through LINQ. Every access goes through the stores in this folder, which use
 * raw SQL because the operations they need — claim-if-free, redeem-once — are single atomic
 * statements in SQL Server and are not expressible through change tracking without a race.
 *
 * They are still EF entities, because that is what puts them in `dotnet ef migrations script` and
 * therefore in the deployment. A cache table created by hand on one server is a cache table that
 * does not exist on the next one.
 *
 * Table and column names are set explicitly rather than left to the snake_case convention. The
 * stores below name these tables in string literals, so a convention change that renamed a column
 * would turn into a runtime error on a till instead of a compile error here.
 */

/// <summary>
/// A cart in flight, and the till that is running it.
/// <para>
/// Redis used two keys — the snapshot and a station pointer. One row carries both: the pointer is
/// <see cref="IsActive"/>, which is what makes "the cart at this station" a query rather than a
/// second key that can disagree with the first.
/// </para>
/// </summary>
internal sealed class CachedCart
{
    public long CartId { get; set; }

    public long StationId { get; set; }

    /// <summary>
    /// Whether this cart still owns its station. A completed or suspended cart sets this false so
    /// the next customer can start a sale, exactly as the Redis version deleted the station key.
    /// </summary>
    public bool IsActive { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Ordering for the station lookup, so the newest cart wins a tie.</summary>
    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>
/// One row per claimed EPC. The row is the claim; its absence or expiry is the release.
/// </summary>
internal sealed class CachedTagClaim
{
    public string Epc { get; set; } = string.Empty;

    public long StationId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A stored response, so a repeated Idempotency-Key does not take the money twice.</summary>
internal sealed class CachedIdempotencyEntry
{
    public string IdempotencyKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A single-use ticket for opening one SignalR connection.</summary>
internal sealed class CachedHubTicket
{
    public string Ticket { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class CachedCartConfiguration : IEntityTypeConfiguration<CachedCart>
{
    public void Configure(EntityTypeBuilder<CachedCart> builder)
    {
        builder.ToTable(CacheTables.Cart);
        builder.HasKey(c => c.CartId);

        builder.Property(c => c.CartId).HasColumnName("cart_id").ValueGeneratedNever();
        builder.Property(c => c.StationId).HasColumnName("station_id");
        builder.Property(c => c.IsActive).HasColumnName("is_active");
        builder.Property(c => c.Payload).HasColumnName("payload").IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.SavedAt).HasColumnName("saved_at");

        // Serves the station lookup, which runs on every till poll. Filtered, because the only
        // rows it ever reads are the active ones and a shop accumulates far more finished carts
        // than live ones.
        builder.HasIndex(c => new { c.StationId, c.SavedAt })
            .HasDatabaseName("ix_cached_cart_station_active")
            .HasFilter("[is_active] = 1");

        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName("ix_cached_cart_expires_at");
    }
}

internal sealed class CachedTagClaimConfiguration : IEntityTypeConfiguration<CachedTagClaim>
{
    public void Configure(EntityTypeBuilder<CachedTagClaim> builder)
    {
        builder.ToTable(CacheTables.TagClaim);
        builder.HasKey(t => t.Epc);

        // The primary key is the arbitration. Two tills claiming one tag are two inserts of the
        // same key, and SQL Server settles it rather than the application.
        builder.Property(t => t.Epc).HasColumnName("epc").HasMaxLength(128).ValueGeneratedNever();
        builder.Property(t => t.StationId).HasColumnName("station_id");
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_cached_tag_claim_expires_at");
    }
}

internal sealed class CachedIdempotencyEntryConfiguration : IEntityTypeConfiguration<CachedIdempotencyEntry>
{
    public void Configure(EntityTypeBuilder<CachedIdempotencyEntry> builder)
    {
        builder.ToTable(CacheTables.IdempotencyEntry);
        builder.HasKey(e => e.IdempotencyKey);

        // Not "key": KEY is a reserved word in T-SQL, and every raw statement touching it would
        // need bracketing that is easy to forget in exactly one place.
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256).ValueGeneratedNever();
        builder.Property(e => e.Payload).HasColumnName("payload").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_cached_idempotency_entry_expires_at");
    }
}

internal sealed class CachedHubTicketConfiguration : IEntityTypeConfiguration<CachedHubTicket>
{
    public void Configure(EntityTypeBuilder<CachedHubTicket> builder)
    {
        builder.ToTable(CacheTables.HubTicket);
        builder.HasKey(t => t.Ticket);

        builder.Property(t => t.Ticket).HasColumnName("ticket").HasMaxLength(128).ValueGeneratedNever();
        builder.Property(t => t.Payload).HasColumnName("payload").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_cached_hub_ticket_expires_at");
    }
}

/// <summary>Table names, in one place, because the raw SQL below spells them out.</summary>
internal static class CacheTables
{
    internal const string Cart = "cached_cart";
    internal const string TagClaim = "cached_tag_claim";
    internal const string IdempotencyEntry = "cached_idempotency_entry";
    internal const string HubTicket = "cached_hub_ticket";
}
