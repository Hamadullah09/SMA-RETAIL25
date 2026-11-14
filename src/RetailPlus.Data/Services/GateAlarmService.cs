using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A tag read by an exit-gate antenna of a station.</summary>
public sealed record GateRead(string Epc, int StationNumber, DateTime SeenAt, string? ReaderName = null, int Antenna = 0);

/// <summary>What a gate read led to: the outcome, and the alarm when it sounds (new, or the tag's open one).</summary>
public sealed record GateReadResult(GateOutcome Outcome, string Epc, GateAlarm? Alarm)
{
    public bool Sounds => GateRules.Sounds(Outcome);
}

/// <summary>
/// The exit-gate alarm. A tagged piece that was not sold and is read by a gate antenna gets flag 3 (Unsold) and an open
/// <see cref="GateAlarm"/>; the till sounds the siren and shows the alarm until someone resolves it: the item was brought
/// back (flag 1) or it left the store (Lost). Selling the piece afterwards resolves the alarm by itself
/// (<see cref="ResolveSoldAsync"/>). The screen only shows what this service decides.
/// <para>Keep one instance per program: it remembers which tags it just saw, so one pass through the gate is one read.</para>
/// </summary>
public sealed class GateAlarmService(DataContext data, TimeSpan? quietTime = null)
{
    /// <summary>The name the audit log gives the gate for an alarm it raised.</summary>
    public const string GateUser = "Exit gate";

    private readonly GateDebouncer _debouncer = new(quietTime);

    public TimeSpan QuietTime => _debouncer.QuietTime;

    /// <summary>
    /// A read at a gate antenna: no alarm for a sold or lost piece or an unknown code, a new alarm for any other piece,
    /// and the open alarm again for a piece that already has one (no second record). Repeated reads of the same pass
    /// through the gate are <see cref="GateOutcome.Repeated"/>.
    /// </summary>
    public async Task<GateReadResult> ReadAsync(GateRead read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        var epc = Epc.Normalize(read.Epc) ?? read.Epc.Trim().ToUpperInvariant();
        if (!_debouncer.IsNewPass(epc, read.SeenAt)) return new GateReadResult(GateOutcome.Repeated, epc, null);

        await using var db = data.Create();
        var tag = await db.ItemTags.SingleOrDefaultAsync(t => t.Epc == epc, cancellationToken);
        var open = tag is null ? null
            : await db.GateAlarms.Where(a => a.Epc == epc && a.Resolution == GateAlarmResolution.Open).OrderBy(a => a.RaisedAt).FirstOrDefaultAsync(cancellationToken);
        var outcome = GateRules.Decide(tag?.Status, open is not null);
        switch (outcome)
        {
            case GateOutcome.OpenAlarm:
                open!.LastSeenAt = read.SeenAt;
                open.Passes++;
                // A piece put back to flag 1 by a return while its alarm stayed open is flag 3 again.
                if (tag!.Status != ItemTagStatus.Unsold) tag.Status = ItemTagStatus.Unsold;
                await db.SaveChangesAsync(cancellationToken);
                return new GateReadResult(outcome, epc, open);
            case GateOutcome.NewAlarm:
                var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == tag!.ItemId, cancellationToken);
                if (item is null) return new GateReadResult(GateOutcome.Unknown, epc, null);
                var variant = tag!.VariantId is Guid v ? await db.ItemVariants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == v, cancellationToken) : null;
                var variantText = variant is null ? "" : string.Join(" ", new[] { variant.Value1, variant.Value2, variant.Value3 }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var alarm = new GateAlarm
                {
                    Id = Guid.CreateVersion7(),
                    StationNumber = read.StationNumber,
                    LocationId = item.LocationId,
                    Epc = epc,
                    TagId = tag.Id,
                    ItemId = item.Id,
                    VariantId = tag.VariantId,
                    Plu = item.Plu,
                    ItemName = Cut((item.Name + " " + variantText).Trim(), 60),
                    Price = item.Price1,
                    ReaderName = read.ReaderName is null ? null : Cut(read.ReaderName, 40),
                    Antenna = read.Antenna,
                    RaisedAt = read.SeenAt,
                    LastSeenAt = read.SeenAt,
                };
                tag.Status = ItemTagStatus.Unsold;
                db.GateAlarms.Add(alarm);
                db.Audit(AuditActions.GateAlarm,
                    $"{item.Plu} {item.Name}, tag {epc}, not sold, read at the exit gate of station {read.StationNumber}"
                    + (read.ReaderName is null ? "" : $" ({read.ReaderName} antenna {read.Antenna})"),
                    nameof(GateAlarm), alarm.Id, userName: GateUser);
                await db.SaveChangesAsync(cancellationToken);
                return new GateReadResult(outcome, epc, alarm);
            default:
                return new GateReadResult(outcome, epc, null);
        }
    }

    /// <summary>The open alarms, oldest first; of one station when given.</summary>
    public async Task<List<GateAlarm>> GetOpenAsync(int? stationNumber = null, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var q = db.GateAlarms.AsNoTracking().Where(a => a.Resolution == GateAlarmResolution.Open);
        if (stationNumber is int s) q = q.Where(a => a.StationNumber == s);
        return await q.OrderBy(a => a.RaisedAt).ToListAsync(cancellationToken);
    }

    /// <summary>The alarms of a tag, newest first.</summary>
    public async Task<List<GateAlarm>> GetForTagAsync(string epc, CancellationToken cancellationToken = default)
    {
        var code = Epc.Normalize(epc) ?? epc;
        await using var db = data.Create();
        return await db.GateAlarms.AsNoTracking().Where(a => a.Epc == code).OrderByDescending(a => a.RaisedAt).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves an open alarm from the till: <see cref="GateAlarmResolution.BackInStock"/> puts the tag back to flag 1,
    /// <see cref="GateAlarmResolution.LeftStore"/> makes it Lost. <paramref name="by"/> is who resolved it: a signed-in user
    /// who may approve, or the supervisor who approved it at the till; the screen checks that.
    /// </summary>
    public async Task<GateAlarm> ResolveAsync(Guid alarmId, GateAlarmResolution resolution, SessionInfo by, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (resolution is not (GateAlarmResolution.BackInStock or GateAlarmResolution.LeftStore))
            throw new BusinessRuleException("A gate alarm is resolved as back in stock or as left the store.");
        await using var db = data.Create();
        var alarm = await db.GateAlarms.SingleOrDefaultAsync(a => a.Id == alarmId, cancellationToken)
                    ?? throw new BusinessRuleException("This gate alarm is no longer there.");
        if (!alarm.IsOpen)
            throw new BusinessRuleException($"This gate alarm was already resolved ({GateRules.Describe(alarm.Resolution).ToLowerInvariant()}, by {alarm.ResolvedBy}).");
        var now = DateTime.Now;
        alarm.Resolution = resolution;
        alarm.ResolvedAt = now;
        alarm.ResolvedBy = Cut(by.UserName, 50);
        alarm.ResolvedById = by.UserId;
        var tag = await db.ItemTags.SingleOrDefaultAsync(t => t.Epc == alarm.Epc, cancellationToken);
        // A piece sold meanwhile stays sold.
        if (tag is not null && tag.Status != ItemTagStatus.Sold)
        {
            tag.Status = GateRules.StatusAfter(resolution);
            if (tag.Status == ItemTagStatus.InStock) tag.SoldAt = null;
        }
        db.Audit(AuditActions.GateAlarmResolved, $"{alarm.Plu} {alarm.ItemName}, tag {alarm.Epc}: {GateRules.Describe(resolution).ToLowerInvariant()}",
            nameof(GateAlarm), alarm.Id, userId: by.UserId, userName: by.UserName);
        await db.SaveChangesAsync(cancellationToken);
        return alarm;
    }

    /// <summary>
    /// Part of saving a sale: the open alarms of the tags it sells are resolved as sold afterwards (the customer came back
    /// and paid). Saved with the sale.
    /// </summary>
    internal static async Task ResolveSoldAsync(RetailPlusDbContext db, IReadOnlyCollection<string> soldEpcs, long invoice, DateTime at, string? staff, Guid? staffId)
    {
        if (soldEpcs.Count == 0) return;
        var open = await db.GateAlarms.Where(a => a.Resolution == GateAlarmResolution.Open && soldEpcs.Contains(a.Epc)).ToListAsync();
        foreach (var alarm in open)
        {
            alarm.Resolution = GateAlarmResolution.SoldAfterwards;
            alarm.ResolvedAt = at;
            alarm.ResolvedBy = staff is null ? null : Cut(staff, 50);
            alarm.ResolvedById = staffId;
            alarm.SoldOnInvoice = invoice;
            db.Audit(AuditActions.GateAlarmResolved, $"{alarm.Plu} {alarm.ItemName}, tag {alarm.Epc}: sold afterwards on invoice {invoice}",
                nameof(GateAlarm), alarm.Id, userId: staffId, userName: staff);
        }
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
