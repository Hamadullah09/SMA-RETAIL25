using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.Security;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Stamps audit columns, turns a delete of a soft-deletable entity into an update, and writes an
/// <see cref="AuditLogEntry"/> for every change to money, stock, price, tax or permissions
/// (doc 07 §Audit).
/// <para>
/// It lives in an interceptor rather than in handlers for one reason: "who changed this and when"
/// must never be a question the system cannot answer, and a rule applied by hand in twenty handlers
/// is a rule that will be forgotten in the twenty-first.
/// </para>
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Entity types whose changes are recorded in full. Not everything is audited: a cart line
    /// changes on every keystroke of a quantity, and burying the void of a $500 sale under ten
    /// thousand cart edits would make the trail useless.
    /// </summary>
    private static readonly HashSet<string> AuditedTypes = new(StringComparer.Ordinal)
    {
        "Product", "ProductPrice", "PriceBreak", "SalePricing", "BonusPricing",
        "SalesTransaction", "SaleLine", "SaleTender",
        "StockLevel", "StockLedgerEntry", "StockTransfer", "StockCount",
        "TaxConfiguration", "PosPolicy", "PricingRuleSetting", "TenderType", "Currency",
        "Invoice", "InvoicePayment", "ARLedgerEntry", "GiftCertificate",
        "CustomerAccount", "LoyaltyLedgerEntry",
        "DrawerSession", "DrawerLedgerEntry",
        "StaffProfile", "RolePermission", "Permission",
        "SerializedUnit", "Station", "ReaderProfile", "PrinterProfile",
    };

    /// <summary>Columns never written to an audit diff, whatever entity they appear on.</summary>
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "PinHash", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
        "AgentTokenHash", "RefreshTokenFamily", "BootstrapSecret",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IDateTime _clock;

    public AuditingInterceptor(ICurrentUser currentUser, IRequestContext requestContext, IDateTime clock)
    {
        _currentUser = currentUser;
        _requestContext = requestContext;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.Now;
        var actor = _currentUser.UserId;

        // Materialised before anything is added: adding audit rows mutates the change tracker, and
        // enumerating it while it changes would throw.
        var entries = context.ChangeTracker.Entries().ToList();
        var auditRows = new List<AuditLogEntry>();

        foreach (var entry in entries)
        {
            // An audit row must never itself be audited, or one change becomes an infinite regress.
            if (entry.Entity is AuditLogEntry)
            {
                continue;
            }

            var wasDeleted = entry.State == EntityState.Deleted;

            ApplyAudit(entry, now, actor);
            ApplySoftDelete(entry, now, actor);

            if (BuildAuditRow(entry, wasDeleted, now) is { } row)
            {
                auditRows.Add(row);
            }
        }

        if (auditRows.Count > 0)
        {
            context.Set<AuditLogEntry>().AddRange(auditRows);
        }
    }

    private static void ApplyAudit(EntityEntry entry, DateTimeOffset now, Guid? actor)
    {
        if (entry.Entity is not IAuditable auditable)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                // A handler that set CreatedAt deliberately — a migration, a backdated import —
                // keeps it.
                if (auditable.CreatedAt == default)
                {
                    auditable.CreatedAt = now;
                }

                auditable.CreatedBy ??= actor;
                break;

            case EntityState.Modified:
                auditable.ModifiedAt = now;
                auditable.ModifiedBy = actor;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Rewrites a hard delete into a soft one (guide p.24, "Undelete Items"). Retail data is named by
    /// history: destroying a product row would orphan every sale line that mentioned it.
    /// </summary>
    private static void ApplySoftDelete(EntityEntry entry, DateTimeOffset now, Guid? actor)
    {
        if (entry.State != EntityState.Deleted || entry.Entity is not ISoftDeletable deletable)
        {
            return;
        }

        entry.State = EntityState.Modified;
        deletable.IsDeleted = true;
        deletable.DeletedAt = now;
        deletable.DeletedBy = actor;
    }

    private AuditLogEntry? BuildAuditRow(EntityEntry entry, bool wasDeleted, DateTimeOffset now)
    {
        var typeName = entry.Entity.GetType().Name;

        if (!AuditedTypes.Contains(typeName))
        {
            return null;
        }

        var action = wasDeleted
            ? AuditAction.Deleted
            : entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                _ => (AuditAction?)null,
            };

        if (action is not { } auditAction)
        {
            return null;
        }

        var (before, after) = Diff(entry, auditAction);

        // A "modification" where nothing meaningful changed — only audit stamps — is noise.
        if (auditAction == AuditAction.Updated && before is null && after is null)
        {
            return null;
        }

        var row = AuditLogEntry
            .For(auditAction, typeName, now, IdOf(entry), _requestContext.CorrelationId)
            .WithActor(
                _currentUser.UserId,
                _currentUser.StaffId,
                null,
                _currentUser.StationId,
                _currentUser.LocationId,
                _requestContext.IpAddress,
                _requestContext.CorrelationId);

        row.BeforeJson = before;
        row.AfterJson = after;

        return row;
    }

    /// <summary>
    /// Only the columns that actually changed, so a diff is readable. Storing whole entities would
    /// make a one-field price change indistinguishable from a wholesale rewrite.
    /// </summary>
    private static (string? Before, string? After) Diff(EntityEntry entry, AuditAction action)
    {
        var before = new Dictionary<string, object?>(StringComparer.Ordinal);
        var after = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;

            if (RedactedProperties.Contains(name) || IsAuditColumn(name))
            {
                continue;
            }

            switch (action)
            {
                case AuditAction.Created:
                    after[name] = property.CurrentValue;
                    break;

                case AuditAction.Deleted:
                    before[name] = property.OriginalValue;
                    break;

                default:
                    if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                    {
                        before[name] = property.OriginalValue;
                        after[name] = property.CurrentValue;
                    }

                    break;
            }
        }

        return (
            before.Count == 0 ? null : JsonSerializer.Serialize(before, SerializerOptions),
            after.Count == 0 ? null : JsonSerializer.Serialize(after, SerializerOptions));
    }

    /// <summary>The stamps this interceptor writes itself are not a change worth recording.</summary>
    private static bool IsAuditColumn(string name) => name is
        "CreatedAt" or "CreatedBy" or "ModifiedAt" or "ModifiedBy" or "RowVersion";

    private static string? IdOf(EntityEntry entry)
        => entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString();
}
