namespace RetailPlus.Core.Model;

/// <summary>Where a lead stands.</summary>
public enum LeadStatus
{
    Open,
    /// <summary>Became a client; <see cref="Lead.ConvertedClientId"/> says which.</summary>
    Converted,
    /// <summary>Not followed up any more.</summary>
    Dropped,
}

/// <summary>
/// Someone who may become a client: a walk-in who asked for a quote, a call, a message from an advertisement. Kept on the
/// server only, for the store that took it, and converted into a client when they buy or agree to an account.
/// </summary>
public class Lead : Entity, IStoreScoped, IVersioned, ILocalOnly
{
    public Guid StoreId { get; set; }
    /// <summary>The person's name.</summary>
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string? Phone { get; set; }
    /// <summary>In international form (+923001234567), see <c>Rules.WhatsAppNumbers</c>.</summary>
    public string? WhatsAppNumber { get; set; }
    public string? Email { get; set; }
    /// <summary>Where the lead came from: walk-in, phone, an advertisement, a referral (free text).</summary>
    public string? Source { get; set; }
    /// <summary>The user who follows the lead up; no foreign key, so a deleted user leaves the lead in place.</summary>
    public Guid? OwnerId { get; set; }
    public string? Notes { get; set; }
    public LeadStatus Status { get; set; }
    public Guid? ConvertedClientId { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public Guid Version { get; set; }
}

/// <summary>What reaching a pipeline stage means for an opportunity.</summary>
public enum StageOutcome
{
    Open,
    Won,
    Lost,
}

/// <summary>A column of the sales pipeline. The list is the company's own (New, Qualified, Proposal, Won, Lost to start with).</summary>
public class OpportunityStage : Entity, ILocalOnly
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>The chance of winning, in percent, an opportunity gets when it reaches the stage.</summary>
    public decimal Probability { get; set; }
    public StageOutcome Outcome { get; set; }
}

/// <summary>A sale that may come: for a client or a lead, with its expected value and where it stands in the pipeline.</summary>
public class Opportunity : Entity, IStoreScoped, IVersioned, ILocalOnly
{
    public Guid StoreId { get; set; }
    public string Title { get; set; } = "";
    public Guid? ClientId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid StageId { get; set; }
    public decimal ExpectedValue { get; set; }
    /// <summary>Chance of winning in percent (the stage's when it moved there, or set by hand).</summary>
    public decimal Probability { get; set; }
    public DateOnly? ExpectedCloseOn { get; set; }
    public Guid? OwnerId { get; set; }
    /// <summary>Why it was lost; required for a Lost stage.</summary>
    public string? LostReason { get; set; }
    /// <summary>The store's day it was won or lost; null while open.</summary>
    public DateOnly? ClosedOn { get; set; }
    public string? Notes { get; set; }
    public Guid Version { get; set; }
}

/// <summary>What an activity is.</summary>
public enum ActivityKind
{
    Call,
    Visit,
    Meeting,
    Note,
    Task,
}

/// <summary>
/// A call, visit, meeting, note or task on a client, a lead or an opportunity, with an owner and, for what is still to
/// do, a due time (the store's local time, like every business time).
/// </summary>
public class CrmActivity : Entity, IStoreScoped, IVersioned, ILocalOnly
{
    public Guid StoreId { get; set; }
    public ActivityKind Kind { get; set; }
    public string Subject { get; set; } = "";
    public string? Notes { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? OpportunityId { get; set; }
    public DateTime? DueAt { get; set; }
    public bool IsDone { get; set; }
    public DateTime? DoneAtUtc { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid Version { get; set; }
}

/// <summary>
/// How much a store, or one salesperson at a store, should sell in a month (net sales, as the dashboard counts them).
/// </summary>
public class SalesTarget : Entity, IStoreScoped, IVersioned, ILocalOnly
{
    public Guid StoreId { get; set; }
    /// <summary>The salesperson; null for the store's own target.</summary>
    public Guid? UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public Guid Version { get; set; }
}
