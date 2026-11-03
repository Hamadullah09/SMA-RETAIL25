using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

public sealed record SegmentRow(Guid Id, string Version, string Name, string? Description, SegmentRules Rules, string Summary, Guid? StoreId, int LastCount,
    DateTime? LastEvaluatedAtUtc);
public sealed record SegmentRequest(string? Name, string? Description, SegmentRules? Rules, string? Version);
public sealed record PreviewRequest(SegmentRules? Rules);
public sealed record SegmentResult(int Count, DateTime EvaluatedAtUtc, IReadOnlyList<SegmentMember> Members);
public sealed record WhatsAppStatusInfo(bool Enabled, string Provider, string? PhoneNumberIdHint, bool WebhookReady, int MessagesPerMinute);
public sealed record TemplateRow(Guid Id, string Version, string Name, string Language, WhatsAppTemplateCategory Category, string Body, bool IsActive,
    IReadOnlyList<string> Placeholders);
public sealed record TemplateRequest(string? Name, string? Language, WhatsAppTemplateCategory Category, string? Body, bool IsActive, string? Version);
public sealed record CampaignRow(Guid Id, string Version, string Name, Guid SegmentId, string Segment, Guid TemplateId, string Template, CampaignStatus Status,
    Guid StoreId, string StoreCode, DateTime? ScheduledAtUtc, DateTime? StartedAtUtc, DateTime? FinishedAtUtc, int Recipients, CampaignCounts Counts,
    string? CreatedBy, DateTime CreatedAtUtc);
public sealed record CampaignRequest(string? Name, Guid SegmentId, Guid TemplateId, Guid? StoreId, string? Version);
public sealed record ScheduleRequest(DateTime? SendAtUtc, string? Version);
public sealed record VersionRequest(string? Version);
public sealed record WhatsAppMessageRow(Guid Id, Guid? CampaignId, Guid ClientId, string ClientName, string ToNumber, string Template, string Body,
    WhatsAppMessageStatus Status, string? Error, DateTime? SentAtUtc, DateTime? DeliveredAtUtc, DateTime? ReadAtUtc, DateTime CreatedAtUtc);
public sealed record SingleMessageRequest(Guid TemplateId, Guid? StoreId);

/// <summary>
/// Phase E4, marketing: customer segments, WhatsApp templates, campaigns and their messages, a single message to a
/// client, and WhatsApp's webhook for delivery and read statuses.
/// </summary>
public static partial class Endpoints
{
    /// <summary>Every endpoint group of phase E4 (CRM, marketing, promotions and targets).</summary>
    private static void MapE4(WebApplication app)
    {
        MapCrm(app.MapGroup("/api").WithTags("CRM"));
        MapMarketing(app.MapGroup("/api").WithTags("Segments and campaigns"));
        MapPromotions(app.MapGroup("/api").WithTags("Promotions and targets"));
    }

    private static void MapMarketing(RouteGroupBuilder api)
    {
        var campaigns = Need(Permissions.MarketingCampaigns);
        api.MapGet("/segments", SegmentsAsync).RequireAuthorization(campaigns);
        api.MapGet("/segments/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await SegmentRowAsync(api, id))).RequireAuthorization(campaigns);
        api.MapPost("/segments", (SegmentRequest body, ApiRequest api) => SaveSegmentAsync(null, body, api)).RequireAuthorization(campaigns);
        api.MapPut("/segments/{id:guid}", (Guid id, SegmentRequest body, ApiRequest api) => SaveSegmentAsync(id, body, api)).RequireAuthorization(campaigns);
        api.MapDelete("/segments/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var segment = await SegmentAsync(api, id);
            await RequireSegmentChangeAsync(api, segment.StoreId);
            await new SegmentService(await api.ForChangeAsync(segment.StoreId)).DeleteAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(campaigns);
        api.MapPost("/segments/preview", async (PreviewRequest body, ApiRequest api) =>
        {
            var rules = body.Rules ?? new SegmentRules();
            if (rules.StoreId is { } store && !api.User.CanSee(store)) throw ApiException.Forbidden("You may not see that store.");
            var members = await new SegmentService(api.Root).EvaluateAsync(rules, await api.TodayAsync(rules.StoreId ?? api.User.StoreId));
            return Results.Ok(new SegmentResult(members.Count, DateTime.UtcNow, [.. members.Take(200)]));
        }).RequireAuthorization(campaigns);
        api.MapPost("/segments/{id:guid}/evaluate", async (Guid id, ApiRequest api) =>
        {
            var segment = await SegmentAsync(api, id);
            var members = await new SegmentService(api.Root).EvaluateSavedAsync(id, await api.TodayAsync(segment.StoreId ?? api.User.StoreId));
            return Results.Ok(new SegmentResult(members.Count, DateTime.UtcNow, [.. members.Take(200)]));
        }).RequireAuthorization(campaigns);
        api.MapGet("/segments/{id:guid}/csv", async (Guid id, ApiRequest api) =>
        {
            var segment = await SegmentAsync(api, id);
            var members = await new SegmentService(api.Root).EvaluateAsync(SegmentService.Rules(segment), await api.TodayAsync(segment.StoreId ?? api.User.StoreId));
            var name = new string([.. segment.Name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')]).Trim('-');
            return Results.File(SegmentService.Csv(members), "text/csv; charset=utf-8", $"segment-{(name.Length == 0 ? "members" : name)}.csv");
        }).RequireAuthorization(campaigns);

        api.MapGet("/whatsapp/status", (IOptions<WhatsAppOptions> options, IWhatsAppSender sender) =>
        {
            var o = options.Value;
            string? hint = string.IsNullOrWhiteSpace(o.PhoneNumberId) ? null : "…" + o.PhoneNumberId[^Math.Min(4, o.PhoneNumberId.Length)..];
            return Results.Ok(new WhatsAppStatusInfo(sender.Provider == "meta", sender.Provider, hint,
                !string.IsNullOrWhiteSpace(o.VerifyToken) && !string.IsNullOrWhiteSpace(o.AppSecret), o.MessagesPerMinute));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        // The client page's Send on WhatsApp lists the templates too, for users who may send a single message.
        api.MapGet("/whatsapp/templates", async (ApiRequest api) =>
        {
            if (!api.User.Can(Permissions.MarketingCampaigns) && !api.User.Can(Permissions.CrmEdit))
                throw ApiException.Forbidden("Your role cannot see the WhatsApp templates.");
            await using var db = api.Root.Create();
            return Results.Ok((await db.WhatsAppTemplates.AsNoTracking().OrderBy(t => t.Name).ThenBy(t => t.Language).ToListAsync()).Select(ToRow));
        }).RequireAuthorization(Need(Permissions.WebSignIn));
        api.MapPost("/whatsapp/templates", (TemplateRequest body, ApiRequest api) => SaveTemplateAsync(null, body, api)).RequireAuthorization(campaigns);
        api.MapPut("/whatsapp/templates/{id:guid}", (Guid id, TemplateRequest body, ApiRequest api) => SaveTemplateAsync(id, body, api)).RequireAuthorization(campaigns);
        api.MapDelete("/whatsapp/templates/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            await api.RequireCompanyChangeAsync();
            await using (var db = api.Root.Create())
                if (!await db.WhatsAppTemplates.AnyAsync(t => t.Id == id)) throw ApiException.NotFound("That template");
            await new CampaignService(await api.ForChangeAsync()).DeleteTemplateAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(campaigns);

        api.MapGet("/campaigns", CampaignsAsync).RequireAuthorization(campaigns);
        api.MapGet("/campaigns/{id:guid}", async (Guid id, ApiRequest api) => Results.Ok(await CampaignRowAsync(api, id))).RequireAuthorization(campaigns);
        api.MapPost("/campaigns", (CampaignRequest body, ApiRequest api) => SaveCampaignAsync(null, body, api)).RequireAuthorization(campaigns);
        api.MapPut("/campaigns/{id:guid}", (Guid id, CampaignRequest body, ApiRequest api) => SaveCampaignAsync(id, body, api)).RequireAuthorization(campaigns);
        api.MapDelete("/campaigns/{id:guid}", async (Guid id, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Campaign>(api, id, "That campaign");
            await new CampaignService(await api.ForChangeAsync(store)).DeleteCampaignAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(campaigns);
        api.MapPost("/campaigns/{id:guid}/schedule", async (Guid id, ScheduleRequest body, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Campaign>(api, id, "That campaign");
            var when = body.SendAtUtc is { } at ? DateTime.SpecifyKind(at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : at, DateTimeKind.Utc) : (DateTime?)null;
            await new CampaignService(await api.ForChangeAsync(store)).ScheduleAsync(id, ApiRequest.ParseVersion(body.Version), when, await api.TodayAsync(store));
            return Results.Ok(await CampaignRowAsync(api, id));
        }).RequireAuthorization(campaigns);
        api.MapPost("/campaigns/{id:guid}/cancel", async (Guid id, VersionRequest body, ApiRequest api) =>
        {
            var store = await StoreOfAsync<Campaign>(api, id, "That campaign");
            await new CampaignService(await api.ForChangeAsync(store)).CancelAsync(id, ApiRequest.ParseVersion(body.Version));
            return Results.Ok(await CampaignRowAsync(api, id));
        }).RequireAuthorization(campaigns);
        api.MapGet("/campaigns/{id:guid}/messages", async (Guid id, ApiRequest api) =>
        {
            await StoreOfAsync<Campaign>(api, id, "That campaign");
            await using var db = api.Root.Create();
            var q = db.WhatsAppMessages.AsNoTracking().Where(m => m.CampaignId == id);
            if (api.Text("status") is not null) { var status = ParseEnum<WhatsAppMessageStatus>(api, "status"); q = q.Where(m => m.Status == status); }
            var page = await api.PageAsync(q.OrderBy(m => m.CreatedAtUtc), m => m);
            return Results.Ok(new PageOf<WhatsAppMessageRow>(await MessageRowsAsync(db, page.Items), page.Total, page.Page, page.PageSize));
        }).RequireAuthorization(campaigns);
        api.MapGet("/campaigns/{id:guid}/results", async (Guid id, ApiRequest api) =>
        {
            await StoreOfAsync<Campaign>(api, id, "That campaign");
            return Results.Ok(await new CampaignService(api.Root).ResultsAsync(id, api.Number("days") ?? 7));
        }).RequireAuthorization(campaigns);

        api.MapGet("/clients/{id:guid}/whatsapp", async (Guid id, ApiRequest api) =>
        {
            await using var db = api.Root.Create();
            if (!await db.Clients.AnyAsync(c => c.Id == id)) throw ApiException.NotFound("That client");
            var q = db.WhatsAppMessages.AsNoTracking().Where(m => m.ClientId == id);
            if (!api.User.AllStores) q = q.Where(m => m.StoreId == api.User.StoreId);
            return Results.Ok(await MessageRowsAsync(db, await q.OrderByDescending(m => m.CreatedAtUtc).Take(200).ToListAsync()));
        }).RequireAuthorization(Need(Permissions.CrmView));
        api.MapPost("/clients/{id:guid}/whatsapp", async (Guid id, SingleMessageRequest body, ApiRequest api, IWhatsAppSender sender, CancellationToken cancellationToken) =>
        {
            await using (var db = api.Root.Create())
                if (!await db.Clients.AnyAsync(c => c.Id == id, cancellationToken)) throw ApiException.NotFound("That client");
            var store = await NewRecordStoreAsync(api, body.StoreId);
            var message = await new CampaignService(await api.ForChangeAsync(store)).SendSingleAsync(id, body.TemplateId, sender, cancellationToken);
            await using var read = api.Root.Create();
            return Results.Ok((await MessageRowsAsync(read, [message]))[0]);
        }).RequireAuthorization(Need(Permissions.CrmEdit));

        api.MapGet("/whatsapp/webhook", (HttpContext http, IOptions<WhatsAppOptions> options) =>
        {
            var q = http.Request.Query;
            var token = options.Value.VerifyToken;
            if (q["hub.mode"] == "subscribe" && !string.IsNullOrEmpty(token) && string.Equals(q["hub.verify_token"], token, StringComparison.Ordinal))
                return Results.Text(q["hub.challenge"].ToString(), "text/plain");
            return Error(403, "The verify token is not right.");
        }).AllowAnonymous();
        api.MapPost("/whatsapp/webhook", WebhookAsync).AllowAnonymous();
    }

    // Segments ----------------------------------------------------------------------------------------------------------

    private static SegmentRow ToRow(CustomerSegment s, Func<Guid, string?> names)
    {
        var rules = SegmentService.Rules(s);
        return new SegmentRow(s.Id, ApiRequest.Version(s.Version), s.Name, s.Description, rules, rules.Describe(names), s.StoreId, s.LastCount,
            ApiRequest.Utc(s.LastEvaluatedAtUtc));
    }

    private static async Task<Func<Guid, string?>> SegmentNamesAsync(RetailPlusDbContext db)
    {
        var names = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        foreach (var d in await db.Departments.AsNoTracking().ToListAsync()) names[d.Id] = d.Name;
        return id => names.GetValueOrDefault(id);
    }

    /// <summary>A segment the user may see: the company's, or one of a store they see (404 otherwise).</summary>
    private static async Task<CustomerSegment> SegmentAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var s = await db.CustomerSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That segment");
        if (s.StoreId is { } store) api.RequireStore(store, "That segment");
        return s;
    }

    /// <summary>A store's segment is changed by its users; the company's under the company rule.</summary>
    private static async Task RequireSegmentChangeAsync(ApiRequest api, Guid? store)
    {
        if (store is { } s)
        {
            if (!api.User.CanSee(s)) throw ApiException.Forbidden("You may not see that store.");
        }
        else await api.RequireCompanyChangeAsync();
    }

    private static async Task<IResult> SegmentsAsync(ApiRequest api)
    {
        await using var db = api.Root.Create();
        var q = db.CustomerSegments.AsNoTracking();
        if (!api.User.AllStores) q = q.Where(s => s.StoreId == null || s.StoreId == api.User.StoreId);
        var names = await SegmentNamesAsync(db);
        return Results.Ok((await q.OrderBy(s => s.Name).ToListAsync()).Select(s => ToRow(s, names)));
    }

    private static async Task<SegmentRow> SegmentRowAsync(ApiRequest api, Guid id)
    {
        var s = await SegmentAsync(api, id);
        await using var db = api.Root.Create();
        return ToRow(s, await SegmentNamesAsync(db));
    }

    private static async Task<IResult> SaveSegmentAsync(Guid? id, SegmentRequest body, ApiRequest api)
    {
        var rules = body.Rules ?? new SegmentRules();
        CustomerSegment segment;
        if (id is { } existing)
        {
            segment = await SegmentAsync(api, existing);
            await RequireSegmentChangeAsync(api, segment.StoreId);
            segment.Version = ApiRequest.ParseVersion(body.Version);
        }
        else segment = new CustomerSegment();
        await RequireSegmentChangeAsync(api, rules.StoreId);
        segment.Name = body.Name ?? "";
        segment.Description = body.Description;
        var saved = await new SegmentService(await api.ForChangeAsync(rules.StoreId)).SaveAsync(segment, rules);
        var row = await SegmentRowAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/segments/{saved.Id}", row) : Results.Ok(row);
    }

    // Templates ---------------------------------------------------------------------------------------------------------

    private static TemplateRow ToRow(WhatsAppTemplate t) => new(t.Id, ApiRequest.Version(t.Version), t.Name, t.Language, t.Category, t.Body, t.IsActive,
        WhatsAppTemplates.Placeholders(t.Body));

    private static async Task<IResult> SaveTemplateAsync(Guid? id, TemplateRequest body, ApiRequest api)
    {
        await api.RequireCompanyChangeAsync();
        var t = new WhatsAppTemplate { Name = body.Name ?? "", Language = body.Language ?? "", Category = body.Category, Body = body.Body ?? "", IsActive = body.IsActive };
        if (id is { } existing)
        {
            await using var db = api.Root.Create();
            if (!await db.WhatsAppTemplates.AnyAsync(x => x.Id == existing)) throw ApiException.NotFound("That template");
            t.Id = existing;
            t.Version = ApiRequest.ParseVersion(body.Version);
        }
        var saved = await new CampaignService(await api.ForChangeAsync()).SaveTemplateAsync(t);
        await using var read = api.Root.Create();
        var row = ToRow(await read.WhatsAppTemplates.AsNoTracking().SingleAsync(x => x.Id == saved.Id));
        return id is null ? Results.Created($"/api/whatsapp/templates/{saved.Id}", row) : Results.Ok(row);
    }

    // Campaigns ---------------------------------------------------------------------------------------------------------

    private static async Task<List<CampaignRow>> CampaignRowsAsync(RetailPlusDbContext db, ApiRequest api, IReadOnlyList<Campaign> list)
    {
        var ids = list.Select(c => c.Id).ToList();
        var counts = await new CampaignService(api.Root).CountsAsync(ids);
        var segments = await db.CustomerSegments.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        var templates = await db.WhatsAppTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
        return [.. list.Select(c =>
        {
            var n = counts.GetValueOrDefault(c.Id) ?? CampaignCounts.None;
            return new CampaignRow(c.Id, ApiRequest.Version(c.Version), c.Name, c.SegmentId, segments.GetValueOrDefault(c.SegmentId) ?? "", c.TemplateId,
                templates.GetValueOrDefault(c.TemplateId) ?? "", c.Status, c.StoreId, stores.GetValueOrDefault(c.StoreId) ?? "", ApiRequest.Utc(c.ScheduledAtUtc),
                ApiRequest.Utc(c.StartedAtUtc), ApiRequest.Utc(c.FinishedAtUtc), n.Total, n, c.CreatedBy, ApiRequest.Utc(c.CreatedAtUtc));
        })];
    }

    private static async Task<IResult> CampaignsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var q = db.Campaigns.AsNoTracking();
        if (store is { } s) q = q.Where(c => c.StoreId == s);
        if (api.Text("status") is not null) { var status = ParseEnum<CampaignStatus>(api, "status"); q = q.Where(c => c.Status == status); }
        var page = await api.PageAsync(q.OrderByDescending(c => c.CreatedAtUtc), c => c);
        return Results.Ok(new PageOf<CampaignRow>(await CampaignRowsAsync(db, api, page.Items), page.Total, page.Page, page.PageSize));
    }

    private static async Task<CampaignRow> CampaignRowAsync(ApiRequest api, Guid id)
    {
        await using var db = api.Root.Create();
        var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That campaign");
        api.RequireStore(c.StoreId, "That campaign");
        return (await CampaignRowsAsync(db, api, [c]))[0];
    }

    private static async Task<IResult> SaveCampaignAsync(Guid? id, CampaignRequest body, ApiRequest api)
    {
        Guid store;
        Campaign c;
        if (id is { } existing)
        {
            store = await StoreOfAsync<Campaign>(api, existing, "That campaign");
            await using var db = api.Root.Create();
            c = await db.Campaigns.AsNoTracking().SingleAsync(x => x.Id == existing);
            c.Version = ApiRequest.ParseVersion(body.Version);
        }
        else
        {
            store = await NewRecordStoreAsync(api, body.StoreId);
            c = new Campaign { StoreId = store };
        }
        await SegmentAsync(api, body.SegmentId);
        c.Name = body.Name ?? "";
        c.SegmentId = body.SegmentId;
        c.TemplateId = body.TemplateId;
        var saved = await new CampaignService(await api.ForChangeAsync(store)).SaveCampaignAsync(c);
        var row = await CampaignRowAsync(api, saved.Id);
        return id is null ? Results.Created($"/api/campaigns/{saved.Id}", row) : Results.Ok(row);
    }

    // Messages and the webhook ------------------------------------------------------------------------------------------

    private static async Task<List<WhatsAppMessageRow>> MessageRowsAsync(RetailPlusDbContext db, IReadOnlyList<WhatsAppMessage> list)
    {
        var clientIds = list.Select(m => m.ClientId).Distinct().ToList();
        var clients = (await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToListAsync()).ToDictionary(c => c.Id, CrmClientName);
        var templates = await db.WhatsAppTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        return [.. list.Select(m => new WhatsAppMessageRow(m.Id, m.CampaignId, m.ClientId, clients.GetValueOrDefault(m.ClientId) ?? "", m.ToNumber,
            templates.GetValueOrDefault(m.TemplateId) ?? "", m.Body, m.Status, m.Error, ApiRequest.Utc(m.SentAtUtc), ApiRequest.Utc(m.DeliveredAtUtc),
            ApiRequest.Utc(m.ReadAtUtc), ApiRequest.Utc(m.CreatedAtUtc)))];
    }

    /// <summary>
    /// WhatsApp's status notifications: signed with the app secret (anything else is refused with 403), each status moving
    /// its message on. Unknown message ids are ignored, so Meta does not send them again.
    /// </summary>
    private static async Task<IResult> WebhookAsync(HttpContext http, DataContext data, IOptions<WhatsAppOptions> options, ILogger<WhatsAppOptions> log)
    {
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();
        if (!WebhookSignature.IsValid(options.Value.AppSecret, body, http.Request.Headers["X-Hub-Signature-256"]))
        {
            ServerLog.WebhookRefused(log, string.IsNullOrEmpty(options.Value.AppSecret) ? "no app secret is set (WhatsApp:AppSecret)" : "the signature is not right");
            return Error(403, "The signature is not right.");
        }
        WebhookNotification? notification;
        try
        {
            notification = JsonSerializer.Deserialize<WebhookNotification>(body);
        }
        catch (JsonException)
        {
            return Error(400, "The notification is not JSON.");
        }
        var campaigns = new CampaignService(data);
        foreach (var status in notification?.Entry.SelectMany(e => e.Changes).SelectMany(c => c.Value?.Statuses ?? []) ?? [])
        {
            if (string.IsNullOrEmpty(status.Id) || string.IsNullOrEmpty(status.Status)) continue;
            var at = long.TryParse(status.Timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : DateTime.UtcNow;
            var error = status.Errors.FirstOrDefault() is { } e ? $"{e.Code} {e.Title ?? e.Message}".Trim() : null;
            await campaigns.ApplyStatusAsync(status.Id, status.Status, at, error);
        }
        return Results.Ok();
    }
}
