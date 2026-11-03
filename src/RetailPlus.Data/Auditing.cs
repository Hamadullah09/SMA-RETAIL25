using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RetailPlus.Core.Model;

namespace RetailPlus.Data;

/// <summary>The actions of the audit log: rows added, changed and deleted, and the events the program writes itself.</summary>
public static class AuditActions
{
    public const string Added = "Added";
    public const string Changed = "Changed";
    public const string Deleted = "Deleted";
    public const string Undeleted = "Undeleted";
    public const string SignIn = "Sign in";
    public const string FailedSignIn = "Failed sign-in";
    public const string Approval = "Approval";
    public const string ApprovalRefused = "Approval refused";
    public const string VoidSale = "Void sale";
    public const string VoidInvoice = "Void invoice";
    public const string NoSale = "No sale";
    public const string EndOfDay = "End of day";
    public const string Backup = "Backup";
    public const string Restore = "Restore";
    public const string Import = "Import";
    public const string YearEnd = "Year end";
    public const string TimeClock = "Time clock";
    public const string TimeClockEdit = "Time clock edit";
    public const string GiftCardAdjust = "Gift card adjust";
    public const string LeadConverted = "Lead converted";
    public const string OpportunityWon = "Opportunity won";
    public const string OpportunityLost = "Opportunity lost";
    public const string CampaignScheduled = "Campaign scheduled";
    public const string CampaignCancelled = "Campaign cancelled";
    public const string CampaignSent = "Campaign sent";
    public const string WhatsAppMessage = "WhatsApp message";
    public const string WhatsAppConsent = "WhatsApp consent";
}

/// <summary>
/// The tables whose changes the DbContext writes to the audit log by itself, each with how its rows are named in the log.
/// To audit another table, add a line here.
/// </summary>
public static class AuditedTables
{
    private static readonly Dictionary<Type, Func<object, string?>> Tables = new()
    {
        [typeof(Item)] = Name<Item>(i => $"{i.Plu} {i.Name}"),
        [typeof(Client)] = Name<Client>(c => !string.IsNullOrWhiteSpace(c.Company) ? c.Company : $"{c.FirstName} {c.LastName}".Trim()),
        [typeof(Supplier)] = Name<Supplier>(s => s.Company),
        [typeof(UserAccount)] = Name<UserAccount>(u => u.UserName),
        [typeof(Role)] = Name<Role>(r => r.Name),
        [typeof(RoleGrant)] = Name<RoleGrant>(g => g.Permission),
        [typeof(StoreSettings)] = Name<StoreSettings>(s => s.CompanyName),
        [typeof(StoreSettingExtra)] = Name<StoreSettingExtra>(x => x.Name),
        [typeof(TenderType)] = Name<TenderType>(t => t.Name),
        [typeof(TaxConfiguration)] = Name<TaxConfiguration>(t => t.Name),
        [typeof(FxRate)] = Name<FxRate>(r => r.Currency),
        [typeof(Station)] = Name<Station>(s => $"Station {s.Number}"),
        [typeof(StationSetting)] = Name<StationSetting>(s => $"{s.Section} / {s.Key}"),
        [typeof(Location)] = Name<Location>(l => l.Code),
        [typeof(Department)] = Name<Department>(d => d.Name),
        [typeof(Category)] = Name<Category>(c => c.Name),
        [typeof(DocumentText)] = Name<DocumentText>(t => t.Kind.ToString()),
        [typeof(FastKey)] = Name<FastKey>(k => $"Panel {k.Panel} key {k.KeyNumber}"),
        [typeof(RfidReader)] = Name<RfidReader>(r => r.Name),
        [typeof(CommissionRule)] = Name<CommissionRule>(r => r.Name),
        [typeof(GiftCard)] = Name<GiftCard>(c => c.Number),
        [typeof(AccountMapping)] = Name<AccountMapping>(m => $"{m.Kind} {m.Key}"),
        [typeof(StoreImage)] = Name<StoreImage>(i => i.Kind.ToString()),
        [typeof(Lead)] = Name<Lead>(l => l.Name),
        [typeof(OpportunityStage)] = Name<OpportunityStage>(s => s.Name),
        [typeof(Opportunity)] = Name<Opportunity>(o => o.Title),
        [typeof(SalesTarget)] = Name<SalesTarget>(t => $"{t.Year}-{t.Month:00}"),
        [typeof(CustomerSegment)] = Name<CustomerSegment>(s => s.Name),
        [typeof(WhatsAppTemplate)] = Name<WhatsAppTemplate>(t => $"{t.Name} ({t.Language})"),
        [typeof(Campaign)] = Name<Campaign>(c => c.Name),
        [typeof(Promotion)] = Name<Promotion>(p => p.Name),
        [typeof(PromotionTarget)] = Name<PromotionTarget>(t => t.Plu ?? t.Role.ToString()),
        [typeof(PromotionStore)] = Name<PromotionStore>(_ => "Store"),
    };

    /// <summary>Properties never written to the log: kept by the program, or marks that are not changes of substance.</summary>
    internal static readonly HashSet<string> Ignored =
    [
        nameof(ITracked.CreatedAtUtc), nameof(ITracked.UpdatedAtUtc), nameof(IVersioned.Version), nameof(IFlaggable.IsFlagged),
        nameof(Item.LastUpdated), nameof(UserAccount.FailedSignIns), nameof(UserAccount.LockedUntilUtc),
    ];

    /// <summary>Properties whose values are never written, only that they changed.</summary>
    internal static readonly HashSet<string> Secret = [nameof(UserAccount.PasswordHash), nameof(UserAccount.PinHash)];

    public static IReadOnlyCollection<Type> All => Tables.Keys;

    public static bool Contains(Type type) => Tables.ContainsKey(type);

    /// <summary>The row's name for the log (an item's code and name, a user's name).</summary>
    public static string? Describe(object entity) => Tables.TryGetValue(entity.GetType(), out var name) ? name(entity) : null;

    private static Func<object, string?> Name<T>(Func<T, string?> name) => o => o is T t ? name(t) : null;
}

public partial class RetailPlusDbContext
{
    private static readonly JsonSerializerOptions AuditJson = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// True while rows are written that must not reach the audit log one by one: a sync package being applied, an import.
    /// Only the rows the DbContext would audit by itself are held back; <see cref="Audit"/> still writes.
    /// </summary>
    public bool SuppressAudit { get; set; }

    /// <summary>The user the audit log names for changes saved through this context (null before anyone signed in).</summary>
    public Guid? ActingUserId { get; set; }

    public string? ActingUserName { get; set; }

    /// <summary>
    /// Adds a line to the audit log, saved with the next SaveChanges. It names the acting user unless a user is given.
    /// Nothing is written by a context without a station (the log belongs to a store).
    /// </summary>
    public AuditEntry? Audit(string action, string? details = null, string? entityName = null, Guid? entityId = null,
        IReadOnlyDictionary<string, object?>? before = null, IReadOnlyDictionary<string, object?>? after = null,
        Guid? userId = null, string? userName = null)
    {
        if (Station is null) return null;
        bool givenUser = userId is not null || userName is not null;
        var entry = new AuditEntry
        {
            AtUtc = SyncColumns.Now(),   // to the millisecond, so the server stores the same time
            UserId = givenUser ? userId : ActingUserId,
            UserName = Cut(givenUser ? userName : ActingUserName, 50),
            Action = Cut(action, 40)!,
            EntityName = entityName,
            EntityId = entityId,
            Before = AuditValues(before),
            After = AuditValues(after),
            Details = Cut(details, 400),
        };
        AuditEntries.Add(entry);
        return entry;
    }

    /// <summary>Values as the audit log keeps them: a JSON object, or null for none.</summary>
    public static string? AuditValues(IReadOnlyDictionary<string, object?>? values) =>
        values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values, AuditJson);

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];

    /// <summary>
    /// Adds an audit line for every added, changed and deleted row of the <see cref="AuditedTables"/>, with the changed
    /// values. A row saved with Update (read elsewhere, so EF does not know what it held) is compared with what the
    /// database holds, so only real changes are written.
    /// </summary>
    private async ValueTask AuditChangesAsync(bool sync, CancellationToken cancellationToken)
    {
        if (SuppressAudit || ApplyingSync || Station is null) return;
        ChangeTracker.DetectChanges();
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && AuditedTables.Contains(e.Metadata.ClrType))
            .ToList();
        foreach (var entry in entries)
        {
            Dictionary<string, object?>? before = null, after = null;
            string action;
            if (entry.State == EntityState.Added)
            {
                action = AuditActions.Added;
                after = Logged(entry).Where(p => !IsEmpty(p.CurrentValue)).ToDictionary(p => p.Metadata.Name, p => Shown(p.Metadata.Name, p.CurrentValue));
            }
            else if (entry.State == EntityState.Deleted)
            {
                action = AuditActions.Deleted;
                before = Logged(entry).Where(p => !IsEmpty(p.OriginalValue)).ToDictionary(p => p.Metadata.Name, p => Shown(p.Metadata.Name, p.OriginalValue));
            }
            else
            {
                var modified = Logged(entry).Where(p => p.IsModified).ToList();
                if (modified.Count == 0) continue;
                // Update() on a row read elsewhere marks every property changed with the same value before and after.
                var stored = modified.Any(p => Equals(p.OriginalValue, p.CurrentValue))
                    ? sync ? entry.GetDatabaseValues() : await entry.GetDatabaseValuesAsync(cancellationToken)
                    : null;
                object? Old(PropertyEntry p) => stored is null ? p.OriginalValue : stored[p.Metadata];
                var changed = modified.Where(p => !Equals(Old(p), p.CurrentValue)).ToList();
                if (changed.Count == 0) continue;
                before = changed.ToDictionary(p => p.Metadata.Name, p => Shown(p.Metadata.Name, Old(p)));
                after = changed.ToDictionary(p => p.Metadata.Name, p => Shown(p.Metadata.Name, p.CurrentValue));
                action = after.TryGetValue(nameof(ISoftDeletable.IsDeleted), out var deleted) && deleted is bool now
                    ? now ? AuditActions.Deleted : AuditActions.Undeleted
                    : AuditActions.Changed;
            }
            Audit(action, AuditedTables.Describe(entry.Entity), entry.Metadata.ClrType.Name, (entry.Entity as Entity)?.Id, before, after);
        }
    }

    /// <summary>The properties of a row the log may show (not its key, nor the ones the program keeps).</summary>
    private static IEnumerable<PropertyEntry> Logged(EntityEntry entry) =>
        entry.Properties.Where(p => !p.Metadata.IsPrimaryKey() && !AuditedTables.Ignored.Contains(p.Metadata.Name) && p.Metadata.ClrType != typeof(byte[]));

    private static object? Shown(string property, object? value) => AuditedTables.Secret.Contains(property) && value is not null ? "(hidden)" : value;

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        bool b => !b,
        decimal d => d == 0,
        int i => i == 0,
        long l => l == 0,
        Guid g => g == Guid.Empty,
        _ => false,
    };
}
