using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

// Phase E4: CRM, marketing, promotions and targets. Owners and users are kept without a foreign key (like the audit log),
// so deleting a user leaves their leads and tasks in place.

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> e)
    {
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.Company).HasMaxLength(80);
        e.Property(x => x.Phone).HasMaxLength(30);
        e.Property(x => x.WhatsAppNumber).HasMaxLength(20);
        e.Property(x => x.Email).HasMaxLength(100);
        e.Property(x => x.Source).HasMaxLength(40);
        e.Property(x => x.Notes).HasMaxLength(2000);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ConvertedClientId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StoreId, x.Status });
        e.HasIndex(x => x.OwnerId);
    }
}

internal sealed class OpportunityStageConfiguration : IEntityTypeConfiguration<OpportunityStage>
{
    public void Configure(EntityTypeBuilder<OpportunityStage> e)
    {
        e.Property(x => x.Name).HasMaxLength(40);
        e.Property(x => x.Probability).Rate();
        e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(8);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> e)
    {
        e.Property(x => x.Title).HasMaxLength(120);
        e.Property(x => x.Probability).Rate();
        e.Property(x => x.LostReason).HasMaxLength(200);
        e.Property(x => x.Notes).HasMaxLength(2000);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OpportunityStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StoreId, x.StageId });
        e.HasIndex(x => x.OwnerId);
        e.HasIndex(x => x.ClosedOn);
    }
}

internal sealed class CrmActivityConfiguration : IEntityTypeConfiguration<CrmActivity>
{
    public void Configure(EntityTypeBuilder<CrmActivity> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Subject).HasMaxLength(120);
        e.Property(x => x.Notes).HasMaxLength(2000);
        e.Property(x => x.DoneAtUtc).HasPrecision(3);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.OwnerId, x.IsDone, x.DueAt });
        e.HasIndex(x => new { x.StoreId, x.DueAt });
    }
}

internal sealed class SalesTargetConfiguration : IEntityTypeConfiguration<SalesTarget>
{
    public void Configure(EntityTypeBuilder<SalesTarget> e)
    {
        // One target per store (or salesperson at the store) and month; the service checks it, since SQLite and SQL
        // Server treat a null salesperson differently in a unique index.
        e.HasIndex(x => new { x.StoreId, x.Year, x.Month, x.UserId });
    }
}

internal sealed class CustomerSegmentConfiguration : IEntityTypeConfiguration<CustomerSegment>
{
    public void Configure(EntityTypeBuilder<CustomerSegment> e)
    {
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.Description).HasMaxLength(400);
        e.Property(x => x.RulesJson).HasMaxLength(4000);
        e.Property(x => x.LastEvaluatedAtUtc).HasPrecision(3);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Name);
    }
}

internal sealed class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> e)
    {
        e.Property(x => x.Name).HasMaxLength(512);
        e.Property(x => x.Language).HasMaxLength(10);
        e.Property(x => x.Category).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Body).HasMaxLength(1024);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.HasIndex(x => new { x.Name, x.Language }).IsUnique();
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> e)
    {
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.ScheduledAtUtc).HasPrecision(3);
        e.Property(x => x.StartedAtUtc).HasPrecision(3);
        e.Property(x => x.FinishedAtUtc).HasPrecision(3);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasOne<CustomerSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<WhatsAppTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.Status, x.ScheduledAtUtc });
    }
}

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> e)
    {
        e.Property(x => x.ToNumber).HasMaxLength(20);
        e.Property(x => x.ParametersJson).HasMaxLength(2000);
        e.Property(x => x.Body).HasMaxLength(2000);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Error).HasMaxLength(400);
        e.Property(x => x.ProviderMessageId).HasMaxLength(128);
        e.Property(x => x.SentBy).HasMaxLength(50);
        e.Property(x => x.SentAtUtc).HasPrecision(3);
        e.Property(x => x.DeliveredAtUtc).HasPrecision(3);
        e.Property(x => x.ReadAtUtc).HasPrecision(3);
        e.Property(x => x.FailedAtUtc).HasPrecision(3);
        e.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<WhatsAppTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.CampaignId, x.Status });
        e.HasIndex(x => x.ProviderMessageId);
        e.HasIndex(x => new { x.ClientId, x.CreatedAtUtc });
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(400);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.Property(x => x.AllStores).HasDefaultValue(true);
        e.Property(x => x.Percent).Rate();
        e.Property(x => x.GetPercent).Rate();
        e.Property(x => x.CouponCode).HasMaxLength(30);
        e.HasMany(x => x.Targets).WithOne().HasForeignKey(t => t.PromotionId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Stores).WithOne().HasForeignKey(s => s.PromotionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.CouponCode);
    }
}

internal sealed class PromotionTargetConfiguration : IEntityTypeConfiguration<PromotionTarget>
{
    public void Configure(EntityTypeBuilder<PromotionTarget> e)
    {
        e.Property(x => x.Role).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.Plu).HasMaxLength(20);
        e.Property(x => x.Quantity).Quantity();
        e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PromotionStoreConfiguration : IEntityTypeConfiguration<PromotionStore>
{
    public void Configure(EntityTypeBuilder<PromotionStore> e)
    {
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.PromotionId, x.StoreId }).IsUnique();
    }
}

/// <summary>The promotion columns of a sale line (the rest of the line is in <c>SaleLineConfiguration</c>).</summary>
internal sealed class SaleLinePromotionConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> e)
    {
        e.Property(x => x.PromotionName).HasMaxLength(60);
        e.HasIndex(x => x.PromotionId);
    }
}

/// <summary>A client's WhatsApp number and consent (the rest of the client is in <c>ClientConfiguration</c>).</summary>
internal sealed class ClientWhatsAppConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> e)
    {
        e.Property(x => x.WhatsAppNumber).HasMaxLength(20);
        e.Property(x => x.WhatsAppOptInAt).HasPrecision(3);
    }
}

internal sealed class CouponUseConfiguration : IEntityTypeConfiguration<CouponUse>
{
    public void Configure(EntityTypeBuilder<CouponUse> e)
    {
        // No foreign keys to the sale or the promotion: every till keeps every store's uses, without their sales.
        e.Property(x => x.Code).HasMaxLength(30);
        e.HasIndex(x => x.PromotionId);
        e.HasIndex(x => x.SaleId);
    }
}
