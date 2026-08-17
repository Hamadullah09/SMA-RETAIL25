using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Shoppers;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class ShopperConfiguration : IEntityTypeConfiguration<Shopper>
{
    public void Configure(EntityTypeBuilder<Shopper> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.LastName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Phone).IsRequired().HasMaxLength(30);
        builder.Property(s => s.Email).IsRequired().HasMaxLength(200);
        builder.Property(s => s.NormalizedEmail).IsRequired().HasMaxLength(200);
        builder.Property(s => s.PasswordHash).IsRequired().HasMaxLength(400);

        // One account per address. The uniqueness is on the normalised column rather than on Email,
        // because "Amina@gmail.com" and "amina@gmail.com" are one person to everybody except a
        // case-sensitive index.
        builder.HasIndex(s => s.NormalizedEmail).IsUnique();

        // Not unique. Households share a number, and a shopper locked out because a relative
        // registered first would have no way to discover why.
        builder.HasIndex(s => s.Phone);

        builder.Ignore(s => s.FullName);
        builder.Ignore(s => s.DomainEvents);
    }
}

public class ShopperDeviceConfiguration : IEntityTypeConfiguration<ShopperDevice>
{
    public void Configure(EntityTypeBuilder<ShopperDevice> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.DeviceName).IsRequired().HasMaxLength(100);
        builder.Property(d => d.RefreshTokenHash).IsRequired().HasMaxLength(200);

        // One row per phone per account, so signing in again on a handset the shopper already used
        // rotates that phone's token instead of accumulating a new row on every sign-in.
        builder.HasIndex(d => new { d.ShopperId, d.DeviceId }).IsUnique();

        // The refresh path looks a device up by the hash it was handed, and only then checks whose it
        // is. Indexed because that lookup happens on every cold start of the app.
        builder.HasIndex(d => d.RefreshTokenHash);

        builder.HasOne<Shopper>()
            .WithMany()
            .HasForeignKey(d => d.ShopperId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
