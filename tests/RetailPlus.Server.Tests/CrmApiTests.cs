using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;
using RetailPlus.Sync.Tests;
using static RetailPlus.Server.Tests.BackOffice;

namespace RetailPlus.Server.Tests;

/// <summary>
/// Phase E4's endpoints over the seeded company of <see cref="BackOffice"/>: CRM (stages, leads, opportunities, activities),
/// segments, WhatsApp templates, campaigns, single messages and the webhook, promotions (with a till that uses one and
/// sends its coupon back up), and targets. Each group: the happy path, the refusal without the permission, store scope,
/// and 409 on an old version.
/// </summary>
public sealed class CrmApiTests(BackOffice office) : IClassFixture<BackOffice>
{
    private const string AppSecret = "test-app-secret-0123456789";
    private const string VerifyToken = "test-verify-token";

    private void Require() => Skip.IfNot(office.Available, office.UnavailableReason);

    private static readonly System.Text.Json.JsonSerializerOptions Json = SyncWorld.Json;
    private static readonly int[] EveryDay = [0, 1, 2, 3, 4, 5, 6];

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body) => await ReadAsync<T>(await client.PostAsJsonAsync(url, body, Json));

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body) => await ReadAsync<T>(await client.PutAsJsonAsync(url, body, Json));

    private static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return response.StatusCode;
    }

    private static Task<HttpStatusCode> Post(HttpClient client, string url, object body) => StatusAsync(client.PostAsJsonAsync(url, body, Json));

    private static Task<HttpStatusCode> Put(HttpClient client, string url, object body) => StatusAsync(client.PutAsJsonAsync(url, body, Json));

    private string Today => office.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A client of the company with a WhatsApp number (consent as asked), made on the server.</summary>
    private async Task<Client> ClientAsync(string first, bool optIn, string number)
    {
        var data = office.World.ServerData.For(new Data.StationIdentity(office.StoreA.Id, ApiRequest.ServerStation), null);
        return await new PartyService(data).SaveClientAsync(new Client { FirstName = first, LastName = "Api", WhatsAppNumber = number, WhatsAppOptIn = optIn });
    }

    /// <summary>A cash sale at a till, paid in full (the till's taxes and cash rounding).</summary>
    private static async Task<Sale> SellAtAsync(Till till, SaleDraft draft)
    {
        decimal total;
        await using (var db = till.Data.Create())
        {
            var settings = await db.CurrentStoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
            total = Money.RoundToMinimumTender(draft.Calculate(draft.EffectiveTax(settings)).Total, settings.MinimumTender);
        }
        return await till.Sales.PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, total, total)], await till.SessionAsync());
    }

    // Stages, leads, opportunities and activities -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Stages_are_read_by_crm_users_and_changed_under_the_company_rule()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);
        var stages = await GetAsync<List<StageRow>>(adminA, "/api/crm/stages");
        Assert.Equal(["New", "Qualified", "Proposal", "Won", "Lost"], stages.Take(5).Select(s => s.Name));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/crm/stages")));
        var body = stages.Select(s => new StageRequest(s.Id, s.Name, s.Probability, s.Outcome)).ToList();
        // Two stores: only a user who sees every store changes what they share.
        Assert.Equal(HttpStatusCode.Forbidden, await Put(adminA, "/api/crm/stages", body));
        Assert.Equal(HttpStatusCode.BadRequest, await Put(super, "/api/crm/stages", body.Where(s => s.Outcome != StageOutcome.Lost).ToList()));
        var saved = await PutAsync<List<StageRow>>(super, "/api/crm/stages", body);
        Assert.Equal(stages.Select(s => s.Id), saved.Select(s => s.Id));
    }

    [SkippableFact]
    public async Task Leads_belong_to_a_store_and_are_converted_into_clients()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var viewer = await office.SignInAsync(office.ViewerA);
        var crmOnly = await office.SignInAsync(await office.UserWithAsync("CRM only", office.StoreA.Id, [Permissions.WebSignIn, Permissions.CrmView, Permissions.CrmEdit]));

        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, "/api/leads", new { name = "No code", whatsAppNumber = "03001234567" }));
        var lead = await PostAsync<LeadRow>(adminA, "/api/leads", new { name = "Zara Ahmed", company = "Ahmed & Sons", whatsAppNumber = "+92 321 5550101", source = "Walk-in" });
        Assert.Equal(office.StoreA.Id, lead.StoreId);
        Assert.Equal("+923215550101", lead.WhatsAppNumber);
        Assert.Equal(LeadStatus.Open, lead.Status);
        Assert.NotNull(lead.OwnerName);

        // Store scope: store B's administrator neither lists nor opens it; the super administrator sees it.
        Assert.DoesNotContain((await GetAsync<PageOf<LeadRow>>(adminB, "/api/leads")).Items, l => l.Id == lead.Id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/leads/{lead.Id}")));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(adminB.GetAsync($"/api/leads?storeId={office.StoreA.Id}")));
        Assert.Contains((await GetAsync<PageOf<LeadRow>>(super, "/api/leads?search=Zara")).Items, l => l.Id == lead.Id);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/leads")));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(adminB, $"/api/leads?storeId={office.StoreA.Id}", new { name = "Other store" }));

        var changed = await PutAsync<LeadRow>(adminA, $"/api/leads/{lead.Id}", new { name = lead.Name, company = lead.Company, whatsAppNumber = lead.WhatsAppNumber, notes = "Called", version = lead.Version });
        Assert.Equal("Called", changed.Notes);
        Assert.Equal(HttpStatusCode.Conflict, await Put(adminA, $"/api/leads/{lead.Id}", new { name = "Stale", version = lead.Version }));

        Assert.Equal(HttpStatusCode.Forbidden, await Post(crmOnly, $"/api/leads/{lead.Id}/convert", new { version = changed.Version }));
        var converted = await PostAsync<ConvertResult>(adminA, $"/api/leads/{lead.Id}/convert", new { version = changed.Version });
        Assert.Equal(LeadStatus.Converted, converted.Lead.Status);
        Assert.Equal(converted.ClientId, converted.Lead.ClientId);
        // Numbered from the server's own range.
        Assert.True(long.Parse(converted.ClientNumber!, CultureInfo.InvariantCulture) > (long)ApiRequest.ServerStation * 10_000_000);
        var client = await GetAsync<ClientDetail>(adminA, $"/api/clients/{converted.ClientId}");
        Assert.Equal("+923215550101", client.WhatsAppNumber);
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, $"/api/leads/{lead.Id}/convert", new { version = converted.Lead.Version }));
    }

    [SkippableFact]
    public async Task Opportunities_move_on_the_board_and_the_won_and_lost_report_counts_them()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        var client = await ClientAsync("Board", false, "+923000000101");
        var stages = await GetAsync<List<StageRow>>(adminA, "/api/crm/stages");
        Guid Stage(string name) => stages.Single(s => s.Name == name).Id;
        var big = await PostAsync<OpportunityRow>(adminA, "/api/opportunities", new { title = "Wedding order", clientId = client.Id, expectedValue = 200_000m });
        Assert.Equal("New", big.Stage);
        Assert.Equal(20_000m, big.WeightedValue);
        var small = await PostAsync<OpportunityRow>(adminA, "/api/opportunities", new { title = "Gift boxes", clientId = client.Id, expectedValue = 10_000m, probability = 50m });
        Assert.Equal(5_000m, small.WeightedValue);
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, "/api/opportunities", new { title = "For nobody", expectedValue = 1m }));

        var board = await GetAsync<BoardResult>(adminA, "/api/opportunities/board");
        var column = board.Stages.Single(s => s.Name == "New");
        Assert.Contains(column.Opportunities, o => o.Id == big.Id);
        Assert.Equal(column.Opportunities.Sum(o => o.ExpectedValue), column.Value);
        Assert.DoesNotContain((await GetAsync<BoardResult>(adminB, "/api/opportunities/board")).Stages.SelectMany(s => s.Opportunities), o => o.Id == big.Id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/opportunities/{big.Id}")));

        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, $"/api/opportunities/{small.Id}/move", new { stageId = Stage("Lost"), version = small.Version }));
        var lost = await PostAsync<OpportunityRow>(adminA, $"/api/opportunities/{small.Id}/move", new { stageId = Stage("Lost"), version = small.Version, lostReason = "Price" });
        Assert.Equal((StageOutcome.Lost, office.Today), (lost.Outcome, lost.ClosedOn));
        Assert.Equal(HttpStatusCode.Conflict, await Post(adminA, $"/api/opportunities/{big.Id}/move", new { stageId = Stage("Won"), version = small.Version }));
        var won = await PostAsync<OpportunityRow>(adminA, $"/api/opportunities/{big.Id}/move", new { stageId = Stage("Won"), version = big.Version });
        Assert.Equal(100m, won.Probability);

        var report = await GetAsync<WonLostResult>(adminA, $"/api/opportunities/report?from={Today}&to={Today}");
        Assert.True(report.Won.Count >= 1 && report.Lost.Count >= 1);
        Assert.Contains(report.LostReasons, r => r.Reason == "Price");
        Assert.Contains(report.Closed, o => o.Id == big.Id);
        Assert.Contains((await GetAsync<PageOf<OpportunityRow>>(adminA, $"/api/opportunities?outcome=Won&from={Today}&to={Today}")).Items, o => o.Id == big.Id);
        Assert.DoesNotContain((await GetAsync<WonLostResult>(adminB, $"/api/opportunities/report?from={Today}&to={Today}")).Closed, o => o.Id == big.Id);
    }

    [SkippableFact]
    public async Task My_tasks_show_the_overdue_first_and_only_the_owner_or_an_editor_marks_one_done()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        var viewerName = await office.UserWithAsync("CRM viewer", office.StoreA.Id, [Permissions.WebSignIn, Permissions.CrmView]);
        var viewerId = (await office.AccountAsync(viewerName)).Id;
        using var viewer = await office.SignInAsync(viewerName);
        var client = await ClientAsync("Tasks", false, "+923000000102");
        var yesterday = DateTime.Today.AddDays(-1).AddHours(10).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var tomorrow = DateTime.Today.AddDays(1).AddHours(10).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var mine = await PostAsync<ActivityRow>(adminA, "/api/activities", new { kind = "Call", subject = "Call about the order", clientId = client.Id, ownerId = viewerId, dueAt = yesterday });
        await PostAsync<ActivityRow>(adminA, "/api/activities", new { kind = "Task", subject = "Send the quote", clientId = client.Id, ownerId = viewerId, dueAt = tomorrow });
        var theirs = await PostAsync<ActivityRow>(adminA, "/api/activities", new { kind = "Visit", subject = "Visit", clientId = client.Id, dueAt = yesterday });
        Assert.Equal("overdue", mine.Due);
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, "/api/activities", new { kind = "Note", subject = "On nothing" }));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(viewer, "/api/activities", new { kind = "Note", subject = "Viewer", clientId = client.Id }));

        var tasks = await GetAsync<MyTasksResult>(viewer, "/api/activities/mine");
        Assert.Equal(["Call about the order", "Send the quote"], tasks.Items.Select(i => i.Subject));
        Assert.Equal(1, tasks.Overdue);
        var today = await GetAsync<MyTasksResult>(viewer, "/api/activities/mine?due=today");
        Assert.Equal([mine.Id], today.Items.Select(i => i.Id));

        Assert.Equal(HttpStatusCode.Forbidden, await Post(viewer, $"/api/activities/{theirs.Id}/done", new { done = true, version = theirs.Version }));
        var done = await PostAsync<ActivityRow>(viewer, $"/api/activities/{mine.Id}/done", new { done = true, version = mine.Version });
        Assert.Equal("done", done.Due);
        Assert.Equal(HttpStatusCode.Conflict, await Post(viewer, $"/api/activities/{mine.Id}/done", new { done = false, version = mine.Version }));
        Assert.Single((await GetAsync<MyTasksResult>(viewer, "/api/activities/mine")).Items);
        Assert.Contains((await GetAsync<PageOf<ActivityRow>>(adminA, $"/api/activities?clientId={client.Id}")).Items, a => a.Id == theirs.Id);
    }

    // Segments, templates, campaigns, messages, the webhook -------------------------------------------------------------

    [SkippableFact]
    public async Task A_campaign_goes_to_the_consenting_members_and_the_webhook_moves_its_messages_on()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var reporter = await office.SignInAsync(office.ReporterA);
        var marketer = await office.SignInAsync(await office.UserAsync(Permissions.MarketingRole, office.StoreA.Id));

        // Store A's sales were to walk-ins; two clients buy there now, one of them agreed to WhatsApp.
        var yes = await ClientAsync("Ali", true, "+923009990001");
        var no = await ClientAsync("Bilal", false, "+923009990002");
        foreach (var c in new[] { yes, no })
        {
            var draft = Draft((await office.A1.Inventory.GetAsync(office.ItemA1.Id))!, 1);
            draft.ClientId = c.Id;
            await office.A1.SyncAsync();
            await SellAtAsync(office.A1, draft);
        }
        await office.A1.SyncAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/segments")));
        var rules = new SegmentRules { StoreId = office.StoreA.Id, PeriodDays = 7, MinVisits = 1 };
        var preview = await PostAsync<SegmentResult>(marketer, "/api/segments/preview", new { rules });
        Assert.Contains(preview.Members, m => m.ClientId == yes.Id);
        Assert.Contains(preview.Members, m => m.ClientId == no.Id);
        Assert.Equal(HttpStatusCode.Forbidden, await Post(adminB, "/api/segments/preview", new { rules }));
        var segment = await PostAsync<SegmentRow>(marketer, "/api/segments", new { name = "Bought this week", rules });
        Assert.Equal(office.StoreA.Id, segment.StoreId);
        Assert.Contains("at least 1 visits", segment.Summary);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/segments/{segment.Id}")));
        var evaluated = await PostAsync<SegmentResult>(marketer, $"/api/segments/{segment.Id}/evaluate", new { });
        Assert.Equal(preview.Count, evaluated.Count);
        using (var csv = await marketer.GetAsync($"/api/segments/{segment.Id}/csv"))
        {
            Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Ali Api", await csv.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Conflict, await Put(marketer, $"/api/segments/{segment.Id}", new { name = "Renamed", rules, version = Guid.NewGuid().ToString("N") }));

        // Templates are the company's: changed by the super administrator here (two stores).
        var templateBody = new { name = "visit_thanks_" + Guid.NewGuid().ToString("N")[..6], language = "en", category = "Marketing", body = "Thank you {FirstName}! See you at {Store}.", isActive = true };
        Assert.Equal(HttpStatusCode.Forbidden, await Post(marketer, "/api/whatsapp/templates", templateBody));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(super, "/api/whatsapp/templates", templateBody with { body = "Hi {Nickname}" }));
        var template = await PostAsync<TemplateRow>(super, "/api/whatsapp/templates", templateBody);
        Assert.Equal(["FirstName", "Store"], template.Placeholders);
        Assert.Contains((await GetAsync<List<TemplateRow>>(adminA, "/api/whatsapp/templates")), t => t.Id == template.Id);
        Assert.Equal("recording", (await GetAsync<WhatsAppStatusInfo>(reporter, "/api/whatsapp/status")).Provider);

        var campaign = await PostAsync<CampaignRow>(marketer, "/api/campaigns", new { name = "Thank you", segmentId = segment.Id, templateId = template.Id });
        Assert.Equal((CampaignStatus.Draft, office.StoreA.Id), (campaign.Status, campaign.StoreId));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/campaigns/{campaign.Id}")));
        Assert.Equal(HttpStatusCode.Conflict, await Post(marketer, $"/api/campaigns/{campaign.Id}/schedule", new { version = Guid.NewGuid().ToString("N") }));
        var scheduled = await PostAsync<CampaignRow>(marketer, $"/api/campaigns/{campaign.Id}/schedule", new { sendAtUtc = (DateTime?)null, version = campaign.Version });
        Assert.Equal(CampaignStatus.Scheduled, scheduled.Status);
        Assert.Equal(1, scheduled.Counts.Queued + scheduled.Counts.Sent);   // the worker may already have sent it

        // The server's sender (the recording stand-in here): what the background worker does, run now.
        var sender = office.World.Server.Services.GetRequiredService<RecordingWhatsAppSender>();
        await new CampaignService(office.World.ServerData).SendDueAsync(sender, 100, DateTime.UtcNow.AddMinutes(1));
        var sent = await GetAsync<CampaignRow>(marketer, $"/api/campaigns/{campaign.Id}");
        Assert.Equal(CampaignStatus.Sent, sent.Status);
        Assert.Equal(1, sent.Counts.Sent);
        var message = (await GetAsync<PageOf<WhatsAppMessageRow>>(marketer, $"/api/campaigns/{campaign.Id}/messages")).Items.Single();
        Assert.Equal((yes.Id, "+923009990001"), (message.ClientId, message.ToNumber));
        Assert.StartsWith("Thank you Ali!", message.Body);
        var wamid = sender.Sent.Single(s => s.Message.To == "+923009990001" && s.Message.TemplateName == template.Name).Id;

        // WhatsApp's webhook, on a server with the app secret and verify token set.
        using var hooked = office.World.Server.WithWebHostBuilder(b =>
        {
            b.UseSetting("WhatsApp:AppSecret", AppSecret);
            b.UseSetting("WhatsApp:VerifyToken", VerifyToken);
            b.UseSetting("WhatsApp:WorkerIntervalSeconds", "0");
        });
        using var meta = hooked.CreateClient();
        Assert.Equal("12345", await meta.GetStringAsync($"/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=12345"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(meta.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1")));
        string Notification(string status) =>
            $$$"""{"object":"whatsapp_business_account","entry":[{"changes":[{"value":{"statuses":[{"id":"{{{wamid}}}","status":"{{{status}}}","timestamp":"{{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}","recipient_id":"923009990001"}]}}]}]}""";
        Task<HttpResponseMessage> Hook(string body, string? signature)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (signature is not null) request.Headers.Add("X-Hub-Signature-256", signature);
            return meta.SendAsync(request);
        }
        var delivered = Notification("delivered");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Hook(delivered, null)));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Hook(delivered, WebhookSignature.Sign("not-the-secret", Encoding.UTF8.GetBytes(delivered)))));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(Hook(delivered, WebhookSignature.Sign(AppSecret, Encoding.UTF8.GetBytes(delivered)))));
        var read = Notification("read");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(Hook(read, WebhookSignature.Sign(AppSecret, Encoding.UTF8.GetBytes(read)))));
        var after = await GetAsync<CampaignRow>(marketer, $"/api/campaigns/{campaign.Id}");
        Assert.Equal(1, after.Counts.Read);
        var results = await GetAsync<CampaignResults>(marketer, $"/api/campaigns/{campaign.Id}/results?days=7");
        Assert.Equal(1, results.Recipients);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(marketer.DeleteAsync($"/api/campaigns/{campaign.Id}")));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(super.DeleteAsync($"/api/whatsapp/templates/{template.Id}")));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(marketer.DeleteAsync($"/api/segments/{segment.Id}")));
    }

    [SkippableFact]
    public async Task A_client_page_takes_consent_and_sends_a_single_message_only_with_it()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var viewer = await office.SignInAsync(office.ViewerA);
        var client = await ClientAsync("Single", false, "+923008880001");
        var detail = await GetAsync<ClientDetail>(super, $"/api/clients/{client.Id}");
        Assert.False(detail.WhatsAppOptIn);
        Assert.Null(detail.WhatsAppOptInAtUtc);
        var template = await PostAsync<TemplateRow>(super, "/api/whatsapp/templates",
            new { name = "order_ready_" + Guid.NewGuid().ToString("N")[..6], language = "en", category = "Utility", body = "Hello {Name}, your order is ready.", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, $"/api/clients/{client.Id}/whatsapp", new { templateId = template.Id }));

        // Two stores: clients are changed by the super administrator. Without the consent fields a PUT keeps them.
        var withConsent = await PutAsync<ClientDetail>(super, $"/api/clients/{client.Id}", detail with { WhatsAppOptIn = true, WhatsAppNumber = "0092 300 8880001", Birthday = new DateOnly(1991, 3, 4) });
        Assert.Equal((true, "+923008880001", new DateOnly(1991, 3, 4)), (withConsent.WhatsAppOptIn!.Value, withConsent.WhatsAppNumber, withConsent.Birthday!.Value));
        Assert.NotNull(withConsent.WhatsAppOptInAtUtc);
        var kept = await PutAsync<ClientDetail>(super, $"/api/clients/{client.Id}", withConsent with { City = "Lahore", WhatsAppOptIn = null, WhatsAppNumber = null, Birthday = null });
        Assert.True(kept.WhatsAppOptIn);
        Assert.Equal(new DateOnly(1991, 3, 4), kept.Birthday);

        Assert.Equal(HttpStatusCode.Forbidden, await Post(viewer, $"/api/clients/{client.Id}/whatsapp", new { templateId = template.Id }));
        var message = await PostAsync<WhatsAppMessageRow>(adminA, $"/api/clients/{client.Id}/whatsapp", new { templateId = template.Id });
        Assert.Equal(WhatsAppMessageStatus.Sent, message.Status);
        Assert.Equal("Hello Single Api, your order is ready.", message.Body);
        Assert.Contains((await GetAsync<List<WhatsAppMessageRow>>(adminA, $"/api/clients/{client.Id}/whatsapp")), m => m.Id == message.Id);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync($"/api/clients/{client.Id}/whatsapp")));
    }

    // Promotions ----------------------------------------------------------------------------------------------------------

    private static Dictionary<string, object?> Promotion(string name, string kind, bool allStores, Guid[] stores, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name, ["kind"] = kind, ["isActive"] = true, ["priority"] = 0, ["combinable"] = true, ["allStores"] = allStores, ["storeIds"] = stores,
            ["couponMaxUses"] = 0, ["days"] = Array.Empty<int>(), ["scope"] = "AllItems", ["targets"] = Array.Empty<object>(), ["percent"] = 0m, ["amount"] = 0m,
            ["buyQuantity"] = 0, ["getQuantity"] = 0, ["getPercent"] = 100m, ["rewardTargets"] = Array.Empty<object>(), ["bundlePrice"] = 0m,
            ["bundleParts"] = Array.Empty<object>(), ["spendThreshold"] = 0m,
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return body;
    }

    [SkippableFact]
    public async Task Promotions_go_down_to_the_tills_and_a_coupon_used_at_one_comes_back_up_and_reaches_the_others()
    {
        Require();
        using var super = await office.SignInAsync();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var reporter = await office.SignInAsync(office.ReporterA);
        var code = "API" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // A store's administrator changes promotions of their own store only.
        Assert.Equal(HttpStatusCode.Forbidden, await Post(adminA, "/api/promotions", Promotion("Everywhere", "PercentOff", true, [], new { percent = 5m })));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(adminA, "/api/promotions", Promotion("At B", "PercentOff", false, [office.StoreB.Id], new { percent = 5m })));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, "/api/promotions", Promotion("No percent", "PercentOff", false, [office.StoreA.Id])));
        var local = await PostAsync<PromotionDetail>(adminA, "/api/promotions", Promotion("Snacks 10% at A", "PercentOff", false, [office.StoreA.Id],
            new { percent = 10m, scope = "Items", targets = new[] { new { plu = office.ItemA1.Plu, quantity = 1m } }, days = EveryDay }));
        Assert.Equal([office.StoreA.Id], local.StoreIds);
        Assert.Equal(7, local.Days.Count);
        Assert.Contains("10% off " + office.ItemA1.Plu, local.Summary);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/promotions")));
        Assert.DoesNotContain((await GetAsync<List<PromotionRow>>(adminB, "/api/promotions")), p => p.Id == local.Id);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(adminB.GetAsync($"/api/promotions/{local.Id}")));
        var coupon = await PostAsync<PromotionDetail>(super, "/api/promotions", Promotion("Coupon 15 off", "SpendOver", true, [],
            new { spendThreshold = 20m, amount = 15m, couponCode = code.ToLowerInvariant(), couponMaxUses = 1 }));
        Assert.Equal(code, coupon.CouponCode);
        Assert.Equal(HttpStatusCode.Conflict, await Put(super, $"/api/promotions/{coupon.Id}", Promotion("Coupon 15 off", "SpendOver", true, [],
            new { spendThreshold = 20m, amount = 15m, couponCode = code, couponMaxUses = 1, version = local.Version })));

        // Till A1 receives both, works them out on a sale with the coupon, and sends the sale and the coupon's use up.
        await office.A1.SyncAsync();
        var draft = Draft((await office.A1.Inventory.GetAsync(office.ItemA1.Id))!, 3);
        draft.CouponCodes = [code];
        var outcome = await new PromotionService(office.A1.Data).ApplyAsync(draft);
        Assert.Equal(3m + 15m, outcome.Total);
        var sale = await SellAtAsync(office.A1, draft);
        await office.A1.SyncAsync();

        await using (var db = office.World.ServerData.Create())
        {
            Assert.True(await db.CouponUses.AnyAsync(u => u.SaleId == sale.Id && u.PromotionId == coupon.Id));
            var lines = await db.SaleLines.Where(l => l.SaleId == sale.Id).ToListAsync();
            Assert.Equal(local.Id, lines.Single(l => l.Kind == SaleLineKind.Sale).PromotionId);
            Assert.Equal(coupon.Id, lines.Single(l => l.Kind == SaleLineKind.Coupon).PromotionId);
        }
        var report = await GetAsync<List<PromotionReportRow>>(adminA, $"/api/promotions/report?from={Today}&to={Today}");
        Assert.Equal(3m, report.Single(r => r.PromotionId == local.Id).Discount);
        Assert.Equal((15m, 1), (report.Single(r => r.PromotionId == coupon.Id).Discount, report.Single(r => r.PromotionId == coupon.Id).CouponUses));
        Assert.Equal(1, (await GetAsync<PromotionDetail>(super, $"/api/promotions/{coupon.Id}")).CouponUses);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(super.DeleteAsync($"/api/promotions/{coupon.Id}")));

        // Store B's till hears of the use, so the one-use coupon is used up there too; it never gets store A's promotion's use.
        await office.B1.SyncAsync();
        var atB = Draft((await office.B1.Inventory.GetAsync(office.ItemB1.Id))!, 5);
        atB.CouponCodes = [code];
        var refused = await new PromotionService(office.B1.Data).ApplyAsync(atB);
        Assert.Contains("used up", refused.Notes.Single());
        await using (var tillB = office.B1.Data.Create())
        {
            Assert.True(await tillB.Promotions.AnyAsync(p => p.Id == local.Id));   // every till has every promotion; the engine keeps those of its store
            Assert.DoesNotContain(await new PromotionService(office.B1.Data).ForStoreAsync(office.StoreB.Id, office.Today), p => p.Id == local.Id);
        }
    }

    // Targets -------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Targets_compare_the_month_with_net_sales_per_store_and_salesperson()
    {
        Require();
        using var adminA = await office.SignInAsync(office.AdminA);
        using var adminB = await office.SignInAsync(office.AdminB);
        using var viewer = await office.SignInAsync(office.ViewerA);
        using var reporter = await office.SignInAsync(office.ReporterA);
        var today = office.Today;
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(viewer.GetAsync("/api/targets")));
        var store = await PostAsync<SalesTargetRow>(adminA, "/api/targets", new { storeId = office.StoreA.Id, year = today.Year, month = today.Month, amount = 1000m });
        var person = await PostAsync<SalesTargetRow>(adminA, "/api/targets", new { storeId = office.StoreA.Id, userId = office.CashierA.Id, year = today.Year, month = today.Month, amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, await Post(adminA, "/api/targets", new { storeId = office.StoreA.Id, year = today.Year, month = today.Month, amount = 5m }));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(adminB, "/api/targets", new { storeId = office.StoreA.Id, year = today.Year, month = today.Month, amount = 5m }));

        var first = new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = await GetAsync<List<SalesTargetRow>>(adminA, "/api/targets");
        var dashboard = await GetAsync<System.Text.Json.JsonElement>(adminA, $"/api/dashboard?from={first}&to={Today}&storeId={office.StoreA.Id}");
        decimal net = dashboard.GetProperty("totals").GetProperty("netSales").GetDecimal();
        var storeRow = rows.Single(r => r.Id == store.Id);
        Assert.Equal(net, storeRow.Actual);
        Assert.Equal(TargetService.Percent(net, 1000m), storeRow.Percent);
        Assert.Null(storeRow.UserId);
        Assert.Equal(office.CashierA.Id, rows.Single(r => r.Id == person.Id).UserId);
        Assert.DoesNotContain((await GetAsync<List<SalesTargetRow>>(adminB, "/api/targets")), r => r.Id == store.Id);
        Assert.Equal(HttpStatusCode.NotFound, await Put(adminB, $"/api/targets/{store.Id}", new { amount = 1m, version = store.Version }));

        var changed = await PutAsync<SalesTargetRow>(adminA, $"/api/targets/{store.Id}", new { amount = 2000m, version = store.Version });
        Assert.Equal(2000m, changed.Amount);
        Assert.Equal(HttpStatusCode.Conflict, await Put(adminA, $"/api/targets/{store.Id}", new { amount = 3000m, version = store.Version }));

        var progress = await GetAsync<TargetProgress>(adminA, "/api/targets/progress");
        Assert.Equal((today.Year, today.Month, today.Day), (progress.Year, progress.Month, progress.Day));
        Assert.Equal(2000m, progress.Stores.Single(s => s.StoreId == office.StoreA.Id).Target);
        Assert.Contains(progress.People, p => p.UserId == office.CashierA.Id);
        // The dashboard's tile: dashboard.view is enough to read it, reports alone are not.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reporter.GetAsync("/api/targets/progress")));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(adminA.DeleteAsync($"/api/targets/{person.Id}")));
    }

    // The real sender, without the network --------------------------------------------------------------------------------

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"messages":[{"id":"wamid.HBgM"}]}""", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task The_Meta_sender_posts_a_template_message_with_its_parameters_to_the_Graph_API()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new WhatsAppOptions { Enabled = true, PhoneNumberId = "1234567890", AccessToken = "token-for-the-test", ApiVersion = "v21.0" });
        var sender = new MetaCloudWhatsAppSender(new HttpClient(handler), options);
        var result = await sender.SendAsync(new WhatsAppOutgoing("+923001234567", "points_reminder", "en_US", ["Ali", "120"]));
        Assert.True(result.Ok);
        Assert.Equal("wamid.HBgM", result.MessageId);
        Assert.Equal("https://graph.facebook.com/v21.0/1234567890/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer token-for-the-test", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"to\":\"923001234567\"", handler.Body);
        Assert.Contains("\"name\":\"points_reminder\"", handler.Body);
        Assert.Contains("\"code\":\"en_US\"", handler.Body);
        Assert.Contains("{\"type\":\"text\",\"text\":\"Ali\"}", handler.Body);
        Assert.True(options.Value.UsesMeta);
        Assert.False(new WhatsAppOptions { Enabled = true }.UsesMeta);
        Assert.True(WebhookSignature.IsValid("s", Encoding.UTF8.GetBytes("{}"), WebhookSignature.Sign("s", Encoding.UTF8.GetBytes("{}"))));
        Assert.False(WebhookSignature.IsValid("", Encoding.UTF8.GetBytes("{}"), WebhookSignature.Sign("", Encoding.UTF8.GetBytes("{}"))));
    }
}
