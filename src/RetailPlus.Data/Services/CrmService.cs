using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>Won and lost opportunities of a period, for the report.</summary>
public sealed record WonLostOwner(Guid? OwnerId, int Won, decimal WonValue, int Lost, decimal LostValue);
public sealed record LostReasonRow(string Reason, int Count, decimal Value);
public sealed record WonLostReport(int Won, decimal WonValue, int Lost, decimal LostValue, decimal WinRate, IReadOnlyList<WonLostOwner> ByOwner,
    IReadOnlyList<LostReasonRow> LostReasons, IReadOnlyList<Opportunity> Closed);

/// <summary>
/// Leads, the sales pipeline (stages and opportunities) and activities. Everything here lives on the server only; the
/// web back office works with it through the API, as the user who signed in, for the store of the record.
/// </summary>
public sealed class CrmService(DataContext data)
{
    private static readonly SemaphoreSlim Seeding = new(1, 1);

    /// <summary>The stages of a new database.</summary>
    public static readonly IReadOnlyList<(string Name, decimal Probability, StageOutcome Outcome)> DefaultStages =
    [
        ("New", 10, StageOutcome.Open), ("Qualified", 30, StageOutcome.Open), ("Proposal", 60, StageOutcome.Open),
        ("Won", 100, StageOutcome.Won), ("Lost", 0, StageOutcome.Lost),
    ];

    // Stages ----------------------------------------------------------------------------------------------------------

    /// <summary>The pipeline's stages in board order; a database without any gets the default ones first.</summary>
    public async Task<List<OpportunityStage>> StagesAsync()
    {
        await using (var db = data.Create())
            if (await db.OpportunityStages.AnyAsync())
                return await db.OpportunityStages.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
        await Seeding.WaitAsync();
        try
        {
            await using var db = data.Create();
            if (!await db.OpportunityStages.AnyAsync())
            {
                int order = 0;
                foreach (var (name, probability, outcome) in DefaultStages)
                    db.OpportunityStages.Add(new OpportunityStage { Name = name, Probability = probability, Outcome = outcome, SortOrder = ++order });
                db.SuppressAudit = true;
                await db.SaveChangesAsync();
            }
            return await db.OpportunityStages.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
        }
        finally
        {
            Seeding.Release();
        }
    }

    /// <summary>
    /// Saves the whole list of stages in board order: new ones (empty id) are added, the rest renamed and reordered, and
    /// those left out removed, unless an opportunity is still at one.
    /// </summary>
    public async Task<List<OpportunityStage>> SaveStagesAsync(IReadOnlyList<OpportunityStage> stages)
    {
        await StagesAsync();
        foreach (var s in stages) s.Name = (s.Name ?? "").Trim();
        if (stages.Any(s => s.Name.Length == 0)) throw new BusinessRuleException("Every stage needs a name.");
        if (stages.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stages.Count) throw new BusinessRuleException("Two stages have the same name.");
        if (stages.Any(s => s.Probability is < 0 or > 100)) throw new BusinessRuleException("A probability is 0 to 100.");
        foreach (var outcome in Enum.GetValues<StageOutcome>())
            if (!stages.Any(s => s.Outcome == outcome)) throw new BusinessRuleException($"The pipeline needs a stage for {outcome.ToString().ToLowerInvariant()} opportunities.");
        await using var db = data.Create();
        var existing = await db.OpportunityStages.ToListAsync();
        var kept = stages.Where(s => s.Id != Guid.Empty).Select(s => s.Id).ToHashSet();
        foreach (var gone in existing.Where(e => !kept.Contains(e.Id)))
        {
            if (await db.Opportunities.AnyAsync(o => o.StageId == gone.Id))
                throw new BusinessRuleException($"Opportunities are still at the stage {gone.Name}. Move them to another stage first.");
            db.OpportunityStages.Remove(gone);
        }
        // Names are unique: removed and renamed stages are saved before the new ones take a name.
        await db.SaveChangesAsync();
        int order = 0;
        foreach (var s in stages)
        {
            var row = existing.FirstOrDefault(e => e.Id == s.Id && s.Id != Guid.Empty);
            if (row is null) db.OpportunityStages.Add(row = new OpportunityStage());
            row.Name = s.Name;
            row.Probability = s.Probability;
            row.Outcome = s.Outcome;
            row.SortOrder = ++order;
        }
        await db.SaveChangesAsync();
        return await db.OpportunityStages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }

    // Leads -----------------------------------------------------------------------------------------------------------

    /// <summary>Adds or changes a lead (a stale version is a conflict); the WhatsApp number is kept in international form.</summary>
    public async Task<Lead> SaveLeadAsync(Lead lead)
    {
        lead.Name = (lead.Name ?? "").Trim();
        if (lead.Name.Length == 0) throw new BusinessRuleException("Enter the lead's name.");
        lead.WhatsAppNumber = Number(lead.WhatsAppNumber);
        lead.Company = Blank(lead.Company);
        lead.Phone = Blank(lead.Phone);
        lead.Email = Blank(lead.Email);
        lead.Source = Blank(lead.Source);
        await using var db = data.Create();
        if (lead.Id == Guid.Empty)
        {
            lead.OwnerId ??= data.CurrentUser?.UserId;
            db.Leads.Add(lead);
        }
        else
        {
            var stored = await db.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lead.Id) ?? throw new BusinessRuleException("That lead no longer exists.");
            if (stored.Status == LeadStatus.Converted && lead.Status != LeadStatus.Converted) throw new BusinessRuleException("A converted lead stays converted.");
            if (lead.Status == LeadStatus.Converted && stored.Status != LeadStatus.Converted) throw new BusinessRuleException("Convert a lead with Convert to client.");
            lead.ConvertedClientId = stored.ConvertedClientId;
            lead.ConvertedAtUtc = stored.ConvertedAtUtc;
            lead.StoreId = stored.StoreId;
            db.Leads.Update(lead);
        }
        await SaveAsync(db, "lead");
        return lead;
    }

    /// <summary>Deletes a lead and its activities; refused while opportunities point at it.</summary>
    public async Task DeleteLeadAsync(Guid id)
    {
        await using var db = data.Create();
        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == id) ?? throw new BusinessRuleException("That lead no longer exists.");
        if (await db.Opportunities.AnyAsync(o => o.LeadId == id)) throw new BusinessRuleException("Opportunities point at this lead. Delete them or convert the lead first.");
        db.CrmActivities.RemoveRange(await db.CrmActivities.Where(a => a.LeadId == id).ToListAsync());
        db.Leads.Remove(lead);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Converts a lead into a client: an existing one (<paramref name="clientId"/>), or a new client made from the lead with
    /// <paramref name="clientNumber"/> (the station's next number when null). The lead's opportunities and activities move
    /// to the client. One transaction; a stale <paramref name="version"/> is a conflict.
    /// </summary>
    public async Task<(Lead Lead, Client Client)> ConvertLeadAsync(Guid leadId, Guid version, Guid? clientId, string? clientNumber = null)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == leadId) ?? throw new BusinessRuleException("That lead no longer exists.");
        if (lead.Version != version) throw new ConcurrencyConflictException("Someone else changed this lead. Load it again and make your changes again.");
        if (lead.Status == LeadStatus.Converted) throw new BusinessRuleException("This lead is already a client.");
        Client client;
        if (clientId is { } existing)
            client = await db.Clients.SingleOrDefaultAsync(c => c.Id == existing && !c.IsDeleted) ?? throw new BusinessRuleException("That client no longer exists.");
        else
        {
            var words = lead.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            client = new Client
            {
                FirstName = Cut(words.ElementAtOrDefault(0), 12), LastName = Cut(words.ElementAtOrDefault(1), 16), Company = Cut(lead.Company, 30),
                HomePhone = Cut(lead.Phone, 16), Email = Cut(lead.Email, 100), WhatsAppNumber = lead.WhatsAppNumber, Notes = lead.Notes,
                Number = clientNumber ?? (await db.NextNumberAsync(RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (await db.Clients.AnyAsync(c => c.Number == client.Number && !c.IsDeleted)) throw new BusinessRuleException($"Customer number {client.Number} is already used.");
            db.Clients.Add(client);
        }
        lead.Status = LeadStatus.Converted;
        lead.ConvertedClientId = client.Id;
        lead.ConvertedAtUtc = DateTime.UtcNow;
        foreach (var o in await db.Opportunities.Where(o => o.LeadId == leadId).ToListAsync()) o.ClientId = client.Id;
        foreach (var a in await db.CrmActivities.Where(a => a.LeadId == leadId).ToListAsync()) a.ClientId ??= client.Id;
        db.Audit(AuditActions.LeadConverted, $"{lead.Name} became client {client.Number}", nameof(Lead), lead.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (lead, client);
    }

    // Opportunities ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds or changes an opportunity: for a client or a lead, at a stage (the first open one by default) with its
    /// probability unless one is given. The stage decides whether it is closed.
    /// </summary>
    public async Task<Opportunity> SaveOpportunityAsync(Opportunity o, DateOnly today, bool probabilityGiven = true)
    {
        o.Title = (o.Title ?? "").Trim();
        if (o.Title.Length == 0) throw new BusinessRuleException("Give the opportunity a title.");
        if (o.ClientId is null && o.LeadId is null) throw new BusinessRuleException("Choose the client or the lead the opportunity is for.");
        if (o.ExpectedValue < 0) throw new BusinessRuleException("The expected value cannot be below zero.");
        var stages = await StagesAsync();
        var stage = o.StageId == Guid.Empty ? stages.First(s => s.Outcome == StageOutcome.Open) : stages.FirstOrDefault(s => s.Id == o.StageId)
            ?? throw new BusinessRuleException("That stage does not exist.");
        o.StageId = stage.Id;
        if (!probabilityGiven) o.Probability = stage.Probability;
        if (o.Probability is < 0 or > 100) throw new BusinessRuleException("The probability is 0 to 100.");
        o.LostReason = Blank(o.LostReason);
        if (stage.Outcome == StageOutcome.Lost && o.LostReason is null) throw new BusinessRuleException("Say why the opportunity was lost.");
        await using var db = data.Create();
        if (o.ClientId is { } c && !await db.Clients.AnyAsync(x => x.Id == c)) throw new BusinessRuleException("That client does not exist.");
        if (o.LeadId is { } l && !await db.Leads.AnyAsync(x => x.Id == l)) throw new BusinessRuleException("That lead does not exist.");
        if (o.Id == Guid.Empty)
        {
            o.OwnerId ??= data.CurrentUser?.UserId;
            o.ClosedOn = stage.Outcome == StageOutcome.Open ? null : today;
            db.Opportunities.Add(o);
        }
        else
        {
            var stored = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == o.Id) ?? throw new BusinessRuleException("That opportunity no longer exists.");
            var was = stages.FirstOrDefault(s => s.Id == stored.StageId);
            o.StoreId = stored.StoreId;
            o.ClosedOn = stage.Outcome == StageOutcome.Open ? null : was?.Outcome == stage.Outcome ? stored.ClosedOn ?? today : today;
            if (stage.Outcome != StageOutcome.Lost) o.LostReason = null;
            db.Opportunities.Update(o);
            if (was?.Outcome != stage.Outcome && stage.Outcome != StageOutcome.Open) AuditClose(db, o, stage);
        }
        await SaveAsync(db, "opportunity");
        return o;
    }

    private static void AuditClose(RetailPlusDbContext db, Opportunity o, OpportunityStage stage) =>
        db.Audit(stage.Outcome == StageOutcome.Won ? AuditActions.OpportunityWon : AuditActions.OpportunityLost,
            $"{o.Title}, {o.ExpectedValue:N2}" + (o.LostReason is null ? "" : ": " + o.LostReason), nameof(Opportunity), o.Id);

    /// <summary>Moves an opportunity to another stage (the board's drag): the stage's probability, closed or open again.</summary>
    public async Task<Opportunity> MoveOpportunityAsync(Guid id, Guid version, Guid stageId, string? lostReason, DateOnly today)
    {
        Opportunity o;
        await using (var db = data.Create())
            o = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That opportunity no longer exists.");
        o.Version = version;
        o.StageId = stageId;
        o.LostReason = lostReason ?? o.LostReason;
        return await SaveOpportunityAsync(o, today, probabilityGiven: false);
    }

    /// <summary>Deletes an opportunity and its activities.</summary>
    public async Task DeleteOpportunityAsync(Guid id)
    {
        await using var db = data.Create();
        var o = await db.Opportunities.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That opportunity no longer exists.");
        db.CrmActivities.RemoveRange(await db.CrmActivities.Where(a => a.OpportunityId == id).ToListAsync());
        db.Opportunities.Remove(o);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Opportunities won and lost between two days (their close date), by owner and by reason. <paramref name="stores"/>
    /// limits it to some stores (null: every store).
    /// </summary>
    public async Task<WonLostReport> WonLostAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? stores = null, Guid? ownerId = null)
    {
        var stages = (await StagesAsync()).ToDictionary(s => s.Id, s => s.Outcome);
        await using var db = data.Create();
        var q = db.Opportunities.AsNoTracking().Where(o => o.ClosedOn != null && o.ClosedOn >= from && o.ClosedOn <= to);
        if (stores is not null) q = q.Where(o => stores.Contains(o.StoreId));
        if (ownerId is { } owner) q = q.Where(o => o.OwnerId == owner);
        var closed = (await q.ToListAsync()).Where(o => stages.GetValueOrDefault(o.StageId) != StageOutcome.Open)
            .OrderByDescending(o => o.ClosedOn).ThenBy(o => o.Title).ToList();
        bool Won(Opportunity o) => stages[o.StageId] == StageOutcome.Won;
        var won = closed.Where(Won).ToList();
        var lost = closed.Where(o => !Won(o)).ToList();
        var byOwner = closed.GroupBy(o => o.OwnerId).Select(g => new WonLostOwner(g.Key, g.Count(Won), g.Where(Won).Sum(o => o.ExpectedValue),
            g.Count(o => !Won(o)), g.Where(o => !Won(o)).Sum(o => o.ExpectedValue))).OrderByDescending(r => r.WonValue).ToList();
        var reasons = lost.GroupBy(o => o.LostReason ?? "").Select(g => new LostReasonRow(g.Key, g.Count(), g.Sum(o => o.ExpectedValue)))
            .OrderByDescending(r => r.Count).ThenByDescending(r => r.Value).ToList();
        decimal rate = closed.Count == 0 ? 0 : Math.Round(won.Count * 100m / closed.Count, 1);
        return new WonLostReport(won.Count, won.Sum(o => o.ExpectedValue), lost.Count, lost.Sum(o => o.ExpectedValue), rate, byOwner, reasons, closed);
    }

    // Activities ------------------------------------------------------------------------------------------------------

    /// <summary>Adds or changes an activity on a client, a lead or an opportunity.</summary>
    public async Task<CrmActivity> SaveActivityAsync(CrmActivity a)
    {
        a.Subject = (a.Subject ?? "").Trim();
        if (a.Subject.Length == 0) throw new BusinessRuleException("Give the activity a subject.");
        if (a.ClientId is null && a.LeadId is null && a.OpportunityId is null) throw new BusinessRuleException("Choose the client, lead or opportunity the activity is for.");
        await using var db = data.Create();
        if (a.OpportunityId is { } o && !await db.Opportunities.AnyAsync(x => x.Id == o)) throw new BusinessRuleException("That opportunity does not exist.");
        if (a.ClientId is { } c && !await db.Clients.AnyAsync(x => x.Id == c)) throw new BusinessRuleException("That client does not exist.");
        if (a.LeadId is { } l && !await db.Leads.AnyAsync(x => x.Id == l)) throw new BusinessRuleException("That lead does not exist.");
        if (a.Id == Guid.Empty)
        {
            a.OwnerId ??= data.CurrentUser?.UserId;
            a.DoneAtUtc = a.IsDone ? DateTime.UtcNow : null;
            db.CrmActivities.Add(a);
        }
        else
        {
            var stored = await db.CrmActivities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == a.Id) ?? throw new BusinessRuleException("That activity no longer exists.");
            a.StoreId = stored.StoreId;
            a.DoneAtUtc = !a.IsDone ? null : stored.IsDone ? stored.DoneAtUtc : DateTime.UtcNow;
            db.CrmActivities.Update(a);
        }
        await SaveAsync(db, "activity");
        return a;
    }

    /// <summary>Marks an activity done or not done.</summary>
    public async Task<CrmActivity> SetDoneAsync(Guid id, Guid version, bool done)
    {
        await using var db = data.Create();
        var a = await db.CrmActivities.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That activity no longer exists.");
        if (a.Version != version) throw new ConcurrencyConflictException("Someone else changed this activity. Load it again and make your changes again.");
        if (a.IsDone != done)
        {
            a.IsDone = done;
            a.DoneAtUtc = done ? DateTime.UtcNow : null;
            await SaveAsync(db, "activity");
        }
        return a;
    }

    public async Task DeleteActivityAsync(Guid id)
    {
        await using var db = data.Create();
        var a = await db.CrmActivities.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That activity no longer exists.");
        db.CrmActivities.Remove(a);
        await db.SaveChangesAsync();
    }

    /// <summary>Where an activity stands against today: done, overdue, today, later, or without a due date.</summary>
    public static string Due(CrmActivity a, DateOnly today) =>
        a.IsDone ? "done" : a.DueAt is not { } due ? "none" : DateOnly.FromDateTime(due) < today ? "overdue" : DateOnly.FromDateTime(due) == today ? "today" : "later";

    /// <summary>
    /// A user's open activities: overdue first (oldest first), then today's, later ones, and those without a date.
    /// <paramref name="todayOnly"/> keeps the overdue and today's ones.
    /// </summary>
    public async Task<List<CrmActivity>> MineAsync(Guid userId, DateOnly today, bool todayOnly = false, int take = 500)
    {
        var tomorrow = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await using var db = data.Create();
        var q = db.CrmActivities.AsNoTracking().Where(a => a.OwnerId == userId && !a.IsDone);
        if (todayOnly) q = q.Where(a => a.DueAt != null && a.DueAt < tomorrow);
        var rows = await q.OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.CreatedAtUtc).Take(take).ToListAsync();
        return rows;
    }

    // Helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>A WhatsApp number in international form, or a business error the user can act on.</summary>
    public static string? Number(string? number)
    {
        try
        {
            return WhatsAppNumbers.Normalize(number);
        }
        catch (FormatException ex)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    private static async Task SaveAsync(RetailPlusDbContext db, string what)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException($"Someone else changed this {what}. Load it again and make your changes again.");
        }
    }
}
