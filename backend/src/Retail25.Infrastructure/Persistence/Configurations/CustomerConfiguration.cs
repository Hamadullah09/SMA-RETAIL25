using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Customers;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Company)
            .HasMaxLength(200);

        builder.Property(c => c.Title)
            .HasMaxLength(50);

        builder.Property(c => c.ClientType)
            .HasMaxLength(50);

        builder.Property(c => c.Notes)
            .HasMaxLength(4000);

        builder.OwnsOne(c => c.BillingAddress, a =>
        {
            a.Property(addr => addr.Line1).HasMaxLength(200);
            a.Property(addr => addr.Line2).HasMaxLength(200);
            a.Property(addr => addr.City).HasMaxLength(100);
            a.Property(addr => addr.StateOrProvince).HasMaxLength(100);
            a.Property(addr => addr.PostalCode).HasMaxLength(20);
            a.Property(addr => addr.Country).HasMaxLength(100);
        });

        builder.OwnsOne(c => c.ShipToAddress, a =>
        {
            a.Property(addr => addr.Line1).HasMaxLength(200);
            a.Property(addr => addr.Line2).HasMaxLength(200);
            a.Property(addr => addr.City).HasMaxLength(100);
            a.Property(addr => addr.StateOrProvince).HasMaxLength(100);
            a.Property(addr => addr.PostalCode).HasMaxLength(20);
            a.Property(addr => addr.Country).HasMaxLength(100);
        });

        builder.OwnsOne(c => c.Contact, c2 =>
        {
            c2.Property(ct => ct.Phone).HasMaxLength(30);
            c2.Property(ct => ct.Extension).HasMaxLength(10);
            c2.Property(ct => ct.Mobile).HasMaxLength(30);
            c2.Property(ct => ct.Fax).HasMaxLength(30);
            c2.Property(ct => ct.Email).HasMaxLength(200);
            c2.Property(ct => ct.Website).HasMaxLength(200);
        });

        builder.HasIndex(c => new { c.LocationId, c.CustomerNumber })
            .IsUnique();

        builder.Ignore(c => c.DomainEvents);
    }
}
