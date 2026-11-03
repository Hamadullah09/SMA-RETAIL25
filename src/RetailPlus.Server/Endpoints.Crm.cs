using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record StageRow(Guid Id, string Name, int SortOrder, decimal Probability, StageOutcome Outcome);
public sealed record StageRequest(Guid? Id, string Name, decimal Probability, StageOutcome Outcome);
public sealed record LeadRow(Guid Id, string Version, string Name, string? Company, string? Phone, string? WhatsAppNumber, string? Email, string? Source,
    Guid? OwnerId, string? OwnerName, Guid StoreId, string StoreCode, LeadStatus Status, Guid? ClientId, string? ClientName, string? Notes,
    DateTime CreatedAtUtc, DateTime? ConvertedAtUtc);
public sealed record LeadRequest(string? Name, string? Company, string? Phone, string? WhatsAppNumber, string? Email, string? Source, Guid? OwnerId,
    Guid? StoreId, string? Notes, LeadStatus? Status, string? Version);
public sealed record ConvertRequest(Guid? ClientId, string? Version);
public sealed record ConvertResult(LeadRow Lead, Guid ClientId, string? ClientNumber);
public sealed record OpportunityRow(Guid Id, string Version, string Title, Guid? ClientId, string? ClientName, Guid? LeadId, string? LeadName, Guid StageId,
    string Stage, StageOutcome Outcome, decimal ExpectedValue, decimal Probability, decimal WeightedValue, DateOnly? ExpectedCloseOn, Guid? OwnerId,
    string? OwnerName, Guid StoreId, string StoreCode, string? LostReason, DateOnly? ClosedOn, string? Notes, DateTime CreatedAtUtc);
public sealed record OpportunityRequest(string? Title, Guid? ClientId, Guid? LeadId, Guid? StageId, decimal ExpectedValue, decimal? Probability,
    DateOnly? ExpectedCloseOn, Guid? OwnerId, Guid? StoreId, string? LostReason, string? Notes, string? Version);
public sealed record MoveRequest(Guid StageId, string? Version, string? LostReason);
public sealed record BoardStage(Guid Id, string Name, StageOutcome Outcome, decimal Probability, int Count, decimal Value, decimal Weighted,
    IReadOnlyList<OpportunityRow> Opportunities);
public sealed record BoardResult(IReadOnlyList<BoardStage> Stages);
public sealed record CountValue(int Count, decimal Value);
public sealed record OwnerResult(Guid? OwnerId, string? OwnerName, int Won, decimal WonValue, int Lost, decimal LostValue);
public sealed record WonLostResult(DateOnly From, DateOnly To, CountValue Won, CountValue Lost, decimal WinRate, IReadOnlyList<OwnerResult> ByOwner,
    IReadOnlyList<LostReasonRow> LostReasons, IReadOnlyList<OpportunityRow> Closed);
public sealed record ActivityRow(Guid Id, string Version, ActivityKind Kind, string Subject, string? Notes, Guid? ClientId, string? ClientName, Guid? LeadId,
    string? LeadName, Guid? OpportunityId, string? OpportunityTitle, DateTime? DueAt, bool IsDone, DateTime? DoneAtUtc, Guid? OwnerId, string? OwnerName,
    Guid StoreId, string StoreCode, string Due, DateTime CreatedAtUtc);
public sealed record ActivityRequest(ActivityKind Kind, string? Subject, string? Notes, Guid? ClientId, Guid? LeadId, Guid? OpportunityId, DateTime? DueAt,
    Guid? OwnerId, Guid? StoreId, bool? IsDone, string? Version);
public sealed record DoneRequest(bool Done, string? Version);
public sealed record MyTasksResult(int Overdue, int DueToday, IReadOnlyList<ActivityRow> Items);

/// <summary>
/// Phase E4, CRM: the pipeline's stages, leads, opportunities (list, board, moves, won and lost) and activities with the
/// signed-in user's tasks. Records belong to one store; stages are the company's.
/// </summary>
public static partial class Endpoints
{
    private static void MapCrm(RouteGroupBuilder api)
    {
        var view = Need(Permissions.CrmView);
        var edit = Need(Permissions.CrmEdit);
        api.MapGet("/crm/stages", async (ApiRequest api) => Results.Ok((await new CrmService(api.Root).StagesAsync()).Select(ToRow))).RequireAuthorization(view);
        api.MapPut("/crm/stages", SaveStagesAsync).RequireAuthorization(edit);

        api.MapGet("/leads", LeadsAsync).RequireAuthorization(view);
        api.MapGet("/leads/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await LeadRowAsync(api, id))).RequireAuthorization(view);
        api.MapPost("/leads", (LeadRequest body, ApiRequest api) => SaveLeadAsync(null, body, api)).RequireAuthorization(edit);
        api.MapPut("/leads/{id:guid}", (Guid id, LeadRequest body, ApiRequest api) => SaveLeadAsync(id, body, api)).RequireAuthorization(edit);
        api.MapDelete("/leads/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Lead>(api, id, "That lead");
            await new CrmService(await api.ForChangeAsync(store)).DeleteLeadAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(edit);
        api.MapPost("/leads/{id:guid}/convert", ConvertLeadAsync).RequireAuthorization(edit);

        api.MapGet("/opportunities", OpportunitiesAsync).RequireAuthorization(view);
        api.MapGet("/opportunities/board", BoardAsync).RequireAuthorization(view);
        api.MapGet("/opportunities/report", WonLostAsync).RequireAuthorization(view);
        api.MapGet("/opportunities/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await OpportunityRowAsync(api, id))).RequireAuthorization(view);
        api.MapPost("/opportunities", (OpportunityRequest body, ApiRequest api) => SaveOpportunityAsync(null, body, api)).RequireAuthorization(edit);
        api.MapPut("/opportunities/{id:guid}", (Guid id, OpportunityRequest body, ApiRequest api) => SaveOpportunityAsync(id, body, api)).RequireAuthorization(edit);
        api.MapPost("/opportunities/{id:guid}/move", async (Guid id, MoveRequest body, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Opportunity>(api, id, "That opportunity");
            var data = await api.ForChangeAsync(store);
            await new CrmService(data).MoveOpportunityAsync(id, ApiRequest.ParseVersion(body.Version), body.StageId, body.LostReason, await api.TodayAsync(store));
            return Results.Ok(await OpportunityRowAsync(api, id));
        }).RequireAuthorization(edit);
        api.MapDelete("/opportunities/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Opportunity>(api, id, "That opportunity");
            await new CrmService(await api.ForChangeAsync(store)).DeleteOpportunityAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(edit);

        api.MapGet("/activities", ActivitiesAsync).RequireAuthorization(view);
        api.MapGet("/activities/mine", MineAsync).RequireAuthorization(view);
        api.MapPost("/activities", (ActivityRequest body, ApiRequest api) => SaveActivityAsync(null, body, api)).RequireAuthorization(edit);
        api.MapPut("/activities/{id:guid}", (Guid id, ActivityRequest body, ApiRequest api) => SaveActivityAsync(id, body, api)).RequireAuthorization(edit);
        api.MapPost("/activities/{id:guid}/done", async (Guid id, DoneRequest body, ApiRequest api) =>
        {
            var store = await StoreOfAsync<CrmActivity>(api, id, "That activity");
            await using (var db = api.Root.Create())
            {
                var owner = await db.CrmActivities.AsNoTracking().Where(a => a.Id == id).Select(a => a.OwnerId).SingleAsync();
                if (owner != api.User.UserId && !api.User.Can(Permissions.CrmEdit))
                    throw ApiException.Forbidden("Only the activity's owner, or a user who may change activities, marks it done.");
            }
            await new CrmService(await api.ForChangeAsync(store)).SetDoneAsync(id, ApiRequest.ParseVersion(body.Version), body.Done);
            return Results.Ok(await ActivityRowAsync(api, id));
        }).RequireAuthorization(view);
        api.MapDelete("/activities/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var store = await StoreOfAsync<CrmActivity>(api, id, "That activity");
            await new CrmService(await api.ForChangeAsync(store)).DeleteActivityAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(edit);
    }

    // Shared helpers ----------------------------------------------------------------------------------------------------

    /// <summary>The store a record of a store table belongs to; 404 when there is none or the user may not see its store.</summary>
    private static async Task<Guid> StoreOfAsync<T>(ApiRequest api, Guid id, string what) where T : Entity, IStoreScoped
    {
        await using var db = api.Root.Create();
        var store = await db.Set<T>().AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync() ?? throw ApiException.NotFound(what);
        api.RequireStore(store, what);
        return store;
    }

    /// <summary>
    /// The store a new record goes to: the one named in the body, else the one asked for, else the user's, else the first
    /// store. 403 for a store the user may not see.
    /// </summary>
    private static async Task<Guid> NewRecordStoreAsync(ApiRequest api, Guid? named)
    {
        var store = named ?? api.AskedStore ?? api.User.StoreId ?? await api.FirstStoreAsync() ?? throw ApiException.Bad("Add a store first.");
        if (!api.User.CanSee(store)) throw ApiException.Forbidden("You may not see that store.");
        return store;
    }

    /// <summary>A date of the query string when it is given (yyyy-MM-dd), else null.</summary>
    private static DateOnly? OptionalDate(ApiRequest api, string name) =>
        api.Text(name) is not { } text ? null
        : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw ApiException.Bad($"{name} must be a date like 2026-10-28, not {text}.");

    private static T ParseEnum<T>(ApiRequest api, string name) where T : struct, Enum =>
        Enum.TryParse<T>(api.Text(name), true, out var value) ? value : throw ApiException.Bad($"{name} is not one of {string.Join(", ", Enum.GetNames<T>())}.");

    private sealed record Names(Dictionary<Guid, string> Stores, Dictionary<Guid, string> Users, Dictionary<Guid, string> Clients, Dictionary<Guid, string> Leads,
        Dictionary<Guid, string> Opportunities, Dictionary<Guid, OpportunityStage> Stages);

    private static string CrmClientName(Client c) => !string.IsNullOrWhiteSpace(c.Company) && string.IsNullOrWhiteSpace(c.LastName) && string.IsNullOrWhiteSpace(c.FirstName)
        ? c.Company! : $"{c.FirstName} {c.LastName}".Trim();

    /// <summary>The names the rows show: stores, users, and the clients, leads and opportunities they point at.</summary>
    private static async Task<Names> NamesAsync(RetailPlusDbContext db, IEnumerable<Guid?> clients, IEnumerable<Guid?> leads, IEnumerable<Guid?> opportunities, CrmService crm)
    {
        var c = clients.OfType<Guid>().Distinct().ToList();
        var l = leads.OfType<Guid>().Distinct().ToList();
        var o = opportunities.OfType<Guid>().Distinct().ToList();
        return new Names(
            await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code),
            await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName),
            (await db.Clients.AsNoTracking().Where(x => c.Contains(x.Id)).ToListAsync()).ToDictionary(x => x.Id, CrmClientName),
            await db.Leads.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name),
            await db.Opportunities.AsNoTracking().Where(x => o.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title),
            (await crm.StagesAsync()).ToDictionary(s => s.Id));
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;

    // Stages ------------------------------------------------------------------------------------------------------------

    private static StageRow ToRow(OpportunityStage s) => new(s.Id, s.Name, s.SortOrder, s.Probability, s.Outcome);

    private static async Task<IResult> SaveStagesAsync(List<StageRequest> body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var stages = body.Select(s => new OpportunityStage { Id = s.Id ?? Guid.Empty, Name = s.Name ?? "", Probability = s.Probability, Outcome = s.Outcome }).ToList();
        var saved = await new CrmService(await api.ForChangeAsync()).SaveStagesAsync(stages);
        return Results.Ok(saved.Select(ToRow));
    }

    // Leads -------------------------------------------------------------------------------------------------------------

    private static LeadRow ToRow(Lead l, Names n) => new(l.Id, ApiRequest.Version(l.Version), l.Name, l.Company, l.Phone, l.WhatsAppNumber, l.Email, l.Source,
        l.OwnerId, Name(n.Users, l.OwnerId), l.StoreId, n.Stores.GetValueOrDefault(l.StoreId) ?? "", l.Status, l.ConvertedClientId, Name(n.Clients, l.ConvertedClientId),
        l.Notes, ApiRequest.Utc(l.CreatedAtUtc), ApiRequest.Utc(l.ConvertedAtUtc));

    private static async Task<IResult> LeadsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.Leads.AsNoTracking();
        if (store is { } s) q = q.Where(x => x.StoreId == s);
        if (api.Id("ownerId") is { } owner) q = q.Where(x => x.OwnerId == owner);
        if (api.Text("status") is not null) { var status = ParseEnum<LeadStatus>(api, "status"); q = q.Where(x => x.Status == status); }
        if (api.Text("search") is { } t)
            q = q.Where(x => x.Name.Contains(t) || (x.Company ?? "").Contains(t) || (x.Phone ?? "").Contains(t) || (x.WhatsAppNumber ?? "").Contains(t) || (x.Email ?? "").Contains(t));
        if (OptionalDate(api, "from") is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
            q = q.Where(x => x.CreatedAtUtc >= start);
        }
        if (OptionalDate(api, "to") is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
            q = q.Where(x => x.CreatedAtUtc < end);
        }
        var page = await api.PageAsync(q.OrderByDescending(x => x.CreatedAtUtc), x => x);
        var names = await NamesAsync(db, page.Items.Select(x => x.ConvertedClientId), [], [], new CrmService(api.Root));
        return Results.Ok(new PageOf<LeadRow>([.. page.Items.Select(x => ToRow(x, names))], page.Total, page.Page, page.PageSize));
    }

    private static async Task<LeadRow> LeadRowAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That lead");
        api.RequireStore(lead.StoreId, "That lead");
        return ToRow(lead, await NamesAsync(db, [lead.ConvertedClientId], [], [], new CrmService(api.Root)));
    }

    private static async Task<IResult> SaveLeadAsync(Guid? id, LeadRequest body, ApiRequest api)
    {
        Guid store;
        Lead lead;
        if (id is { } existing)
        {
            store = await StoreOfAsync<Lead>(api, existing, "That lead");
            await using var db = api.Root.Create();
            lead = await db.Leads.AsNoTracking().SingleAsync(x => x.Id == existing);
            lead.Version = ApiRequest.ParseVersion(body.Version);
        }
        else
        {
            store = await NewRecordStoreAsync(api, body.StoreId);
            lead = new Lead { StoreId = store };
        }
        lead.Name = body.Name ?? ""; lead.Company = body.Company; lead.Phone = body.Phone; lead.WhatsAppNumber = body.WhatsAppNumber; lead.Email = body.Email;
        lead.Source = body.Source; lead.Notes = body.Notes;
        lead.OwnerId = body.OwnerId ?? (id is null ? api.User.UserId : lead.OwnerId);
        if (body.Status is { } status) lead.Status = status;
        var saved = await new CrmService(await api.ForChangeAsync(store)).SaveLeadAsync(lead);
        var row = await LeadRowAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/leads/{saved.Id}", row) : Results.Ok(row);
    }

    private static async Task<IResult> ConvertLeadAsync(Guid id, ConvertRequest body, ApiRequest api)
    {
        if (!api.User.Can(Permissions.ClientsEdit)) throw ApiException.Forbidden("Converting a lead adds a client, which your role may not do.");
        var store = await StoreOfAsync<Lead>(api, id, "That lead");
        var data = await api.ForChangeAsync(store);
        string? number = body.ClientId is null
            ? (await NextServerNumberAsync(api, store, RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(CultureInfo.InvariantCulture) : null;
        var (_, client) = await new CrmService(data).ConvertLeadAsync(id, ApiRequest.ParseVersion(body.Version), body.ClientId, number);
        return Results.Ok(new ConvertResult(await LeadRowAsync(api, id), client.Id, client.Number));
    }

    // Opportunities -----------------------------------------------------------------------------------------------------

    private static OpportunityRow ToRow(Opportunity o, Names n)
    {
        var stage = n.Stages.GetValueOrDefault(o.StageId);
        return new OpportunityRow(o.Id, ApiRequest.Version(o.Version), o.Title, o.ClientId, Name(n.Clients, o.ClientId), o.LeadId, Name(n.Leads, o.LeadId), o.StageId,
            stage?.Name ?? "", stage?.Outcome ?? StageOutcome.Open, o.ExpectedValue, o.Probability, Money.Round(o.ExpectedValue * o.Probability / 100m), o.ExpectedCloseOn,
            o.OwnerId, Name(n.Users, o.OwnerId), o.StoreId, n.Stores.GetValueOrDefault(o.StoreId) ?? "", o.LostReason, o.ClosedOn, o.Notes, ApiRequest.Utc(o.CreatedAtUtc));
    }

    private static async Task<List<OpportunityRow>> OpportunityRowsAsync(RetailPlusDbContext db, ApiRequest api, IReadOnlyList<Opportunity> list)
    {
        var names = await NamesAsync(db, list.Select(o => o.ClientId), list.Select(o => o.LeadId), [], new CrmService(api.Root));
        return [.. list.Select(o => ToRow(o, names))];
    }

    /// <summary>
    /// Opportunities of the stores in scope, by owner, stage, outcome and text; <c>from</c>/<c>to</c> on the expected close
    /// date of open ones and the close date of won and lost ones.
    /// </summary>
    private static async Task<IQueryable<Opportunity>> OpportunityQueryAsync(RetailPlusDbContext db, ApiRequest api)
    {
        var store = api.ListStore();
        var q = db.Opportunities.AsNoTracking();
        if (store is { } s) q = q.Where(x => x.StoreId == s);
        if (api.Id("ownerId") is { } owner) q = q.Where(x => x.OwnerId == owner);
        if (api.Id("stageId") is { } stage) q = q.Where(x => x.StageId == stage);
        if (api.Text("outcome") is not null)
        {
            var outcome = ParseEnum<StageOutcome>(api, "outcome");
            var stages = (await new CrmService(api.Root).StagesAsync()).Where(x => x.Outcome == outcome).Select(x => x.Id).ToList();
            q = q.Where(x => stages.Contains(x.StageId));
        }
        if (api.Text("search") is { } t) q = q.Where(x => x.Title.Contains(t));
        if (OptionalDate(api, "from") is { } from) q = q.Where(x => (x.ClosedOn ?? x.ExpectedCloseOn) >= from);
        if (OptionalDate(api, "to") is { } to) q = q.Where(x => (x.ClosedOn ?? x.ExpectedCloseOn) <= to);
        return q;
    }

    private static async Task<IResult> OpportunitiesAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var q = await OpportunityQueryAsync(db, api);
        var page = await api.PageAsync(q.OrderBy(x => x.ClosedOn != null).ThenBy(x => x.ExpectedCloseOn == null).ThenBy(x => x.ExpectedCloseOn).ThenByDescending(x => x.CreatedAtUtc), x => x);
        return Results.Ok(new PageOf<OpportunityRow>(await OpportunityRowsAsync(db, api, page.Items), page.Total, page.Page, page.PageSize));
    }

    private static async Task<IResult> BoardAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var q = await OpportunityQueryAsync(db, api);
        if (api.Text("from") is null && api.Text("to") is null)
        {
            var since = (await api.TodayAsync(api.ListStore())).AddDays(-30);
            q = q.Where(x => x.ClosedOn == null || x.ClosedOn >= since);
        }
        var rows = await OpportunityRowsAsync(db, api, await q.OrderBy(x => x.ExpectedCloseOn == null).ThenBy(x => x.ExpectedCloseOn).ThenByDescending(x => x.CreatedAtUtc).Take(2000).ToListAsync());
        var stages = await new CrmService(api.Root).StagesAsync();
        return Results.Ok(new BoardResult([.. stages.Select(s =>
        {
            var at = rows.Where(r => r.StageId == s.Id).ToList();
            return new BoardStage(s.Id, s.Name, s.Outcome, s.Probability, at.Count, at.Sum(r => r.ExpectedValue), at.Sum(r => r.WeightedValue), at);
        })]));
    }

    private static async Task<OpportunityRow> OpportunityRowAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var o = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That opportunity");
        api.RequireStore(o.StoreId, "That opportunity");
        return (await OpportunityRowsAsync(db, api, [o]))[0];
    }

    private static async Task<IResult> SaveOpportunityAsync(Guid? id, OpportunityRequest body, ApiRequest api)
    {
        Guid store;
        Opportunity o;
        if (id is { } existing)
        {
            store = await StoreOfAsync<Opportunity>(api, existing, "That opportunity");
            await using var db = api.Root.Create();
            o = await db.Opportunities.AsNoTracking().SingleAsync(x => x.Id == existing);
            o.Version = ApiRequest.ParseVersion(body.Version);
        }
        else
        {
            store = await NewRecordStoreAsync(api, body.StoreId);
            o = new Opportunity { StoreId = store };
        }
        if (body.LeadId is { } leadId) await StoreOfAsync<Lead>(api, leadId, "That lead");
        o.Title = body.Title ?? ""; o.ClientId = body.ClientId; o.LeadId = body.LeadId; o.StageId = body.StageId ?? (id is null ? Guid.Empty : o.StageId);
        o.ExpectedValue = body.ExpectedValue; o.Probability = body.Probability ?? 0; o.ExpectedCloseOn = body.ExpectedCloseOn; o.LostReason = body.LostReason;
        o.Notes = body.Notes;
        o.OwnerId = body.OwnerId ?? (id is null ? api.User.UserId : o.OwnerId);
        var saved = await new CrmService(await api.ForChangeAsync(store)).SaveOpportunityAsync(o, await api.TodayAsync(store), probabilityGiven: body.Probability is not null);
        var row = await OpportunityRowAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/opportunities/{saved.Id}", row) : Results.Ok(row);
    }

    private static async Task<IResult> WonLostAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        var crm = new CrmService(api.Root);
        var report = await crm.WonLostAsync(from, to, store is { } s ? [s] : null, api.Id("ownerId"));
        await using var db = api.Root.Create();
        var closed = await OpportunityRowsAsync(db, api, report.Closed);
        var users = await db.UserAccounts.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName);
        return Results.Ok(new WonLostResult(from, to, new CountValue(report.Won, report.WonValue), new CountValue(report.Lost, report.LostValue), report.WinRate,
            [.. report.ByOwner.Select(r => new OwnerResult(r.OwnerId, Name(users, r.OwnerId), r.Won, r.WonValue, r.Lost, r.LostValue))], report.LostReasons, closed));
    }

    // Activities --------------------------------------------------------------------------------------------------------

    private static ActivityRow ToRow(CrmActivity a, Names n, DateOnly today) => new(a.Id, ApiRequest.Version(a.Version), a.Kind, a.Subject, a.Notes, a.ClientId,
        Name(n.Clients, a.ClientId), a.LeadId, Name(n.Leads, a.LeadId), a.OpportunityId, Name(n.Opportunities, a.OpportunityId), a.DueAt, a.IsDone, ApiRequest.Utc(a.DoneAtUtc),
        a.OwnerId, Name(n.Users, a.OwnerId), a.StoreId, n.Stores.GetValueOrDefault(a.StoreId) ?? "", CrmService.Due(a, today), ApiRequest.Utc(a.CreatedAtUtc));

    private static async Task<List<ActivityRow>> ActivityRowsAsync(RetailPlusDbContext db, ApiRequest api, IReadOnlyList<CrmActivity> list)
    {
        var names = await NamesAsync(db, list.Select(a => a.ClientId), list.Select(a => a.LeadId), list.Select(a => a.OpportunityId), new CrmService(api.Root));
        var today = await api.TodayAsync(api.User.StoreId);
        return [.. list.Select(a => ToRow(a, names, today))];
    }

    private static async Task<IResult> ActivitiesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.CrmActivities.AsNoTracking();
        if (store is { } s) q = q.Where(x => x.StoreId == s);
        if (api.Id("ownerId") is { } owner) q = q.Where(x => x.OwnerId == owner);
        if (api.Id("clientId") is { } client) q = q.Where(x => x.ClientId == client);
        if (api.Id("leadId") is { } lead) q = q.Where(x => x.LeadId == lead);
        if (api.Id("opportunityId") is { } opportunity) q = q.Where(x => x.OpportunityId == opportunity);
        if (api.Text("kind") is not null) { var kind = ParseEnum<ActivityKind>(api, "kind"); q = q.Where(x => x.Kind == kind); }
        if (api.Bool("done") is { } done) q = q.Where(x => x.IsDone == done);
        if (OptionalDate(api, "from") is { } from) { var start = from.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.DueAt >= start); }
        if (OptionalDate(api, "to") is { } to) { var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.DueAt < end); }
        var page = await api.PageAsync(q.OrderBy(x => x.IsDone).ThenBy(x => x.DueAt == null).ThenBy(x => x.DueAt).ThenByDescending(x => x.CreatedAtUtc), x => x);
        return Results.Ok(new PageOf<ActivityRow>(await ActivityRowsAsync(db, api, page.Items), page.Total, page.Page, page.PageSize));
    }

    private static async Task<ActivityRow> ActivityRowAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var a = await db.CrmActivities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That activity");
        api.RequireStore(a.StoreId, "That activity");
        return (await ActivityRowsAsync(db, api, [a]))[0];
    }

    private static async Task<IResult> SaveActivityAsync(Guid? id, ActivityRequest body, ApiRequest api)
    {
        Guid store;
        CrmActivity a;
        if (id is { } existing)
        {
            store = await StoreOfAsync<CrmActivity>(api, existing, "That activity");
            await using var db = api.Root.Create();
            a = await db.CrmActivities.AsNoTracking().SingleAsync(x => x.Id == existing);
            a.Version = ApiRequest.ParseVersion(body.Version);
        }
        else
        {
            store = await NewRecordStoreAsync(api, body.StoreId);
            a = new CrmActivity { StoreId = store };
        }
        if (body.LeadId is { } leadId) await StoreOfAsync<Lead>(api, leadId, "That lead");
        if (body.OpportunityId is { } opportunityId) await StoreOfAsync<Opportunity>(api, opportunityId, "That opportunity");
        a.Kind = body.Kind; a.Subject = body.Subject ?? ""; a.Notes = body.Notes; a.ClientId = body.ClientId; a.LeadId = body.LeadId;
        a.OpportunityId = body.OpportunityId; a.DueAt = body.DueAt is { } due ? DateTime.SpecifyKind(due, DateTimeKind.Unspecified) : null;
        a.IsDone = body.IsDone ?? a.IsDone;
        a.OwnerId = body.OwnerId ?? (id is null ? api.User.UserId : a.OwnerId);
        var saved = await new CrmService(await api.ForChangeAsync(store)).SaveActivityAsync(a);
        var row = await ActivityRowAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/activities/{saved.Id}", row) : Results.Ok(row);
    }

    private static async Task<IResult> MineAsync(ApiRequest api)
    {
        var today = await api.TodayAsync(api.User.StoreId);
        bool todayOnly = string.Equals(api.Text("due"), "today", StringComparison.OrdinalIgnoreCase);
        var mine = await new CrmService(api.Root).MineAsync(api.User.UserId, today, todayOnly);
        await using var db = api.Root.Create();
        var rows = await ActivityRowsAsync(db, api, mine);
        int overdue = rows.Count(r => r.Due == "overdue"), dueToday = rows.Count(r => r.Due == "today");
        return Results.Ok(new MyTasksResult(overdue, dueToday, rows));
    }
}
