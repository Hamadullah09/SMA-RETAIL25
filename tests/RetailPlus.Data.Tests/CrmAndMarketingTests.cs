using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Phase E4 against a real database: leads, the pipeline and activities, segments, WhatsApp campaigns with the recording
/// sender, promotions on posted sales (lines, coupons, voids, the report), targets and WhatsApp consent.
/// </summary>
public abstract class CrmAndMarketingTests(DatabaseFixture db)
{
    private static int _station = 400;

    /// <summary>A store of its own with a POS location, and services working for a new station of it.</summary>
    private async Task<(DataContext Data, SessionInfo Session, Guid StoreId)> StoreAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        int station = Interlocked.Increment(ref _station);
        var store = new Store { Code = "E" + station, Name = "Store " + station };
        var location = new Location { Code = "POS", Name = "Sales floor" };
        await using (var ctx = db.CreateContext())
        {
            ctx.Stores.Add(store);
            await ctx.SaveChangesAsync();
            location.StoreId = store.Id;
            ctx.Locations.Add(location);
            await ctx.SaveChangesAsync();
        }
        var data = new DataContext(db.Connection, new StationIdentity(store.Id, station));
        return (data, new SessionInfo { StationNumber = station, LocationId = location.Id, LocationCode = "POS", UserName = "tester", AccessLevel = 4 }, store.Id);
    }

    private static async Task<Client> ClientAsync(DataContext data, string first, string? whatsApp = null, bool optIn = false, DateOnly? birthday = null)
    {
        var client = new Client { FirstName = first, LastName = "Test", WhatsAppNumber = whatsApp, WhatsAppOptIn = optIn, Birthday = birthday };
        return await new PartyService(data).SaveClientAsync(client);
    }

    /// <summary>The department name of each item made here, which sale lines carry.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> Departments = new();

    private static async Task<Item> ItemAsync(DataContext data, SessionInfo session, string plu, decimal price, string department = "E4DEPT")
    {
        var item = await new InventoryService(data).SaveAsync(new Item
        {
            LocationId = session.LocationId, Plu = plu, Name = "Item " + plu, Price1 = price, LastCost = 1, AverageCost = 1, OnHand = 100,
        }, department, null, null);
        Departments[item.Id] = department;
        return item;
    }

    private static SaleDraftLine Line(Item item, decimal qty) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = item.Price1, UnitCost = item.AverageCost,
        DepartmentId = item.DepartmentId, CategoryId = item.CategoryId, Department = Departments.GetValueOrDefault(item.Id),
    };

    /// <summary>Posts a cash sale for the client, dated some days back if asked.</summary>
    private static async Task<Sale> SellAsync(DataContext data, SessionInfo session, SaleDraft draft, int daysAgo = 0)
    {
        decimal total = draft.Calculate(TaxSetup.None).Total;
        var sale = await new SalesService(data).PostSaleAsync(draft, [new TenderEntry("Cash", TenderKind.Cash, total, total)], session);
        if (daysAgo != 0)
        {
            await using var ctx = data.Create();
            var at = sale.SoldAt.AddDays(-daysAgo);
            await ctx.Sales.Where(s => s.Id == sale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SoldAt, at));
        }
        return sale;
    }

    private static Task<Sale> SellAsync(DataContext data, SessionInfo session, Client? client, Item item, decimal qty, int daysAgo = 0) =>
        SellAsync(data, session, new SaleDraft { ClientId = client?.Id, ClientName = client?.FirstName, Lines = [Line(item, qty)] }, daysAgo);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // Leads, opportunities, activities --------------------------------------------------------------------------------

    [SkippableFact]
    public async Task The_pipeline_starts_with_the_default_stages_and_keeps_an_open_a_won_and_a_lost_one()
    {
        var (data, _, _) = await StoreAsync();
        var crm = new CrmService(data);
        var stages = await crm.StagesAsync();
        Assert.Equal(["New", "Qualified", "Proposal", "Won", "Lost"], stages.Select(s => s.Name));
        var noWon = stages.Where(s => s.Outcome != StageOutcome.Won).ToList();
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.SaveStagesAsync(noWon));
        // Renamed and reordered, with a new stage between.
        var list = stages.ToList();
        list[0].Name = "Enquiry";
        list.Insert(2, new OpportunityStage { Name = "Visit", Probability = 45, Outcome = StageOutcome.Open });
        var saved = await crm.SaveStagesAsync(list);
        Assert.Equal(["Enquiry", "Qualified", "Visit", "Proposal", "Won", "Lost"], saved.Select(s => s.Name));
        Assert.Equal(stages[0].Id, saved[0].Id);
    }

    [SkippableFact]
    public async Task A_lead_becomes_a_client_and_its_opportunities_and_activities_follow()
    {
        var (data, _, storeId) = await StoreAsync();
        var crm = new CrmService(data);
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.SaveLeadAsync(new Lead { Name = "Bad", WhatsAppNumber = "0300 1234567" }));
        var lead = await crm.SaveLeadAsync(new Lead { Name = "Sana Malik", Company = "Malik Traders", Phone = "042-111", WhatsAppNumber = "+92 300 7654321", Source = "Walk-in" });
        Assert.Equal("+923007654321", lead.WhatsAppNumber);
        Assert.Equal(storeId, lead.StoreId);
        var opportunity = await crm.SaveOpportunityAsync(new Opportunity { Title = "Office chairs", LeadId = lead.Id, ExpectedValue = 50_000 }, Today, probabilityGiven: false);
        Assert.Equal(10m, opportunity.Probability);
        var call = await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Call, Subject = "Call back", LeadId = lead.Id, DueAt = DateTime.Today.AddHours(15) });

        var stale = lead.Version;
        lead.Notes = "Wants a quote";
        lead = await crm.SaveLeadAsync(lead);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => crm.ConvertLeadAsync(lead.Id, stale, null, "E4-" + Guid.NewGuid().ToString("N")[..6]));
        var (converted, client) = await crm.ConvertLeadAsync(lead.Id, lead.Version, null, "E4-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(LeadStatus.Converted, converted.Status);
        Assert.Equal("Sana", client.FirstName);
        Assert.Equal("Malik", client.LastName);
        Assert.Equal("+923007654321", client.WhatsAppNumber);
        Assert.False(client.WhatsAppOptIn);
        await using (var ctx = db.CreateContext())
        {
            Assert.Equal(client.Id, (await ctx.Opportunities.SingleAsync(o => o.Id == opportunity.Id)).ClientId);
            Assert.Equal(client.Id, (await ctx.CrmActivities.SingleAsync(a => a.Id == call.Id)).ClientId);
            Assert.Equal(client.Id, (await ctx.Leads.SingleAsync(l => l.Id == lead.Id)).ConvertedClientId);
        }
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.ConvertLeadAsync(lead.Id, converted.Version, null));
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.DeleteLeadAsync(lead.Id));
    }

    [SkippableFact]
    public async Task Opportunities_move_through_the_stages_and_the_report_counts_won_and_lost()
    {
        var (data, _, storeId) = await StoreAsync();
        var crm = new CrmService(data);
        var stages = await crm.StagesAsync();
        Guid Stage(string name) => stages.Single(s => s.Name == name).Id;
        var client = await ClientAsync(data, "Pipe");
        var a = await crm.SaveOpportunityAsync(new Opportunity { Title = "Uniforms", ClientId = client.Id, ExpectedValue = 100_000 }, Today, probabilityGiven: false);
        var b = await crm.SaveOpportunityAsync(new Opportunity { Title = "Catering", ClientId = client.Id, ExpectedValue = 40_000, Probability = 75 }, Today);
        Assert.Equal(75m, b.Probability);

        a = await crm.MoveOpportunityAsync(a.Id, a.Version, Stage("Proposal"), null, Today);
        Assert.Equal(60m, a.Probability);
        Assert.Null(a.ClosedOn);
        a = await crm.MoveOpportunityAsync(a.Id, a.Version, Stage("Won"), null, Today);
        Assert.Equal(Today, a.ClosedOn);
        Assert.Equal(100m, a.Probability);
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.MoveOpportunityAsync(b.Id, b.Version, Stage("Lost"), null, Today));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => crm.MoveOpportunityAsync(b.Id, Guid.NewGuid(), Stage("Lost"), "Too dear", Today));
        b = await crm.MoveOpportunityAsync(b.Id, b.Version, Stage("Lost"), "Too dear", Today);
        Assert.Equal("Too dear", b.LostReason);

        var report = await crm.WonLostAsync(Today, Today, [storeId]);
        Assert.Equal(1, report.Won);
        Assert.Equal(100_000m, report.WonValue);
        Assert.Equal(1, report.Lost);
        Assert.Equal(50m, report.WinRate);
        Assert.Equal("Too dear", report.LostReasons.Single().Reason);

        // Back to an open stage: open again, no reason, no close date.
        b = await crm.MoveOpportunityAsync(b.Id, b.Version, Stage("Qualified"), null, Today);
        Assert.Null(b.ClosedOn);
        Assert.Null(b.LostReason);
        Assert.Equal(0, (await crm.WonLostAsync(Today, Today, [storeId])).Lost);
        await using var ctx = db.CreateContext();
        Assert.True(await ctx.AuditEntries.AnyAsync(e => e.Action == AuditActions.OpportunityWon && e.EntityId == a.Id));
    }

    [SkippableFact]
    public async Task My_tasks_put_the_overdue_first_and_today_only_keeps_overdue_and_today()
    {
        var (data, _, _) = await StoreAsync();
        var owner = Guid.NewGuid();
        var crm = new CrmService(data);
        var client = await ClientAsync(data, "Tasks");
        var later = await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Task, Subject = "Later", ClientId = client.Id, OwnerId = owner, DueAt = DateTime.Today.AddDays(3) });
        var today = await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Visit, Subject = "Today", ClientId = client.Id, OwnerId = owner, DueAt = DateTime.Today.AddHours(16) });
        var overdue = await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Call, Subject = "Overdue", ClientId = client.Id, OwnerId = owner, DueAt = DateTime.Today.AddDays(-2) });
        var undated = await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Note, Subject = "Undated", ClientId = client.Id, OwnerId = owner });
        await crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Task, Subject = "Someone else's", ClientId = client.Id, OwnerId = Guid.NewGuid(), DueAt = DateTime.Today });
        await Assert.ThrowsAsync<BusinessRuleException>(() => crm.SaveActivityAsync(new CrmActivity { Kind = ActivityKind.Task, Subject = "Nobody's" }));

        var mine = await crm.MineAsync(owner, Today);
        Assert.Equal([overdue.Id, today.Id, later.Id, undated.Id], mine.Select(a => a.Id));
        Assert.Equal(["overdue", "today", "later", "none"], mine.Select(a => CrmService.Due(a, Today)));
        Assert.Equal([overdue.Id, today.Id], (await crm.MineAsync(owner, Today, todayOnly: true)).Select(a => a.Id));

        var done = await crm.SetDoneAsync(overdue.Id, overdue.Version, true);
        Assert.NotNull(done.DoneAtUtc);
        Assert.Equal("done", CrmService.Due(done, Today));
        Assert.Equal(3, (await crm.MineAsync(owner, Today)).Count);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => crm.SetDoneAsync(overdue.Id, overdue.Version, false));
    }

    // Segments --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Segments_find_big_spenders_lapsed_clients_birthdays_buyers_of_a_department_and_consent()
    {
        var (data, session, storeId) = await StoreAsync();
        var big = await ClientAsync(data, "Big", "+923001110001", optIn: true);
        var small = await ClientAsync(data, "Small", "+923001110002");
        var lapsed = await ClientAsync(data, "Lapsed", birthday: new DateOnly(1990, (DateTime.Today.Month % 12) + 1, 5));
        var item = await ItemAsync(data, session, "SEG-" + session.StationNumber, 100m, "SEGDEPT" + session.StationNumber);
        var other = await ItemAsync(data, session, "SEG2-" + session.StationNumber, 10m);
        await SellAsync(data, session, big, item, 6, daysAgo: 3);
        await SellAsync(data, session, big, item, 6, daysAgo: 1);
        await SellAsync(data, session, small, other, 2, daysAgo: 2);
        await SellAsync(data, session, lapsed, other, 1, daysAgo: 200);
        var segments = new SegmentService(data);

        var spenders = await segments.EvaluateAsync(new SegmentRules { StoreId = storeId, PeriodDays = 30, MinSpend = 1000, MinVisits = 2 }, Today);
        Assert.Equal([big.Id], spenders.Select(m => m.ClientId));
        Assert.Equal(1200m, spenders[0].Spend);
        Assert.Equal(2, spenders[0].Visits);

        var gone = await segments.EvaluateAsync(new SegmentRules { StoreId = storeId, NoPurchaseForDays = 90 }, Today);
        Assert.Contains(gone, m => m.ClientId == lapsed.Id);
        Assert.DoesNotContain(gone, m => m.ClientId == big.Id || m.ClientId == small.Id);

        await using (var ctx = db.CreateContext())
        {
            var deptId = await ctx.Departments.Where(d => d.Name == "SEGDEPT" + session.StationNumber).Select(d => d.Id).SingleAsync();
            var buyers = await segments.EvaluateAsync(new SegmentRules { DepartmentId = deptId, PeriodDays = 30 }, Today);
            Assert.Equal([big.Id], buyers.Select(m => m.ClientId));
        }

        var birthdays = await segments.EvaluateAsync(new SegmentRules { BirthdayMonth = (DateTime.Today.Month % 12) + 1 }, Today);
        Assert.Contains(birthdays, m => m.ClientId == lapsed.Id);
        var consenting = await segments.EvaluateAsync(new SegmentRules { WhatsAppConsent = true, StoreId = storeId, MinVisits = 1 }, Today);
        Assert.Equal([big.Id], consenting.Select(m => m.ClientId));

        var saved = await segments.SaveAsync(new CustomerSegment { Name = "Big spenders" }, new SegmentRules { StoreId = storeId, PeriodDays = 30, MinSpend = 1000 });
        Assert.Equal(storeId, saved.StoreId);
        await segments.EvaluateSavedAsync(saved.Id, Today);
        await using (var ctx = db.CreateContext())
            Assert.Equal(1, (await ctx.CustomerSegments.SingleAsync(s => s.Id == saved.Id)).LastCount);
        var csv = System.Text.Encoding.UTF8.GetString(SegmentService.Csv(spenders));
        Assert.Contains("Big Test", csv);
        Assert.StartsWith("﻿Number,Name", csv);
        await Assert.ThrowsAsync<BusinessRuleException>(() => segments.SaveAsync(new CustomerSegment { Name = "Wrong" }, new SegmentRules { MinSpend = 5, MaxSpend = 1 }));
    }

    // Campaigns -------------------------------------------------------------------------------------------------------

    private static async Task<WhatsAppTemplate> TemplateAsync(DataContext data) =>
        await new CampaignService(data).SaveTemplateAsync(new WhatsAppTemplate
        {
            Name = "points_" + Guid.NewGuid().ToString("N")[..8], Language = "en", Category = WhatsAppTemplateCategory.Marketing,
            Body = "Hi {FirstName}, you have {Points} points at {Store}.",
        });

    [SkippableFact]
    public async Task A_campaign_goes_only_to_consenting_numbers_slowly_and_its_statuses_move_forward()
    {
        var (data, session, storeId) = await StoreAsync();
        var yes1 = await ClientAsync(data, "Yes", "+923002220001", optIn: true);
        var yes2 = await ClientAsync(data, "Withdrew", "+923002220002", optIn: true);
        var yes3 = await ClientAsync(data, "Refused", "+923002220003", optIn: true);
        var no = await ClientAsync(data, "No", "+923002220004");
        var item = await ItemAsync(data, session, "CMP-" + session.StationNumber, 50m);
        foreach (var c in new[] { yes1, yes2, yes3, no }) await SellAsync(data, session, c, item, 1, daysAgo: 1);

        var campaigns = new CampaignService(data);
        await Assert.ThrowsAsync<BusinessRuleException>(() => campaigns.SaveTemplateAsync(new WhatsAppTemplate { Name = "Bad Name", Language = "en", Body = "Hi" }));
        var template = await TemplateAsync(data);
        var segment = await new SegmentService(data).SaveAsync(new CustomerSegment { Name = "Bought here" }, new SegmentRules { StoreId = storeId, MinVisits = 1 });
        var campaign = await campaigns.SaveCampaignAsync(new Campaign { Name = "November points", SegmentId = segment.Id, TemplateId = template.Id });
        Assert.Equal(CampaignStatus.Draft, campaign.Status);

        int recipients = await campaigns.ScheduleAsync(campaign.Id, campaign.Version, DateTime.UtcNow.AddHours(1), Today);
        Assert.Equal(3, recipients);
        var sender = new RecordingWhatsAppSender();
        Assert.Equal(0, await campaigns.SendDueAsync(sender, 10));   // not due yet

        // Consent is checked again when sending; WhatsApp may refuse a number.
        var withdrawn = await new PartyService(data).GetClientAsync(yes2.Id);
        withdrawn!.WhatsAppOptIn = false;
        await new PartyService(data).SaveClientAsync(withdrawn);
        sender.Refuse["+923002220003"] = "Not a WhatsApp number";
        var later = DateTime.UtcNow.AddHours(2);
        Assert.Equal(2, await campaigns.SendDueAsync(sender, 2, later));
        Assert.Equal(1, await campaigns.SendDueAsync(sender, 2, later));
        Assert.Single(sender.Sent);
        var sent = sender.Sent[0];
        Assert.Equal("+923002220001", sent.Message.To);
        Assert.Equal(template.Name, sent.Message.TemplateName);
        Assert.Equal(["Yes", "0", "Store " + session.StationNumber], sent.Message.Parameters);

        await using (var ctx = db.CreateContext())
        {
            var row = await ctx.Campaigns.SingleAsync(c => c.Id == campaign.Id);
            Assert.Equal(CampaignStatus.Sent, row.Status);
            var messages = await ctx.WhatsAppMessages.Where(m => m.CampaignId == campaign.Id).ToListAsync();
            Assert.DoesNotContain(messages, m => m.ClientId == no.Id);
            Assert.Equal(WhatsAppMessageStatus.Error, messages.Single(m => m.ClientId == yes2.Id).Status);
            Assert.Contains("Not a WhatsApp number", messages.Single(m => m.ClientId == yes3.Id).Error);
            Assert.Equal("Hi Yes, you have 0 points at Store " + session.StationNumber + ".", messages.Single(m => m.ClientId == yes1.Id).Body);
        }

        // WhatsApp's statuses: delivered, read, and a late "sent" that changes nothing.
        Assert.True(await campaigns.ApplyStatusAsync(sent.Id, "delivered", DateTime.UtcNow));
        Assert.True(await campaigns.ApplyStatusAsync(sent.Id, "read", DateTime.UtcNow));
        Assert.True(await campaigns.ApplyStatusAsync(sent.Id, "sent", DateTime.UtcNow));
        Assert.True(await campaigns.ApplyStatusAsync(sent.Id, "failed", DateTime.UtcNow, "131026 Undeliverable"));
        Assert.False(await campaigns.ApplyStatusAsync("wamid.unknown", "read", DateTime.UtcNow));
        var counts = (await campaigns.CountsAsync([campaign.Id]))[campaign.Id];
        Assert.Equal(new CampaignCounts(0, 0, 0, 1, 0, 2), counts);

        // Results: the recipients' sales after the campaign started.
        await SellAsync(data, session, yes1, item, 4);
        var results = await campaigns.ResultsAsync(campaign.Id, 7);
        Assert.Equal(3, results.Recipients);
        Assert.Equal(1, results.Buyers);
        Assert.Equal(200m, results.SalesTotal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => campaigns.DeleteCampaignAsync(campaign.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => campaigns.DeleteTemplateAsync(template.Id));
    }

    [SkippableFact]
    public async Task A_single_message_needs_consent_and_a_cancelled_campaign_sends_nothing()
    {
        var (data, session, storeId) = await StoreAsync();
        var yes = await ClientAsync(data, "Single", "+923003330001", optIn: true);
        var no = await ClientAsync(data, "Nope", "+923003330002");
        var template = await TemplateAsync(data);
        var campaigns = new CampaignService(data);
        var sender = new RecordingWhatsAppSender();
        await Assert.ThrowsAsync<BusinessRuleException>(() => campaigns.SendSingleAsync(no.Id, template.Id, sender));
        var message = await campaigns.SendSingleAsync(yes.Id, template.Id, sender);
        Assert.Equal(WhatsAppMessageStatus.Sent, message.Status);
        Assert.Null(message.CampaignId);
        Assert.Equal(storeId, message.StoreId);
        Assert.Single(sender.Sent);

        var item = await ItemAsync(data, session, "CNC-" + session.StationNumber, 5m);
        await SellAsync(data, session, yes, item, 1);
        var segment = await new SegmentService(data).SaveAsync(new CustomerSegment { Name = "One" }, new SegmentRules { StoreId = storeId, MinVisits = 1 });
        var campaign = await campaigns.SaveCampaignAsync(new Campaign { Name = "Cancel me", SegmentId = segment.Id, TemplateId = template.Id });
        await campaigns.ScheduleAsync(campaign.Id, campaign.Version, null, Today);
        await using (var ctx = db.CreateContext())
            campaign = await ctx.Campaigns.AsNoTracking().SingleAsync(c => c.Id == campaign.Id);
        await campaigns.CancelAsync(campaign.Id, campaign.Version);
        Assert.Equal(0, await campaigns.SendDueAsync(sender, 10, DateTime.UtcNow.AddMinutes(1)));
        Assert.Single(sender.Sent);
        await campaigns.DeleteCampaignAsync(campaign.Id);
    }

    // Promotions on sales ---------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_sale_records_its_promotions_and_coupons_and_a_void_gives_the_coupon_back()
    {
        var (data, session, storeId) = await StoreAsync();
        var item = await ItemAsync(data, session, "PRM-" + session.StationNumber, 100m);
        var promotions = new PromotionService(data);
        await Assert.ThrowsAsync<BusinessRuleException>(() => promotions.SaveAsync(new Promotion { Name = "No percent", Kind = PromotionKind.PercentOff }));
        var tenOff = await promotions.SaveAsync(new Promotion
        {
            Name = "Ten off " + session.StationNumber, Kind = PromotionKind.PercentOff, Percent = 10, Scope = PromotionScope.Items, Combinable = true,
            AllStores = false, Stores = [new PromotionStore { StoreId = storeId }], Targets = [new PromotionTarget { Plu = item.Plu.ToLowerInvariant() }],
        });
        Assert.Equal(item.Plu, tenOff.Targets.Single().Plu);
        var code = "SAVE" + session.StationNumber;
        var coupon = await promotions.SaveAsync(new Promotion
        {
            Name = "Coupon " + session.StationNumber, Kind = PromotionKind.SpendOver, SpendThreshold = 100, Amount = 20, Combinable = true,
            CouponCode = code.ToLowerInvariant(), CouponMaxUses = 1,
        });
        Assert.Equal(code, coupon.CouponCode);

        var draft = new SaleDraft { Lines = [Line(item, 2)], CouponCodes = [code] };
        var outcome = await promotions.ApplyAsync(draft);
        Assert.Equal(20m, draft.Lines[0].PromotionDiscount);
        Assert.Equal(20m, -draft.Lines.Single(l => l.IsPromotionLine).LineTotal);
        Assert.Equal(40m, outcome.Total);
        var sale = await SellAsync(data, session, draft);
        Assert.Equal(160m, sale.Total);

        await using (var ctx = db.CreateContext())
        {
            var lines = await ctx.SaleLines.Where(l => l.SaleId == sale.Id).OrderBy(l => l.LineNumber).ToListAsync();
            Assert.Equal(tenOff.Id, lines[0].PromotionId);
            Assert.Equal(20m, lines[0].PromotionDiscount);
            Assert.Equal(20m, lines[0].DiscountAmount);
            Assert.Equal(180m, lines[0].LineTotal);
            Assert.Equal(SaleLineKind.Coupon, lines[1].Kind);
            Assert.Equal(coupon.Id, lines[1].PromotionId);
            Assert.Equal(code, (await ctx.CouponUses.SingleAsync(u => u.SaleId == sale.Id)).Code);
        }
        // The one use is gone: the engine leaves the coupon out, and a draft that still has it is refused.
        var again = new SaleDraft { Lines = [Line(item, 2)], CouponCodes = [code] };
        var second = await promotions.ApplyAsync(again);
        Assert.Contains("used up", second.Notes.Single());
        var forced = new SaleDraft { Lines = [Line(item, 2)], CouponCodes = [code] };
        PromotionEngine.Apply(forced, [coupon], new PromotionContext(storeId, DateTime.Now, [code]));
        await Assert.ThrowsAsync<BusinessRuleException>(() => SellAsync(data, session, forced));

        var report = await promotions.ReportAsync(Today, Today, [storeId]);
        var tenRow = report.Single(r => r.PromotionId == tenOff.Id);
        Assert.Equal(20m, tenRow.Discount);
        Assert.Equal(180m, tenRow.NetSales);
        Assert.Equal(2m, tenRow.Units);
        var couponRow = report.Single(r => r.PromotionId == coupon.Id);
        Assert.Equal(20m, couponRow.Discount);
        Assert.Equal(1, couponRow.CouponUses);
        await Assert.ThrowsAsync<BusinessRuleException>(() => promotions.DeleteAsync(tenOff.Id));

        await new SalesService(data).VoidSaleAsync(sale.Id, session);
        Assert.Equal(0, (await promotions.CouponUsesAsync([coupon.Id])).GetValueOrDefault(coupon.Id));
        var afterVoid = new SaleDraft { Lines = [Line(item, 2)], CouponCodes = [code] };
        Assert.Equal(40m, (await promotions.ApplyAsync(afterVoid)).Total);
    }

    [SkippableFact]
    public async Task A_promotion_keeps_its_rows_when_saved_again_and_a_stale_version_is_a_conflict()
    {
        var (data, _, storeId) = await StoreAsync();
        var promotions = new PromotionService(data);
        var p = await promotions.SaveAsync(new Promotion
        {
            Name = "Bundle", Kind = PromotionKind.BundlePrice, BundlePrice = 99, AllStores = false, Stores = [new PromotionStore { StoreId = storeId }],
            Targets = [new PromotionTarget { Role = PromotionTargetRole.BundlePart, Plu = "A" }, new PromotionTarget { Role = PromotionTargetRole.BundlePart, Plu = "B" }],
        });
        var firstTarget = p.Targets.Single(t => t.Plu == "A").Id;
        var stale = p.Version;
        var edit = (await promotions.GetAsync(p.Id))!;
        edit.Targets = [new PromotionTarget { Role = PromotionTargetRole.BundlePart, Plu = "A", Quantity = 2 }, new PromotionTarget { Role = PromotionTargetRole.BundlePart, Plu = "C" }];
        edit.Stores = [new PromotionStore { StoreId = storeId }];
        var saved = await promotions.SaveAsync(edit);
        Assert.Equal(firstTarget, saved.Targets.Single(t => t.Plu == "A").Id);
        Assert.Equal(2m, saved.Targets.Single(t => t.Plu == "A").Quantity);
        Assert.Equal(["A", "C"], saved.Targets.Select(t => t.Plu).Order());
        edit.Version = stale;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => promotions.SaveAsync(edit));
        Assert.Contains((await promotions.ForStoreAsync(storeId, Today)), x => x.Id == p.Id);
        Assert.DoesNotContain((await promotions.ForStoreAsync(Guid.NewGuid(), Today)), x => x.Id == p.Id);
        await promotions.DeleteAsync(p.Id);
        await using var ctx = db.CreateContext();
        Assert.False(await ctx.PromotionTargets.AnyAsync(t => t.PromotionId == p.Id));
        Assert.True(await ctx.Tombstones.AnyAsync(t => t.RowId == p.Id && t.EntityName == nameof(Promotion)));
    }

    // Targets and consent ---------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Targets_are_one_per_store_or_salesperson_and_month_against_net_sales()
    {
        var (data, session, storeId) = await StoreAsync();
        var seller = Guid.NewGuid();
        await using (var ctx = db.CreateContext())
        {
            ctx.UserAccounts.Add(new UserAccount { Id = seller, UserName = "seller-" + session.StationNumber, IsActive = true });
            await ctx.SaveChangesAsync();
        }
        var item = await ItemAsync(data, session, "TGT-" + session.StationNumber, 250m);
        var draft = new SaleDraft { Lines = [Line(item, 2)], Staff = "seller", StaffId = seller };
        await SellAsync(data, session, draft);
        await SellAsync(data, session, null, item, 1);
        var targets = new TargetService(data);
        var now = DateTime.Today;
        var store = await targets.SaveAsync(new SalesTarget { Year = now.Year, Month = now.Month, Amount = 1000 });
        Assert.Equal(storeId, store.StoreId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => targets.SaveAsync(new SalesTarget { Year = now.Year, Month = now.Month, Amount = 5 }));
        var person = await targets.SaveAsync(new SalesTarget { UserId = seller, Year = now.Year, Month = now.Month, Amount = 400 });
        var actuals = await targets.ActualsAsync(now.Year, now.Month, [storeId]);
        Assert.Equal(750m, actuals.ByStore[storeId]);
        Assert.Equal(500m, actuals.ByPerson[(storeId, seller)]);
        Assert.Equal(125m, TargetService.Percent(500, person.Amount));
        Assert.Equal([store.Id, person.Id], (await targets.ListAsync(now.Year, now.Month, [storeId])).Select(t => t.Id));
        var changed = await targets.SaveAsync(new SalesTarget { Id = store.Id, Version = store.Version, Amount = 1500 });
        Assert.Equal(1500m, changed.Amount);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => targets.SaveAsync(new SalesTarget { Id = store.Id, Version = store.Version, Amount = 1 }));
    }

    [SkippableFact]
    public async Task Consent_needs_a_number_keeps_its_time_and_is_cleared_when_taken_back()
    {
        var (data, _, _) = await StoreAsync();
        var parties = new PartyService(data);
        await Assert.ThrowsAsync<BusinessRuleException>(() => parties.SaveClientAsync(new Client { FirstName = "No number", WhatsAppOptIn = true }));
        var client = await ClientAsync(data, "Consent", "0092 300 4440001", optIn: true);
        Assert.Equal("+923004440001", client.WhatsAppNumber);
        var given = client.WhatsAppOptInAt;
        Assert.NotNull(given);
        client.City = "Lahore";
        client = await parties.SaveClientAsync(client);
        Assert.Equal(given, client.WhatsAppOptInAt);
        client.WhatsAppOptIn = false;
        client = await parties.SaveClientAsync(client);
        Assert.Null((await parties.GetClientAsync(client.Id))!.WhatsAppOptInAt);
        // Taking consent back is the client's answer: the till does not ask them again.
        Assert.NotNull(client.WhatsAppAskedAt);
    }

    [SkippableFact]
    public async Task The_tills_answer_about_WhatsApp_is_kept_audited_and_followed_by_campaigns()
    {
        var (data, session, storeId) = await StoreAsync();
        var parties = new PartyService(data);
        var yes = await ClientAsync(data, "Till yes");
        var no = await ClientAsync(data, "Till no", "+1 555 555 0102");
        Assert.Null(yes.WhatsAppAskedAt);
        // Yes needs a number in international form.
        await Assert.ThrowsAsync<BusinessRuleException>(() => parties.RecordWhatsAppAnswerAsync(yes.Id, true));
        await Assert.ThrowsAsync<BusinessRuleException>(() => parties.RecordWhatsAppAnswerAsync(yes.Id, true, "0300 1234567"));
        Assert.Null((await parties.GetClientAsync(yes.Id))!.WhatsAppAskedAt);

        var agreed = await parties.RecordWhatsAppAnswerAsync(yes.Id, true, "+1 (555) 555-0101");
        Assert.Equal(("+15555550101", true), (agreed.WhatsAppNumber, agreed.WhatsAppOptIn));
        Assert.NotNull(agreed.WhatsAppOptInAt);
        Assert.NotNull(agreed.WhatsAppAskedAt);
        var declined = await parties.RecordWhatsAppAnswerAsync(no.Id, false);
        Assert.Equal(("+15555550102", false), (declined.WhatsAppNumber, declined.WhatsAppOptIn));
        Assert.Null(declined.WhatsAppOptInAt);
        Assert.NotNull(declined.WhatsAppAskedAt);
        await using (var ctx = db.CreateContext())
        {
            Assert.True(await ctx.AuditEntries.AnyAsync(a => a.EntityId == yes.Id && a.Action == AuditActions.WhatsAppConsent));
            Assert.True(await ctx.AuditEntries.AnyAsync(a => a.EntityId == no.Id && a.Action == AuditActions.WhatsAppConsent));
        }

        // A campaign to everyone who bought here goes to the client who said yes only.
        var item = await ItemAsync(data, session, "WAT-" + session.StationNumber, 10m);
        await SellAsync(data, session, yes, item, 1);
        await SellAsync(data, session, no, item, 1);
        var segment = await new SegmentService(data).SaveAsync(new CustomerSegment { Name = "Till answers" }, new SegmentRules { StoreId = storeId, MinVisits = 1 });
        var campaigns = new CampaignService(data);
        var campaign = await campaigns.SaveCampaignAsync(new Campaign { Name = "Till answers", SegmentId = segment.Id, TemplateId = (await TemplateAsync(data)).Id });
        Assert.Equal(1, await campaigns.ScheduleAsync(campaign.Id, campaign.Version, null, Today));
        var sender = new RecordingWhatsAppSender();
        await campaigns.SendDueAsync(sender, 10, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(["+15555550101"], sender.Sent.Select(s => s.Message.To));
    }

    [SkippableFact]
    public async Task Promotions_reach_the_books_as_discounts_with_the_daily_posting()
    {
        var (data, session, storeId) = await StoreAsync();
        var item = await ItemAsync(data, session, "BKS-" + session.StationNumber, 50m, "E4BOOKS");
        var promotions = new PromotionService(data);
        List<PromotionStore> Here() => [new PromotionStore { StoreId = storeId }];
        await promotions.SaveAsync(new Promotion
        {
            Name = "Books 20% " + session.StationNumber, Kind = PromotionKind.PercentOff, Percent = 20, Scope = PromotionScope.Items, Combinable = true,
            AllStores = false, Stores = Here(), Targets = [new PromotionTarget { Plu = item.Plu }],
        });
        await promotions.SaveAsync(new Promotion
        {
            Name = "Books spend " + session.StationNumber, Kind = PromotionKind.SpendOver, SpendThreshold = 50, Amount = 5, Combinable = true,
            AllStores = false, Stores = Here(),
        });
        var draft = new SaleDraft { Lines = [Line(item, 2)] };
        Assert.Equal(25m, (await promotions.ApplyAsync(draft)).Total);
        var sale = await SellAsync(data, session, draft);
        Assert.Equal(75m, sale.Total);

        // The department's sales are counted before the promotions, and what they took off is a discount.
        var export = await new AccountingService(data).ExportAsync(Today, Today);
        var day = export.Lines.Where(l => l.Store == "E" + session.StationNumber).ToList();
        Assert.Equal(day.Sum(l => l.Debit), day.Sum(l => l.Credit));
        Assert.Equal(25m, day.Where(l => l.Memo == "Promotions").Sum(l => l.Debit - l.Credit));
        Assert.Equal(100m, day.Where(l => l.Memo == "Sales E4BOOKS").Sum(l => l.Credit - l.Debit));

        // The ledger's daily posting takes it from there: one balanced entry with the discount on the discounts account.
        var ledger = new LedgerService(data);
        await ledger.SeedAsync();
        Assert.True(await ledger.PostDailyAsync(Today, Today, storeId) >= 1);
        await using var ctx = db.CreateContext();
        var entry = await ctx.JournalEntries.AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == $"daily:{storeId:N}:{Today:yyyy-MM-dd}");
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        var discounts = await ctx.AccountMappings.AsNoTracking().SingleAsync(m => m.StoreId == storeId && m.Kind == AccountMappingKind.Discounts);
        Assert.Equal(25m, entry.Lines.Where(l => l.Account!.Code == discounts.AccountCode).Sum(l => l.Debit - l.Credit));
    }
}

public sealed class CrmAndMarketingTestsOnSqlite(SqliteFixture db) : CrmAndMarketingTests(db), IClassFixture<SqliteFixture>;

public sealed class CrmAndMarketingTestsOnSqlServer(SqlServerFixture db) : CrmAndMarketingTests(db), IClassFixture<SqlServerFixture>;
