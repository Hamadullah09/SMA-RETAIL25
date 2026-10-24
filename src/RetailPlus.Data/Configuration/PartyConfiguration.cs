using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

/// <summary>Columns and indexes shared by Client and Supplier, following CLIENTS.DBF / SUPPLIER.DBF and their tags.</summary>
internal static class PartyColumns
{
    public static void Configure<T>(EntityTypeBuilder<T> e) where T : Party
    {
        e.Property(x => x.FirstName).HasMaxLength(12);
        e.Property(x => x.LastName).HasMaxLength(16);
        e.Property(x => x.Title).HasMaxLength(30);
        e.Property(x => x.Company).HasMaxLength(30);
        e.Property(x => x.Address1).HasMaxLength(30);
        e.Property(x => x.Address2).HasMaxLength(30);
        e.Property(x => x.City).HasMaxLength(18);
        e.Property(x => x.State).HasMaxLength(15);
        e.Property(x => x.Zip).HasMaxLength(10);
        e.Property(x => x.Country).HasMaxLength(20);
        e.Property(x => x.WorkPhone).HasMaxLength(16);
        e.Property(x => x.Extension).HasMaxLength(4);
        e.Property(x => x.Fax).HasMaxLength(16);
        e.Property(x => x.HomePhone).HasMaxLength(16);
        e.Property(x => x.Email).HasMaxLength(100);
        e.Property(x => x.Website).HasMaxLength(100);
        e.Property(x => x.Number).HasMaxLength(11);
        e.Property(x => x.AccountNumber).HasMaxLength(10);
        e.Property(x => x.Type).HasMaxLength(15);
        e.Property(x => x.PhotoPath).HasMaxLength(260);
        e.Property(x => x.RowVersion).IsRowVersion();

        // Tags: CLIENTS/SUPPLIER (last name), COMPANY, CUSTNO, ZIP, STATE (state+city), LASTPURCHA (descending),
        // EMAIL, WORKPHONE, CLIENTTYPE, ACCTNO. Numbers must be unique among live rows.
        e.HasIndex(x => x.LastName);
        e.HasIndex(x => x.Company);
        e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL AND [IsDeleted] = 0");
        e.HasIndex(x => x.AccountNumber).IsUnique().HasFilter("[AccountNumber] IS NOT NULL AND [IsDeleted] = 0");
        e.HasIndex(x => x.Zip);
        e.HasIndex(x => new { x.State, x.City });
        e.HasIndex(x => x.LastPurchase).IsDescending();
        e.HasIndex(x => x.Email);
        e.HasIndex(x => x.WorkPhone);
        e.HasIndex(x => x.Type);
    }
}

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> e)
    {
        PartyColumns.Configure(e);
        e.Property(x => x.UsualDiscountPercent).Rate();
        e.Property(x => x.RecurringItemCode).HasMaxLength(16);
        e.Property(x => x.RecurringUnit).HasMaxLength(6);
        e.ToTable(t => t.HasCheckConstraint("CK_Client_PriceLevel", "[PriceLevel] BETWEEN 0 AND 4"));
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> e) => PartyColumns.Configure(e);
}
