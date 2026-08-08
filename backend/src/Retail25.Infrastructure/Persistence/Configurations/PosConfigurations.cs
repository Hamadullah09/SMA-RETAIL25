using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;

namespace Retail25.Infrastructure.Persistence.Configurations;

/// <summary>
/// Money is stored at four decimal places and quantity at four as well.
/// <para>
/// Two decimals would be wrong: a weighed item sells 1.243 kg, a fractional-cent unit price on a
/// bulk commodity is real, and rounding at storage time is precisely the penny-drift the pricing
/// engine goes to some trouble to avoid. Rounding happens at document and tender boundaries only.
/// </para>
/// </summary>
internal static class PrecisionConventions
{
    public const int MoneyPrecision = 19;
    public const int MoneyScale = 4;
    public const int QuantityPrecision = 18;
    public const int QuantityScale = 4;
    public const int CostScale = 3;
    public const int PercentPrecision = 7;
    public const int PercentScale = 2;
}

public sealed class CartAdjustmentConfiguration : IEntityTypeConfiguration<CartAdjustment>
{
    public void Configure(EntityTypeBuilder<CartAdjustment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Label).IsRequired().HasMaxLength(120);
        builder.Property(a => a.Serial).HasMaxLength(64);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(24);
        builder.Property(a => a.Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(a => a.Percent).HasPrecision(PrecisionConventions.PercentPrecision, PrecisionConventions.PercentScale);
        builder.HasIndex(a => a.CartId);
    }
}

public sealed class CartTaxOverrideConfiguration : IEntityTypeConfiguration<CartTaxOverride>
{
    public void Configure(EntityTypeBuilder<CartTaxOverride> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.CartId).IsUnique();
    }
}

public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasPrecision(PrecisionConventions.QuantityPrecision, PrecisionConventions.QuantityScale);
        builder.Property(l => l.ChargeableQuantity).HasPrecision(PrecisionConventions.QuantityPrecision, PrecisionConventions.QuantityScale);
        builder.Property(l => l.UnitPrice).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.ExtendedNet).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.ProratedAdjustment).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.TaxableNet).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.Tax1Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.Tax2Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(l => l.UnitCostSnapshot).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.CostScale);
        builder.Property(l => l.DiscountPct).HasPrecision(PrecisionConventions.PercentPrecision, PrecisionConventions.PercentScale);

        builder.Property(l => l.StockCodeSnapshot).HasMaxLength(24);
        builder.Property(l => l.NameSnapshot).HasMaxLength(200);
        builder.Property(l => l.Note).HasMaxLength(500);
        builder.Property(l => l.Epc).HasMaxLength(96);
        builder.Property(l => l.SerialNumber).HasMaxLength(64);

        builder.Property(l => l.PriceOrigin).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.LineType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Source).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(l => l.TransactionId);
        builder.HasIndex(l => l.ProductId);
    }
}

public sealed class SaleAdjustmentConfiguration : IEntityTypeConfiguration<SaleAdjustment>
{
    public void Configure(EntityTypeBuilder<SaleAdjustment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Label).IsRequired().HasMaxLength(120);
        builder.Property(a => a.Serial).HasMaxLength(64);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(24);
        builder.Property(a => a.Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.HasIndex(a => a.TransactionId);
    }
}

public sealed class SaleTenderConfiguration : IEntityTypeConfiguration<SaleTender>
{
    public void Configure(EntityTypeBuilder<SaleTender> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(t => t.AmountTendered).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(t => t.ChangeGiven).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(t => t.ExchangeRate).HasPrecision(18, 8);

        builder.Property(t => t.Behaviour).HasConversion<string>().HasMaxLength(24);
        builder.Property(t => t.Reference).HasMaxLength(64);
        builder.Property(t => t.AuthCode).HasMaxLength(32);
        builder.Property(t => t.CardLast4).HasMaxLength(4);
        builder.Property(t => t.GatewayReference).HasMaxLength(64);

        builder.HasIndex(t => t.TransactionId);
    }
}

public sealed class SaleTaxSnapshotConfiguration : IEntityTypeConfiguration<SaleTaxSnapshot>
{
    public void Configure(EntityTypeBuilder<SaleTaxSnapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Tax1Name).HasMaxLength(40);
        builder.Property(s => s.Tax2Name).HasMaxLength(40);
        builder.Property(s => s.AddOnName).HasMaxLength(40);
        builder.Property(s => s.TaxRegistrationNumber).HasMaxLength(40);
        builder.Property(s => s.Tax1Rate).HasPrecision(9, 4);
        builder.Property(s => s.Tax2Rate).HasPrecision(9, 4);
        builder.Property(s => s.AddOnRate).HasPrecision(9, 4);
        builder.HasIndex(s => s.TransactionId).IsUnique();
    }
}

public sealed class DrawerLedgerEntryConfiguration : IEntityTypeConfiguration<DrawerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<DrawerLedgerEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Amount).HasPrecision(PrecisionConventions.MoneyPrecision, PrecisionConventions.MoneyScale);
        builder.Property(e => e.EntryType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(200);
        builder.HasIndex(e => e.DrawerSessionId);
    }
}

public sealed class PricingRuleSettingConfiguration : IEntityTypeConfiguration<PricingRuleSetting>
{
    public void Configure(EntityTypeBuilder<PricingRuleSetting> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RuleKey).IsRequired().HasMaxLength(40);
        builder.Property(r => r.ParametersJson).HasColumnType("nvarchar(max)");

        // One row per rule per location: the ladder is an ordering of distinct rules, not a bag.
        builder.HasIndex(r => new { r.LocationId, r.RuleKey }).IsUnique();
    }
}

public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.StationCode).IsRequired().HasMaxLength(3);
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.AgentVersion).HasMaxLength(32);
        builder.Property(s => s.AgentTokenHash).HasMaxLength(128);
        builder.Property(s => s.ReaderMode).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(s => new { s.LocationId, s.StationCode }).IsUnique();
        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class ReaderProfileConfiguration : IEntityTypeConfiguration<ReaderProfile>
{
    public void Configure(EntityTypeBuilder<ReaderProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Host).IsRequired().HasMaxLength(120);
        builder.Property(p => p.AntennaZones).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Protocol).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(p => new { p.LocationId, p.StationId });
    }
}

public sealed class PrinterProfileConfiguration : IEntityTypeConfiguration<PrinterProfile>
{
    public void Configure(EntityTypeBuilder<PrinterProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Port).HasMaxLength(120);
        builder.Property(p => p.SetupCommand).HasMaxLength(120);
        builder.Property(p => p.CutterCommand).HasMaxLength(120);
        builder.Property(p => p.RedCommand).HasMaxLength(120);
        builder.Property(p => p.BlackCommand).HasMaxLength(120);
        builder.Property(p => p.DrawerTrigger).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Output).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(p => new { p.LocationId, p.StationId });
    }
}

public sealed class ScaleProfileConfiguration : IEntityTypeConfiguration<ScaleProfile>
{
    public void Configure(EntityTypeBuilder<ScaleProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Port).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Parity).IsRequired().HasMaxLength(16);
        builder.Property(p => p.StopBits).IsRequired().HasMaxLength(16);
        builder.Property(p => p.GetWeightCommand).IsRequired().HasMaxLength(16);
        builder.Property(p => p.ZeroCommand).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Unit).IsRequired().HasMaxLength(8);
        builder.HasIndex(p => new { p.LocationId, p.StationId });
    }
}

public sealed class PoleDisplayProfileConfiguration : IEntityTypeConfiguration<PoleDisplayProfile>
{
    public void Configure(EntityTypeBuilder<PoleDisplayProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Port).IsRequired().HasMaxLength(60);
        builder.Property(p => p.IdleLine1).HasMaxLength(60);
        builder.Property(p => p.IdleLine2).HasMaxLength(60);
        builder.Property(p => p.ClearCommand).HasMaxLength(60);
        builder.Property(p => p.Line1Command).HasMaxLength(60);
        builder.Property(p => p.Line2Command).HasMaxLength(60);
        builder.HasIndex(p => new { p.LocationId, p.StationId });
    }
}

public sealed class SerializedUnitConfiguration : IEntityTypeConfiguration<Domain.Catalog.SerializedUnit>
{
    public void Configure(EntityTypeBuilder<Domain.Catalog.SerializedUnit> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Epc).HasMaxLength(96);
        builder.Property(u => u.SerialNumber).HasMaxLength(64);
        builder.Property(u => u.State).HasConversion<string>().HasMaxLength(20);

        // One EPC is one physical unit, so the mapping has to be unique — a partial index because
        // stores that track serials without RFID leave the column null.
        builder.HasIndex(u => u.Epc).IsUnique().HasFilter("[epc] IS NOT NULL");
        builder.HasIndex(u => new { u.ProductId, u.State });
    }
}
