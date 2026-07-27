using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Purchasing;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Company)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.SupplierNumber)
            .HasMaxLength(20);

        builder.Property(s => s.ContactFirstName)
            .HasMaxLength(100);

        builder.Property(s => s.ContactLastName)
            .HasMaxLength(100);

        builder.Property(s => s.Title)
            .HasMaxLength(50);

        builder.OwnsOne(s => s.Address, a =>
        {
            a.Property(addr => addr.Line1).HasMaxLength(200);
            a.Property(addr => addr.Line2).HasMaxLength(200);
            a.Property(addr => addr.City).HasMaxLength(100);
            a.Property(addr => addr.StateOrProvince).HasMaxLength(100);
            a.Property(addr => addr.PostalCode).HasMaxLength(20);
            a.Property(addr => addr.Country).HasMaxLength(100);
        });

        builder.OwnsOne(s => s.Contact, c =>
        {
            c.Property(ct => ct.Phone).HasMaxLength(30);
            c.Property(ct => ct.Extension).HasMaxLength(10);
            c.Property(ct => ct.Mobile).HasMaxLength(30);
            c.Property(ct => ct.Fax).HasMaxLength(30);
            c.Property(ct => ct.Email).HasMaxLength(200);
            c.Property(ct => ct.Website).HasMaxLength(200);
        });

        builder.Ignore(s => s.DomainEvents);
    }
}
