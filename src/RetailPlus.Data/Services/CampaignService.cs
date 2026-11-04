using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>How many of a campaign's messages stand where.</summary>
public sealed record CampaignCounts(int Queued, int Sent, int Delivered, int Read, int Failed, int Error)
{
    public static readonly CampaignCounts None = new(0, 0, 0, 0, 0, 0);

    public int Total => Queued + Sent + Delivered + Read + Failed + Error;
}

/// <summary>A campaign's results: its messages, and what its recipients bought within some days of it.</summary>
public sealed record CampaignResults(CampaignCounts Counts, int Recipients, int Days, int Buyers, int Sales, decimal SalesTotal);

/// <summary>
/// WhatsApp templates, campaigns and messages. A campaign's recipients are worked out when it is scheduled (the members
/// of its segment who agreed to WhatsApp messages and have a number), one queued message each; the server's sender sends
/// what is due a few at a time, and WhatsApp's status notifications move each message on to delivered and read. Consent
/// is checked again just before each message goes.
/// </summary>
public sealed class CampaignService(DataContext data)
{
    /// <summary>One sender at a time in this process, so a message is never sent twice.</summary>
    private static readonly SemaphoreSlim Sending = new(1, 1);

    // Templates -------------------------------------------------------------------------------------------------------

    public async Task<WhatsAppTemplate> SaveTemplateAsync(WhatsAppTemplate t)
    {
        t.Name = (t.Name ?? "").Trim();
        t.Language = (t.Language ?? "").Trim();
        t.Body = (t.Body ?? "").Trim();
        if (WhatsAppTemplates.Problem(t.Name, t.Language, t.Body) is { } problem) throw new BusinessRuleException(problem);
        await using var db = data.Create();
        if (await db.WhatsAppTemplates.AnyAsync(x => x.Name == t.Name && x.Language == t.Language && x.Id != t.Id))
            throw new BusinessRuleException($"There is already a template {t.Name} in {t.Language}.");
        if (t.Id == Guid.Empty) db.WhatsAppTemplates.Add(t); else db.WhatsAppTemplates.Update(t);
        await SaveAsync(db, "template");
        return t;
    }

    public async Task DeleteTemplateAsync(Guid id)
    {
        await using var db = data.Create();
        var t = await db.WhatsAppTemplates.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That template no longer exists.");
        if (await db.Campaigns.AnyAsync(c => c.TemplateId == id) || await db.WhatsAppMessages.AnyAsync(m => m.TemplateId == id))
            throw new BusinessRuleException("Messages were sent with this template, so it is kept. Switch it off instead.");
        db.WhatsAppTemplates.Remove(t);
        await db.SaveChangesAsync();
    }

    // Campaigns -------------------------------------------------------------------------------------------------------

    /// <summary>Adds a draft campaign or changes one that is still a draft.</summary>
    public async Task<Campaign> SaveCampaignAsync(Campaign c)
    {
        c.Name = (c.Name ?? "").Trim();
        if (c.Name.Length == 0) throw new BusinessRuleException("Give the campaign a name.");
        await using var db = data.Create();
        if (!await db.CustomerSegments.AnyAsync(s => s.Id == c.SegmentId)) throw new BusinessRuleException("Choose the segment the campaign goes to.");
        if (!await db.WhatsAppTemplates.AnyAsync(t => t.Id == c.TemplateId && t.IsActive)) throw new BusinessRuleException("Choose a template that is switched on.");
        if (c.Id == Guid.Empty)
        {
            c.Status = CampaignStatus.Draft;
            c.CreatedBy ??= data.CurrentUser?.UserName;
            c.CreatedById ??= data.CurrentUser?.UserId;
            db.Campaigns.Add(c);
        }
        else
        {
            var stored = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.Id) ?? throw new BusinessRuleException("That campaign no longer exists.");
            if (stored.Status != CampaignStatus.Draft) throw new BusinessRuleException("Only a draft campaign can be changed.");
            c.Status = stored.Status;
            c.StoreId = stored.StoreId;
            c.CreatedBy = stored.CreatedBy;
            c.CreatedById = stored.CreatedById;
            db.Campaigns.Update(c);
        }
        await SaveAsync(db, "campaign");
        return c;
    }

    /// <summary>Deletes a draft, or a cancelled campaign that sent nothing (with its queued messages).</summary>
    public async Task DeleteCampaignAsync(Guid id)
    {
        await using var db = data.Create();
        var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That campaign no longer exists.");
        bool sentSome = await db.WhatsAppMessages.AnyAsync(m => m.CampaignId == id && m.Status != WhatsAppMessageStatus.Queued);
        if (c.Status is not (CampaignStatus.Draft or CampaignStatus.Cancelled) || sentSome)
            throw new BusinessRuleException("A campaign that sent messages is kept with its results.");
        db.WhatsAppMessages.RemoveRange(await db.WhatsAppMessages.Where(m => m.CampaignId == id).ToListAsync());
        db.Campaigns.Remove(c);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Schedules a draft campaign for <paramref name="sendAtUtc"/> (now when null): its recipients are the segment's
    /// members, as they are now, who agreed to WhatsApp messages and have a number; each gets a queued message with the
    /// template filled in for them. Returns how many.
    /// </summary>
    public async Task<int> ScheduleAsync(Guid id, Guid version, DateTime? sendAtUtc, DateOnly today)
    {
        Campaign campaign;
        CustomerSegment segment;
        WhatsAppTemplate template;
        string storeName;
        await using (var db = data.Create())
        {
            campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That campaign no longer exists.");
            if (campaign.Version != version) throw new ConcurrencyConflictException("Someone else changed this campaign. Load it again and make your changes again.");
            if (campaign.Status != CampaignStatus.Draft) throw new BusinessRuleException("This campaign is already scheduled or sent.");
            segment = await db.CustomerSegments.AsNoTracking().SingleAsync(s => s.Id == campaign.SegmentId);
            template = await db.WhatsAppTemplates.AsNoTracking().SingleAsync(t => t.Id == campaign.TemplateId);
            if (!template.IsActive) throw new BusinessRuleException("The campaign's template is switched off.");
            storeName = await db.Stores.AsNoTracking().Where(s => s.Id == campaign.StoreId).Select(s => s.Name).SingleOrDefaultAsync() ?? "";
        }
        var rules = SegmentService.Rules(segment) with { WhatsAppConsent = true };
        var members = await new SegmentService(data).EvaluateAsync(rules, today);
        if (members.Count == 0) throw new BusinessRuleException("Nobody in the segment agreed to WhatsApp messages and has a number, so there is no one to send to.");

        await using var write = data.Create();
        await using var tx = await write.Database.BeginTransactionAsync();
        var row = await write.Campaigns.SingleAsync(x => x.Id == id);
        if (row.Version != version || row.Status != CampaignStatus.Draft) throw new ConcurrencyConflictException("Someone else changed this campaign. Load it again and make your changes again.");
        var ids = members.Select(m => m.ClientId).ToList();
        var clients = new List<Client>();
        foreach (var chunk in ids.Chunk(1000))
            clients.AddRange(await write.Clients.AsNoTracking().Where(c => chunk.Contains(c.Id)).ToListAsync());
        foreach (var client in clients)
            write.WhatsAppMessages.Add(Message(client, template, storeName, row.StoreId, row.Id));
        row.Status = CampaignStatus.Scheduled;
        row.ScheduledAtUtc = sendAtUtc ?? DateTime.UtcNow;
        write.Audit(AuditActions.CampaignScheduled, $"{row.Name}: {clients.Count} recipients, from {row.ScheduledAtUtc:yyyy-MM-dd HH:mm} UTC", nameof(Campaign), row.Id);
        await write.SaveChangesAsync();
        await tx.CommitAsync();
        return clients.Count;
    }

    private WhatsAppMessage Message(Client client, WhatsAppTemplate template, string storeName, Guid storeId, Guid? campaignId)
    {
        var values = new TemplateValues(client.FirstName, client.LastName, client.Company, client.Points, client.Balance, storeName, client.Number);
        return new WhatsAppMessage
        {
            StoreId = storeId, CampaignId = campaignId, ClientId = client.Id, TemplateId = template.Id, ToNumber = client.WhatsAppNumber ?? "",
            ParametersJson = JsonSerializer.Serialize(WhatsAppTemplates.Parameters(template.Body, values)), Body = Cut(WhatsAppTemplates.Render(template.Body, values), 2000)!,
            Status = WhatsAppMessageStatus.Queued, SentBy = data.CurrentUser?.UserName,
        };
    }

    /// <summary>Cancels a scheduled or sending campaign (or a draft): its queued messages are never sent.</summary>
    public async Task CancelAsync(Guid id, Guid version)
    {
        await using var db = data.Create();
        var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That campaign no longer exists.");
        if (c.Version != version) throw new ConcurrencyConflictException("Someone else changed this campaign. Load it again and make your changes again.");
        if (c.Status is CampaignStatus.Sent or CampaignStatus.Cancelled) throw new BusinessRuleException("This campaign is already finished.");
        c.Status = CampaignStatus.Cancelled;
        c.FinishedAtUtc = DateTime.UtcNow;
        db.Audit(AuditActions.CampaignCancelled, c.Name, nameof(Campaign), c.Id);
        await SaveAsync(db, "campaign");
    }

    /// <summary>The message counts of campaigns, by campaign.</summary>
    public async Task<Dictionary<Guid, CampaignCounts>> CountsAsync(IReadOnlyCollection<Guid> campaignIds)
    {
        if (campaignIds.Count == 0) return [];
        await using var db = data.Create();
        var rows = await db.WhatsAppMessages.AsNoTracking().Where(m => m.CampaignId != null && campaignIds.Contains(m.CampaignId.Value))
            .GroupBy(m => new { Campaign = m.CampaignId!.Value, m.Status }).Select(g => new { g.Key.Campaign, g.Key.Status, Count = g.Count() }).ToListAsync();
        return rows.GroupBy(r => r.Campaign).ToDictionary(g => g.Key, g => Counts(g.ToDictionary(r => r.Status, r => r.Count)));
    }

    private static CampaignCounts Counts(IReadOnlyDictionary<WhatsAppMessageStatus, int> n) => new(n.GetValueOrDefault(WhatsAppMessageStatus.Queued),
        n.GetValueOrDefault(WhatsAppMessageStatus.Sent), n.GetValueOrDefault(WhatsAppMessageStatus.Delivered), n.GetValueOrDefault(WhatsAppMessageStatus.Read),
        n.GetValueOrDefault(WhatsAppMessageStatus.Failed), n.GetValueOrDefault(WhatsAppMessageStatus.Error));

    /// <summary>
    /// The campaign's messages by status, and the completed sales its recipients made from the campaign's start (in the
    /// store's time) to <paramref name="days"/> days after.
    /// </summary>
    public async Task<CampaignResults> ResultsAsync(Guid id, int days)
    {
        days = Math.Clamp(days, 1, 90);
        await using var db = data.Create();
        var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("That campaign no longer exists.");
        var counts = (await CountsAsync([id])).GetValueOrDefault(id) ?? CampaignCounts.None;
        var recipients = await db.WhatsAppMessages.AsNoTracking().Where(m => m.CampaignId == id).Select(m => m.ClientId).Distinct().ToListAsync();
        if (c.StartedAtUtc is not { } started || recipients.Count == 0) return new CampaignResults(counts, recipients.Count, days, 0, 0, 0);
        var zoneId = await db.Stores.AsNoTracking().Where(s => s.Id == c.StoreId).Select(s => s.TimeZoneId).SingleOrDefaultAsync();
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var z) ? z : TimeZoneInfo.Local;
        // From the minute sending started (sale times are kept to the second).
        var from = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(started, DateTimeKind.Utc), zone);
        from = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0);
        var to = from.AddDays(days);
        var sales = new List<(Guid Client, decimal Total)>();
        foreach (var chunk in recipients.Chunk(1000))
            sales.AddRange((await db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Completed && s.ClientId != null && chunk.Contains(s.ClientId.Value) && s.SoldAt >= from && s.SoldAt < to)
                .Select(s => new { Client = s.ClientId!.Value, s.Total }).ToListAsync()).Select(s => (s.Client, s.Total)));
        return new CampaignResults(counts, recipients.Count, days, sales.Select(s => s.Client).Distinct().Count(), sales.Count, sales.Sum(s => s.Total));
    }

    // Sending ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sends up to <paramref name="max"/> queued messages of the campaigns that are due, oldest first: scheduled campaigns
    /// whose time has come start sending, and those with nothing left to send are marked sent. Returns how many messages
    /// were handled (sent or refused).
    /// </summary>
    public async Task<int> SendDueAsync(IWhatsAppSender sender, int max, DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        await Sending.WaitAsync(cancellationToken);
        try
        {
            var now = nowUtc ?? DateTime.UtcNow;
            await using var db = data.Create();
            foreach (var due in await db.Campaigns.Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAtUtc <= now).ToListAsync(cancellationToken))
            {
                due.Status = CampaignStatus.Sending;
                due.StartedAtUtc ??= DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            var sending = db.Campaigns.Where(c => c.Status == CampaignStatus.Sending).Select(c => c.Id);
            var queued = await db.WhatsAppMessages.Where(m => m.Status == WhatsAppMessageStatus.Queued && m.CampaignId != null && sending.Contains(m.CampaignId.Value))
                .OrderBy(m => m.CreatedAtUtc).Take(Math.Max(0, max)).ToListAsync(cancellationToken);
            foreach (var message in queued) await SendAsync(db, message, sender, cancellationToken);
            foreach (var done in await db.Campaigns.Where(c => c.Status == CampaignStatus.Sending
                         && !db.WhatsAppMessages.Any(m => m.CampaignId == c.Id && m.Status == WhatsAppMessageStatus.Queued)).ToListAsync(cancellationToken))
            {
                done.Status = CampaignStatus.Sent;
                done.FinishedAtUtc = DateTime.UtcNow;
                int count = await db.WhatsAppMessages.CountAsync(m => m.CampaignId == done.Id && m.Status != WhatsAppMessageStatus.Error, cancellationToken);
                AuditCampaign(db, done, AuditActions.CampaignSent, $"{done.Name}: {count} messages sent");
            }
            await db.SaveChangesAsync(cancellationToken);
            return queued.Count;
        }
        finally
        {
            Sending.Release();
        }
    }

    /// <summary>
    /// Sends one queued message, saved at once: consent and the number are checked again on the client as it is now, and
    /// what WhatsApp answers (its message id, or why not) is kept on the message.
    /// </summary>
    private static async Task SendAsync(RetailPlusDbContext db, WhatsAppMessage message, IWhatsAppSender sender, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == message.ClientId).Select(c => new { c.WhatsAppOptIn, c.WhatsAppNumber, c.IsDeleted })
            .SingleOrDefaultAsync(cancellationToken);
        var template = await db.WhatsAppTemplates.AsNoTracking().SingleAsync(t => t.Id == message.TemplateId, cancellationToken);
        if (client is null || client.IsDeleted || !client.WhatsAppOptIn || string.IsNullOrWhiteSpace(client.WhatsAppNumber))
            message.Error = "The client does not agree to WhatsApp messages (any more), so it was not sent.";
        else
        {
            message.ToNumber = client.WhatsAppNumber;
            try
            {
                var parameters = JsonSerializer.Deserialize<List<string>>(message.ParametersJson) ?? [];
                var result = await sender.SendAsync(new WhatsAppOutgoing(message.ToNumber, template.Name, template.Language, parameters), cancellationToken);
                if (result.Ok)
                {
                    message.Status = WhatsAppMessageStatus.Sent;
                    message.ProviderMessageId = result.MessageId;
                    message.SentAtUtc = DateTime.UtcNow;
                }
                else message.Error = Cut(result.Error ?? "WhatsApp refused the message.", 400);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Error = Cut("The message could not be sent: " + ex.Message, 400);
            }
        }
        if (message.Status == WhatsAppMessageStatus.Queued) message.Status = WhatsAppMessageStatus.Error;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sends one template message to a client at once (Send on WhatsApp on the client's page). Only to a client who agreed
    /// and has a number; the message is kept whatever WhatsApp answers.
    /// </summary>
    public async Task<WhatsAppMessage> SendSingleAsync(Guid clientId, Guid templateId, IWhatsAppSender sender, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new BusinessRuleException("That client no longer exists.");
        if (!client.WhatsAppOptIn || string.IsNullOrWhiteSpace(client.WhatsAppNumber))
            throw new BusinessRuleException("The client has not agreed to WhatsApp messages, or has no WhatsApp number.");
        var template = await db.WhatsAppTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId && t.IsActive, cancellationToken)
            ?? throw new BusinessRuleException("Choose a template that is switched on.");
        var storeId = db.Station?.StoreId ?? throw new InvalidOperationException("A message needs the store it is sent for.");
        var storeName = await db.Stores.AsNoTracking().Where(s => s.Id == storeId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken) ?? "";
        var message = Message(client, template, storeName, storeId, null);
        db.WhatsAppMessages.Add(message);
        db.Audit(AuditActions.WhatsAppMessage, $"{template.Name} to {client.WhatsAppNumber}", nameof(Client), client.Id);
        await db.SaveChangesAsync(cancellationToken);
        await Sending.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(db, message, sender, cancellationToken);
        }
        finally
        {
            Sending.Release();
        }
        return message;
    }

    /// <summary>
    /// A status WhatsApp reported for a message (its webhook): sent, delivered, read or failed. Statuses only move forward;
    /// a failure is kept with its reason. Returns false when no message has that id.
    /// </summary>
    public async Task<bool> ApplyStatusAsync(string providerMessageId, string status, DateTime atUtc, string? error = null)
    {
        var next = status.ToLowerInvariant() switch
        {
            "sent" => WhatsAppMessageStatus.Sent,
            "delivered" => WhatsAppMessageStatus.Delivered,
            "read" => WhatsAppMessageStatus.Read,
            "failed" => WhatsAppMessageStatus.Failed,
            _ => (WhatsAppMessageStatus?)null,
        };
        await using var db = data.Create();
        var message = await db.WhatsAppMessages.SingleOrDefaultAsync(m => m.ProviderMessageId == providerMessageId);
        if (message is null) return false;
        if (next is not { } s) return true;
        int Rank(WhatsAppMessageStatus x) => x switch
        {
            WhatsAppMessageStatus.Queued => 0, WhatsAppMessageStatus.Sent => 1, WhatsAppMessageStatus.Delivered => 2, WhatsAppMessageStatus.Read => 3, _ => 4,
        };
        if (s == WhatsAppMessageStatus.Failed)
        {
            if (Rank(message.Status) <= Rank(WhatsAppMessageStatus.Sent))
            {
                message.Status = WhatsAppMessageStatus.Failed;
                message.FailedAtUtc = atUtc;
                message.Error = Cut(error ?? "WhatsApp could not deliver the message.", 400);
            }
        }
        else if (Rank(s) > Rank(message.Status) && message.Status is not (WhatsAppMessageStatus.Failed or WhatsAppMessageStatus.Error))
        {
            message.Status = s;
            if (s == WhatsAppMessageStatus.Sent) message.SentAtUtc ??= atUtc;
            if (s >= WhatsAppMessageStatus.Delivered) message.DeliveredAtUtc ??= atUtc;
            if (s == WhatsAppMessageStatus.Read) message.ReadAtUtc ??= atUtc;
        }
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// An audit line for a campaign under its store: the sender in the background works for no store, so it names itself.
    /// </summary>
    private static void AuditCampaign(RetailPlusDbContext db, Campaign c, string action, string details)
    {
        if (db.Station is not null)
        {
            db.Audit(action, details, nameof(Campaign), c.Id);
            return;
        }
        db.AuditEntries.Add(new AuditEntry
        {
            StoreId = c.StoreId, AtUtc = SyncColumns.Now(), UserName = "WhatsApp sender", Action = action, EntityName = nameof(Campaign), EntityId = c.Id,
            Details = Cut(details, 400),
        });
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

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
