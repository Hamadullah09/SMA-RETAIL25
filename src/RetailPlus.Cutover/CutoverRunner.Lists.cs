using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>Tender names (as stored on payments) by Retail25 tender type id.</summary>
    private readonly Dictionary<long, string> _tenders = [];

    /// <summary>Tender kinds by Retail25 tender type id.</summary>
    private readonly Dictionary<long, TenderKind> _tenderKinds = [];

    /// <summary>Currency codes by Retail25 currency id (the base currency is not in it).</summary>
    private readonly Dictionary<long, string> _currencies = [];

    private readonly Dictionary<long, (Guid Id, string Name)> _departments = [];
    private readonly Dictionary<long, Guid> _categories = [];

    private string TenderName(long tenderTypeId) => _tenders.GetValueOrDefault(tenderTypeId, "Tender " + Number(tenderTypeId));

    /// <summary>Retail25's tender behaviours as tender kinds. A manual tender named like a cheque is a cheque.</summary>
    private static TenderKind KindOf(SourceRow tender) => tender.Int("behaviour") switch
    {
        0 => TenderKind.Cash,
        1 => TenderKind.Card,
        2 or 3 => TenderKind.GiftCard,
        4 => TenderKind.OnAccount,
        _ when (tender.Text("code") + " " + tender.Text("display_name")).Contains("CHEQ", StringComparison.OrdinalIgnoreCase)
            || (tender.Text("code") + " " + tender.Text("display_name")).Contains("CHECK", StringComparison.OrdinalIgnoreCase) => TenderKind.Cheque,
        _ => TenderKind.Other,
    };

    /// <summary>Tender types, foreign currencies, departments and categories: company-wide lists, matched by name.</summary>
    private async Task ListsAsync(CancellationToken ct)
    {
        Log("Tenders, currencies, departments and categories");
        Dictionary<string, Guid> tendersByName, ratesByCurrency, departmentsByName, categoriesByName;
        await using (var db = _w.NewContext())
        {
            tendersByName = await db.TenderTypes.ToDictionaryAsync(t => t.Name.ToUpperInvariant(), t => t.Id, ct);
            ratesByCurrency = await db.FxRates.ToDictionaryAsync(r => r.Currency.ToUpperInvariant(), r => r.Id, ct);
            departmentsByName = await db.Departments.ToDictionaryAsync(d => d.Name.ToUpperInvariant(), d => d.Id, ct);
            categoriesByName = await db.Categories.ToDictionaryAsync(c => c.Name.ToUpperInvariant(), c => c.Id, ct);
        }

        // Currencies first: a tender may take one.
        var fxArea = _report.Area("Exchange rates", "currencies");
        var baseCodes = _stores.Values.Select(s => s.Row.Text("base_currency_code")?.ToUpperInvariant()).OfType<string>().ToHashSet();
        var rates = new List<FxRate>();
        foreach (var row in _src["currencies"].Rows.OrderBy(r => r.Id))
        {
            fxArea.Read++;
            var code = row.Text("code")?.ToUpperInvariant();
            if (code is null) { fxArea.Skip("no code"); continue; }
            if (row.Bool("is_base_currency") || baseCodes.Contains(code)) { fxArea.Skip("the home currency (no rate needed)"); continue; }
            if (!row.Bool("is_active")) { fxArea.Skip("not active"); continue; }
            code = Cut(code, 10);
            _currencies[row.Id] = code;
            rates.Add(From(new FxRate { Id = ratesByCurrency.GetValueOrDefault(code, StableIds.For("currency", row.Id)), Currency = code, Rate = Round(row.Dec("exchange_rate"), 6, "FxRate.Rate"), CreatedAtUtc = Utc(row.Instant("created_at")) }, row));
        }
        await _w.UpsertAsync(fxArea, rates, ct);

        var tenderArea = _report.Area("Tender types", "tender_types");
        var tenders = new List<TenderType>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _src["tender_types"].Rows.OrderBy(r => r.Int("sort_order")).ThenBy(r => r.Id))
        {
            tenderArea.Read++;
            var name = Cut(row.Text("display_name") ?? row.Text("code") ?? "Tender " + Number(row.Id), 16).TrimEnd();
            if (!names.Add(name))
            {
                var original = name;
                int n = 2;
                do name = Cut(original, 13).TrimEnd() + " " + Number(n++);
                while (!names.Add(name));
                Note($"Retail25 tender \"{row.Text("display_name")}\" is called \"{name}\" here: another tender has the same name in 16 characters.");
            }
            var kind = KindOf(row);
            _tenders[row.Id] = name;
            _tenderKinds[row.Id] = kind;
            var currency = row.Text("currency_code")?.ToUpperInvariant();
            tenders.Add(From(new TenderType
            {
                Id = tendersByName.GetValueOrDefault(name.ToUpperInvariant(), StableIds.For("tender", row.Id)),
                Name = name, Kind = kind, SortOrder = row.Int("sort_order"), OpensDrawer = row.Bool("opens_cash_drawer"),
                IsActive = row.Bool("is_active") && !row.Bool("is_deleted"),
                Currency = currency is not null && !baseCodes.Contains(currency) ? Cut(currency, 10) : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row));
            if (row.Int("behaviour") == 3) Note($"Retail25 tender \"{name}\" (gift certificates) is a gift card tender here: certificates are gift cards of the kind Certificate and are spent the same way.");
        }
        await _w.UpsertAsync(tenderArea, tenders, ct);

        // Departments and categories are per location in Retail25 and company-wide here: the same name is one entry.
        // Names are kept in capitals, as the program keeps them, and cut to 15 characters as in Retail Plus.
        var carriedProducts = _src["products"].Rows.Where(p => _stores.ContainsKey(p.Long("location_id"))).ToList();
        var usedDepartments = carriedProducts.Select(p => p.LongOrNull("department_id")).OfType<long>().ToHashSet();
        var usedCategories = carriedProducts.Select(p => p.LongOrNull("category_id")).OfType<long>().ToHashSet();
        var departmentRows = await NamedListAsync<Department>(_report.Area("Departments", "departments"), _src["departments"], departmentsByName, usedDepartments,
            (id, name) => new Department { Id = id, Name = name }, "department", ct);
        foreach (var (sourceId, entity) in departmentRows) _departments[sourceId] = (entity.Id, entity.Name);
        var categoryRows = await NamedListAsync<Category>(_report.Area("Categories", "categories"), _src["categories"], categoriesByName, usedCategories,
            (id, name) => new Category { Id = id, Name = name }, "category", ct);
        foreach (var (sourceId, entity) in categoryRows) _categories[sourceId] = entity.Id;
    }

    private async Task<List<(long SourceId, T Entity)>> NamedListAsync<T>(AreaResult area, SourceTable table, Dictionary<string, Guid> existing,
        HashSet<long> used, Func<Guid, string, T> create, string kind, CancellationToken ct) where T : Entity
    {
        var byName = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<(long, T)>();
        // The full names, so that two names that are the same in 15 characters are told apart.
        var fullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows.Where(r => _stores.ContainsKey(r.Long("location_id")) || used.Contains(r.Id)).OrderBy(r => r.Long("location_id")).ThenBy(r => r.Id))
        {
            area.Read++;
            if (row.Bool("is_deleted") && !used.Contains(row.Id)) { area.Skip($"deleted in Retail25 and on no item"); continue; }
            var full = (row.Text("name") ?? row.Text("code") ?? kind + " " + Number(row.Id)).ToUpperInvariant();
            var name = Cut(full, 15).TrimEnd();
            if (fullNames.TryGetValue(name, out var other) && other != full)
            {
                int n = 2;
                var stem = Cut(full, 12).TrimEnd();
                do name = stem + "#" + Number(n++);
                while (fullNames.ContainsKey(name));
            }
            fullNames[name] = full;
            if (name != full) Note($"Retail25 {kind} \"{row.Text("name")}\" is \"{name}\" here (names have at most 15 characters).");
            if (!byName.TryGetValue(name, out var entity))
            {
                entity = From(create(existing.GetValueOrDefault(name.ToUpperInvariant(), StableIds.For(kind, name)), name), row);
                entity.CreatedAtUtc = Utc(row.Instant("created_at"));
                byName[name] = entity;
            }
            else area.Skip($"same name as another {kind} (merged)");
            mapped.Add((row.Id, entity));
        }
        await _w.UpsertAsync(area, byName.Values.ToList(), ct);
        return mapped;
    }
}
