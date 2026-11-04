using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A client of a segment, with the figures the rules looked at.</summary>
public sealed record SegmentMember(Guid ClientId, string? Number, string Name, string? Phone, string? Email, string? WhatsAppNumber, bool WhatsAppOptIn,
    decimal Spend, int Visits, DateOnly? LastPurchase, decimal Points, decimal Balance, DateOnly? Birthday);

/// <summary>
/// Customer segments: rules saved under a name and worked out again on demand from the clients and their sales (see
/// <see cref="SegmentRules"/>). The figures are read from the database in a few grouped queries and the rules applied in
/// memory, which suits the tens of thousands of clients a retail business has.
/// </summary>
public sealed class SegmentService(DataContext data)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SegmentRules Rules(CustomerSegment segment) =>
        JsonSerializer.Deserialize<SegmentRules>(segment.RulesJson, Json) ?? new SegmentRules();

    public static string RulesJson(SegmentRules rules) => JsonSerializer.Serialize(rules, Json);

    /// <summary>Adds or changes a segment (a stale version is a conflict).</summary>
    public async Task<CustomerSegment> SaveAsync(CustomerSegment segment, SegmentRules rules)
    {
        segment.Name = (segment.Name ?? "").Trim();
        if (segment.Name.Length == 0) throw new BusinessRuleException("Give the segment a name.");
        if (rules.Problem() is { } problem) throw new BusinessRuleException(problem);
        segment.RulesJson = RulesJson(rules);
        segment.StoreId = rules.StoreId;
        segment.Description = string.IsNullOrWhiteSpace(segment.Description) ? null : segment.Description.Trim();
        await using var db = data.Create();
        if (rules.StoreId is { } store && !await db.Stores.AnyAsync(s => s.Id == store)) throw new BusinessRuleException("That store does not exist.");
        if (segment.Id == Guid.Empty) db.CustomerSegments.Add(segment);
        else
        {
            var stored = await db.CustomerSegments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == segment.Id) ?? throw new BusinessRuleException("That segment no longer exists.");
            segment.LastCount = stored.LastCount;
            segment.LastEvaluatedAtUtc = stored.LastEvaluatedAtUtc;
            db.CustomerSegments.Update(segment);
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("Someone else changed this segment. Load it again and make your changes again.");
        }
        return segment;
    }

    /// <summary>Deletes a segment no campaign uses.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await using var db = data.Create();
        var segment = await db.CustomerSegments.SingleOrDefaultAsync(s => s.Id == id) ?? throw new BusinessRuleException("That segment no longer exists.");
        if (await db.Campaigns.AnyAsync(c => c.SegmentId == id)) throw new BusinessRuleException("A campaign uses this segment, so it is kept.");
        db.CustomerSegments.Remove(segment);
        await db.SaveChangesAsync();
    }

    /// <summary>Works out a saved segment again and keeps how many members it has.</summary>
    public async Task<List<SegmentMember>> EvaluateSavedAsync(Guid id, DateOnly today)
    {
        CustomerSegment segment;
        await using (var db = data.Create())
            segment = await db.CustomerSegments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id) ?? throw new BusinessRuleException("That segment no longer exists.");
        var members = await EvaluateAsync(Rules(segment), today);
        await using (var db = data.Create())
        {
            var now = DateTime.UtcNow;
            int count = members.Count;
            await db.CustomerSegments.Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCount, count).SetProperty(x => x.LastEvaluatedAtUtc, now).Touched());
        }
        return members;
    }

    /// <summary>The members of a segment with these rules, biggest spenders first.</summary>
    public async Task<List<SegmentMember>> EvaluateAsync(SegmentRules rules, DateOnly today)
    {
        if (rules.Problem() is { } problem) throw new BusinessRuleException(problem);
        await using var db = data.Create();
        var (from, to) = rules.Period(today);
        var sales = db.Sales.AsNoTracking().Where(s => s.ClientId != null && s.Status == SaleStatus.Completed);
        if (rules.StoreId is { } store) sales = sales.Where(s => s.StoreId == store);
        var inPeriod = sales;
        if (from is { } f)
        {
            var start = f.ToDateTime(TimeOnly.MinValue);
            inPeriod = inPeriod.Where(s => s.SoldAt >= start);
        }
        if (to is { } t)
        {
            var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue);
            inPeriod = inPeriod.Where(s => s.SoldAt < end);
        }

        var period = await inPeriod.GroupBy(s => s.ClientId!.Value).Select(g => new { ClientId = g.Key, Spend = g.Sum(s => s.Total), Visits = g.Count() })
            .ToDictionaryAsync(x => x.ClientId, x => (x.Spend, x.Visits));
        var last = await sales.GroupBy(s => s.ClientId!.Value).Select(g => new { ClientId = g.Key, Last = g.Max(s => s.SoldAt) })
            .ToDictionaryAsync(x => x.ClientId, x => DateOnly.FromDateTime(x.Last));
        HashSet<Guid>? department = null;
        if (rules.DepartmentId is { } departmentId)
        {
            var name = await db.Departments.AsNoTracking().Where(d => d.Id == departmentId).Select(d => d.Name).SingleOrDefaultAsync() ?? "";
            department = [.. await db.SaleLines.AsNoTracking().Where(l => l.Department == name && l.Kind == SaleLineKind.Sale)
                .Join(inPeriod, l => l.SaleId, s => s.Id, (l, s) => s.ClientId!.Value).Distinct().ToListAsync()];
        }

        var clients = db.Clients.AsNoTracking().Where(c => !c.IsDeleted);
        if (rules.MinPoints is { } minPoints) clients = clients.Where(c => c.Points >= minPoints);
        if (rules.MaxPoints is { } maxPoints) clients = clients.Where(c => c.Points <= maxPoints);
        if (rules.MinBalance is { } owing) clients = clients.Where(c => c.Balance >= owing);
        if (rules.WhatsAppConsent == true) clients = clients.Where(c => c.WhatsAppOptIn && c.WhatsAppNumber != null);
        // Clients with spend or visits asked for must have bought in the period (unless a maximum alone is asked for).
        if (rules.MinSpend is > 0 || rules.MinVisits is > 0)
        {
            var buyers = period.Keys.ToList();
            if (buyers.Count < 2000) clients = clients.Where(c => buyers.Contains(c.Id));
        }
        var rows = await clients.Select(c => new
        {
            c.Id, c.Number, c.FirstName, c.LastName, c.Company, c.WorkPhone, c.HomePhone, c.Email, c.WhatsAppNumber, c.WhatsAppOptIn, c.Points, c.Balance,
            c.Birthday,
        }).ToListAsync();

        var members = new List<SegmentMember>();
        foreach (var c in rows)
        {
            var (spend, visits) = period.GetValueOrDefault(c.Id);
            DateOnly? lastPurchase = last.TryGetValue(c.Id, out var day) ? day : null;
            var facts = new SegmentFacts(spend, visits, lastPurchase, department?.Contains(c.Id) ?? false, c.Birthday, c.Points, c.Balance, c.WhatsAppOptIn, c.WhatsAppNumber);
            if (!Segments.Matches(rules, facts, today)) continue;
            string name = !string.IsNullOrWhiteSpace(c.FirstName) || !string.IsNullOrWhiteSpace(c.LastName) ? $"{c.FirstName} {c.LastName}".Trim() : c.Company ?? "";
            members.Add(new SegmentMember(c.Id, c.Number, name, c.HomePhone ?? c.WorkPhone, c.Email, c.WhatsAppNumber, c.WhatsAppOptIn, spend, visits, lastPurchase,
                c.Points, c.Balance, c.Birthday));
        }
        return [.. members.OrderByDescending(m => m.Spend).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The members as CSV, for a spreadsheet or a mailing program (UTF-8 with a byte order mark).</summary>
    public static byte[] Csv(IEnumerable<SegmentMember> members)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Number,Name,Phone,Email,WhatsApp,WhatsApp consent,Spend,Visits,Last purchase,Points,Balance,Birthday");
        static string Q(string? s) => s is null ? "" : s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        foreach (var m in members)
            sb.Append(Q(m.Number)).Append(',').Append(Q(m.Name)).Append(',').Append(Q(m.Phone)).Append(',').Append(Q(m.Email)).Append(',')
                .Append(Q(m.WhatsAppNumber)).Append(',').Append(m.WhatsAppOptIn ? "yes" : "no").Append(',')
                .Append(m.Spend.ToString("0.00", inv)).Append(',').Append(m.Visits.ToString(inv)).Append(',')
                .Append(m.LastPurchase?.ToString("yyyy-MM-dd", inv)).Append(',').Append(m.Points.ToString("0.##", inv)).Append(',')
                .Append(m.Balance.ToString("0.00", inv)).Append(',').Append(m.Birthday?.ToString("yyyy-MM-dd", inv)).AppendLine();
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
