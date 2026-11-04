namespace RetailPlus.Core.Model;

/// <summary>
/// A saved group of clients worked out from rules (spend, visits, lapsed, store, department, birthday month, points,
/// balance, WhatsApp consent; see <c>Rules.SegmentRules</c>). The members are worked out again whenever they are needed.
/// </summary>
public class CustomerSegment : Entity, IVersioned, ILocalOnly
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>The rules, as the JSON of <c>Rules.SegmentRules</c>.</summary>
    public string RulesJson { get; set; } = "{}";
    /// <summary>The store the rules name (its sales only); null for the whole company.</summary>
    public Guid? StoreId { get; set; }
    public int LastCount { get; set; }
    public DateTime? LastEvaluatedAtUtc { get; set; }
    public Guid Version { get; set; }
}

/// <summary>The kinds of WhatsApp Business template.</summary>
public enum WhatsAppTemplateCategory
{
    Marketing,
    Utility,
}

/// <summary>
/// A WhatsApp message template, as it is approved at WhatsApp Business: its name, language and body. The body's
/// placeholders ({FirstName}, {Points}, {Store}…) are sent as the template's parameters, in order.
/// </summary>
public class WhatsAppTemplate : Entity, IVersioned, ILocalOnly
{
    /// <summary>The template's name at WhatsApp: lower-case letters, digits and underscores.</summary>
    public string Name { get; set; } = "";
    /// <summary>The language code it was approved in (en, en_US, ur).</summary>
    public string Language { get; set; } = "en";
    public WhatsAppTemplateCategory Category { get; set; }
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; }
}

public enum CampaignStatus
{
    Draft,
    /// <summary>Recipients worked out; waiting for its time.</summary>
    Scheduled,
    Sending,
    Sent,
    Cancelled,
}

/// <summary>A message to the members of a segment who agreed to WhatsApp messages, sent from a template now or later.</summary>
public class Campaign : Entity, IStoreScoped, IVersioned, ILocalOnly
{
    /// <summary>The store the campaign is for: its name fills {Store}.</summary>
    public Guid StoreId { get; set; }
    public string Name { get; set; } = "";
    public Guid SegmentId { get; set; }
    public Guid TemplateId { get; set; }
    public CampaignStatus Status { get; set; }
    /// <summary>When sending starts (UTC).</summary>
    public DateTime? ScheduledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid Version { get; set; }
}

/// <summary>Where one WhatsApp message stands.</summary>
public enum WhatsAppMessageStatus
{
    Queued,
    Sent,
    Delivered,
    Read,
    /// <summary>WhatsApp reported that it could not be delivered.</summary>
    Failed,
    /// <summary>It was not sent: refused, no consent any more, or the call to WhatsApp failed.</summary>
    Error,
}

/// <summary>
/// One WhatsApp message to one client: a campaign's (one per recipient) or a single message sent from the client's page.
/// The text and parameters are kept as they were worked out when it was queued.
/// </summary>
public class WhatsAppMessage : Entity, IStoreScoped, ILocalOnly
{
    public Guid StoreId { get; set; }
    /// <summary>Null for a single message.</summary>
    public Guid? CampaignId { get; set; }
    public Guid ClientId { get; set; }
    public Guid TemplateId { get; set; }
    /// <summary>The number it goes to, in international form.</summary>
    public string ToNumber { get; set; } = "";
    /// <summary>The template's parameters, as a JSON array of strings.</summary>
    public string ParametersJson { get; set; } = "[]";
    /// <summary>The text as the client reads it (the template with its placeholders filled in).</summary>
    public string Body { get; set; } = "";
    public WhatsAppMessageStatus Status { get; set; }
    public string? Error { get; set; }
    /// <summary>WhatsApp's id of the message (wamid…), which its delivery and read statuses name.</summary>
    public string? ProviderMessageId { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? SentBy { get; set; }
}
