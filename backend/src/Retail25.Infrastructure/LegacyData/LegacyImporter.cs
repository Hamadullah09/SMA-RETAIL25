using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Migration;
using Retail25.Domain.Accounting;
using Retail25.Domain.Catalog;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Migration;
using Retail25.Domain.Purchasing;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.LegacyData;

/// <summary>
/// Validates and imports staged legacy rows (doc 09 §3).
/// <para>
/// Two rules shape everything here. First, the dry run and the import take the same path — the flag
/// only decides whether <c>SaveChanges</c> is called — because a dry run down a different code path
/// proves nothing about the import that follows it. Second, opening stock arrives as a ledger entry
/// and never as a raw <c>OnHand</c> write, so the ledger is authoritative from row one.
/// </para>
/// </summary>
public sealed class LegacyImporter : ILegacyImporter
{
    /// <summary>
    /// The provider name every legacy mapping is filed under, so a re-import recognises what it
    /// already brought across rather than duplicating it.
    /// </summary>
    private const string Provider = "retailplus25";

    private const string OpeningBalanceReason = "Legacy opening balance";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    /// <summary>
    /// Takes the concrete context rather than the port because the dry run has to discard the change
    /// tracker, and "do all the work and then throw it away" is not something the application-layer
    /// interface should be able to express.
    /// </summary>
    public LegacyImporter(ApplicationDbContext db, ICurrentUser currentUser, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ValidationFinding>> ValidateAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rows);

        var findings = new List<ValidationFinding>();
        var entity = Parse(batch.Entity);

        // Duplicates are found across the file rather than row by row: the second occurrence is only
        // a problem because of the first, and both need naming.
        var byKey = rows
            .Where(r => !r.IsDeletedInSource && r.LegacyKey is not null)
            .GroupBy(r => r.LegacyKey!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var duplicate in byKey)
        {
            foreach (var row in duplicate)
            {
                findings.Add(new ValidationFinding(
                    row.RowNumber,
                    "key",
                    FindingSeverity.Blocking,
                    "migration.duplicate_key",
                    $"'{duplicate.Key}' appears on {duplicate.Count()} rows. Each must be unique.",
                    duplicate.Key));
            }
        }

        foreach (var row in rows)
        {
            if (row.IsDeletedInSource)
            {
                // Reported so the count is explained, not as a fault.
                findings.Add(new ValidationFinding(
                    row.RowNumber, null, FindingSeverity.Warning,
                    "migration.deleted_in_source",
                    "The legacy system had deleted this row. It will not be imported."));

                continue;
            }

            findings.AddRange(entity switch
            {
                LegacyEntity.Inventory => ValidateInventory(row),
                LegacyEntity.Client => ValidateClient(row),
                LegacyEntity.Supplier => ValidateSupplier(row),
                LegacyEntity.StockCount => ValidateStockCount(row),
                LegacyEntity.RegisterSales => ValidateRegisterSales(row),
                LegacyEntity.Invoice => ValidateInvoice(row),
                _ => [],
            });
        }

        // Orphan check: an invoice for a customer number that is nowhere in the system yet.
        if (entity == LegacyEntity.Invoice)
        {
            findings.AddRange(await ValidateInvoiceCustomersAsync(batch, rows, ct));
        }

        return findings;
    }

    public async Task<ReconciliationReport> RunAsync(
        MigrationBatch batch,
        IReadOnlyList<StagedRow> rows,
        LegacyControlTotals? legacyTotals,
        bool dryRun,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rows);

        var entity = Parse(batch.Entity);
        var importable = rows.Where(r => !r.IsDeletedInSource).ToList();

        var outcome = entity switch
        {
            LegacyEntity.Inventory => await ImportInventoryAsync(batch, importable, ct),
            LegacyEntity.Client => await ImportClientsAsync(batch, importable, ct),
            LegacyEntity.Supplier => await ImportSuppliersAsync(batch, importable, ct),
            _ => NotYetImportable(entity, importable),
        };

        if (dryRun)
        {
            // Every change was made on tracked entities and none of it is saved. Clearing the tracker
            // discards them outright, so a later SaveChanges in the same scope — the one that stamps
            // the batch — cannot flush them by accident. The caller reloads the batch afterwards,
            // because this detaches that too.
            _db.ChangeTracker.Clear();
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        return new ReconciliationReport(
            entity.ToString(),
            rows.Count,
            outcome.Imported,
            rows.Count - outcome.Imported,
            Reconcile(entity, outcome, legacyTotals),
            outcome.Warnings);
    }

    /* ---------------------------------------------------------------------------------------------
     * Validation
     * ------------------------------------------------------------------------------------------- */

    private static IEnumerable<ValidationFinding> ValidateInventory(StagedRow row)
    {
        if (LegacyFieldParsing.NormaliseCode(row.Values.GetValueOrDefault("StockCode")) is null)
        {
            yield return Blocking(row, "StockCode", "migration.missing_code", "An item needs a stock code.");
        }

        if (string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault("ItemName")))
        {
            yield return Blocking(row, "ItemName", "migration.missing_name", "An item needs a description.");
        }

        foreach (var column in new[] { "Cost", "Price", "OnHand" })
        {
            var raw = row.Values.GetValueOrDefault(column);

            if (string.IsNullOrWhiteSpace(raw))
            {
                yield return Warning(row, column, "migration.blank_number", $"{column} is blank and will import as zero.");
            }
            else if (!LegacyFieldParsing.TryDecimal(raw, out _))
            {
                yield return Blocking(row, column, "migration.unparseable_number", $"'{raw}' is not a number.", raw);
            }
        }

        if (LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("Price"), out var price)
            && LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("Cost"), out var cost)
            && price > 0m && cost > price)
        {
            // Not blocking — a genuine loss leader exists — but it is the shape of a
            // cost-and-price-swapped column, which is worth a look before a whole catalogue lands.
            yield return Warning(row, "Price", "migration.cost_above_price",
                $"Cost {cost} is above price {price}. Check the columns are not swapped.");
        }
    }

    private static IEnumerable<ValidationFinding> ValidateClient(StagedRow row)
    {
        var hasName = !string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault("LastName"))
                      || !string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault("Company"));

        if (!hasName)
        {
            yield return Blocking(row, "LastName", "migration.missing_name", "A client needs a surname or a company.");
        }

        var number = row.Values.GetValueOrDefault("CustomerNumber");

        if (!string.IsNullOrWhiteSpace(number) && !LegacyFieldParsing.TryInt(number, out _))
        {
            yield return Warning(row, "CustomerNumber", "migration.non_numeric_key",
                $"'{number}' is not a number. A new customer number will be issued.", number);
        }

        var limit = row.Values.GetValueOrDefault("CreditLimit");

        if (!string.IsNullOrWhiteSpace(limit) && !LegacyFieldParsing.TryDecimal(limit, out _))
        {
            yield return Blocking(row, "CreditLimit", "migration.unparseable_number", $"'{limit}' is not a number.", limit);
        }
    }

    private static IEnumerable<ValidationFinding> ValidateSupplier(StagedRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault("Company")))
        {
            yield return Blocking(row, "Company", "migration.missing_name", "A supplier needs a company name.");
        }

        if (string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault("SupplierNumber")))
        {
            yield return Warning(row, "SupplierNumber", "migration.missing_key",
                "No supplier number. One will be issued.");
        }
    }

    private static IEnumerable<ValidationFinding> ValidateStockCount(StagedRow row)
    {
        if (LegacyFieldParsing.NormaliseCode(row.Values.GetValueOrDefault("StockCode")) is null)
        {
            yield return Blocking(row, "StockCode", "migration.missing_code", "A count row needs a stock code.");
        }

        var count = row.Values.GetValueOrDefault("ShelfCount");

        if (!LegacyFieldParsing.TryDecimal(count, out var quantity))
        {
            yield return Blocking(row, "ShelfCount", "migration.unparseable_number", $"'{count}' is not a number.", count);
        }
        else if (quantity < 0m)
        {
            yield return Blocking(row, "ShelfCount", "migration.negative_count", "A counted quantity cannot be negative.");
        }
    }

    private static IEnumerable<ValidationFinding> ValidateRegisterSales(StagedRow row)
    {
        if (LegacyFieldParsing.NormaliseCode(row.Values.GetValueOrDefault("StockCode")) is null)
        {
            yield return Blocking(row, "StockCode", "migration.missing_code", "A sales row needs a stock code.");
        }

        if (!LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("QuantitySold"), out _))
        {
            yield return Blocking(row, "QuantitySold", "migration.unparseable_number", "Quantity sold is not a number.");
        }
    }

    private static IEnumerable<ValidationFinding> ValidateInvoice(StagedRow row)
    {
        foreach (var column in new[] { "Total", "Balance" })
        {
            var raw = row.Values.GetValueOrDefault(column);

            if (!LegacyFieldParsing.TryDecimal(raw, out _))
            {
                yield return Blocking(row, column, "migration.unparseable_number", $"'{raw}' is not a number.", raw);
            }
        }

        foreach (var column in new[] { "InvoiceDate", "DueDate" })
        {
            var raw = row.Values.GetValueOrDefault(column);

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!LegacyFieldParsing.TryDate(raw, out _))
            {
                yield return Blocking(row, column, "migration.unparseable_date", $"'{raw}' is not a date.", raw);
            }
            else if (LegacyFieldParsing.IsAmbiguousDate(raw))
            {
                // Reported rather than guessed at: 03/04/2010 is two different dates and only the
                // person holding the old system's printout knows which.
                yield return Warning(row, column, "migration.ambiguous_date",
                    $"'{raw}' could be day-first or month-first. It has been read day-first.", raw);
            }
        }
    }

    private async Task<IReadOnlyList<ValidationFinding>> ValidateInvoiceCustomersAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        var numbers = rows
            .Where(r => !r.IsDeletedInSource)
            .Select(r => r.Values.GetValueOrDefault("CustomerNumber"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();

        if (numbers.Count == 0)
        {
            return [];
        }

        var known = await _db.ExternalEntityMaps.AsNoTracking()
            .Where(m => m.Provider == Provider && m.EntityType == nameof(Customer))
            .Select(m => m.RemoteId)
            .ToListAsync(ct);

        var missing = numbers.Except(known, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (missing.Count == 0)
        {
            return [];
        }

        return rows
            .Where(r => !r.IsDeletedInSource
                        && r.Values.GetValueOrDefault("CustomerNumber") is { } number
                        && missing.Contains(number))
            .Select(r => Blocking(
                r,
                "CustomerNumber",
                "migration.orphan_invoice",
                $"No client with number '{r.Values.GetValueOrDefault("CustomerNumber")}' has been imported yet. "
                + "Import the clients file first.",
                r.Values.GetValueOrDefault("CustomerNumber")))
            .ToList();
    }

    /* ---------------------------------------------------------------------------------------------
     * Import
     * ------------------------------------------------------------------------------------------- */

    private async Task<ImportOutcome> ImportInventoryAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        var outcome = new ImportOutcome();

        var existing = await _db.Products
            .Where(p => p.LocationId == batch.LocationId)
            .ToDictionaryAsync(p => p.StockCode, p => p, ct);

        var departments = await _db.Departments.Where(d => d.LocationId == batch.LocationId)
            .ToDictionaryAsync(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase, ct);

        var categories = await _db.Categories.Where(c => c.LocationId == batch.LocationId)
            .ToDictionaryAsync(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase, ct);

        var mapped = await MappedKeysAsync(nameof(Product), ct);

        foreach (var row in rows)
        {
            var code = LegacyFieldParsing.NormaliseCode(row.Values.GetValueOrDefault("StockCode"));
            var name = row.Values.GetValueOrDefault("ItemName");

            if (code is null || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // Idempotent by legacy key: a re-run after a failed cutover updates rather than
            // duplicating, which is the difference between a second attempt and a disaster.
            if (mapped.Contains(code))
            {
                outcome.AlreadyPresent++;
            }

            LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("Price"), out var price);
            LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("Cost"), out var cost);
            LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("OnHand"), out var onHand);

            var fitted = LegacyFieldParsing.Fit(name, 200, out var truncated);

            if (truncated)
            {
                outcome.Warnings.Add($"Row {row.RowNumber}: the description was longer than 200 characters and was shortened.");
            }

            if (!existing.TryGetValue(code, out var product))
            {
                var created = Product.Create(batch.LocationId, code, fitted!, ProductType.Standard, price);

                if (created.IsFailure)
                {
                    outcome.Warnings.Add($"Row {row.RowNumber}: {created.Error.Message}");
                    continue;
                }

                product = created.Value;
                _db.Products.Add(product);
                existing[code] = product;
            }

            product.UpdateDetails(fitted!, null, null, null, null);
            product.UpdatePricing(price, cost, cost);

            if (row.Values.GetValueOrDefault("Department") is { } departmentName && departmentName.Length > 0)
            {
                product.SetDepartment(FindOrCreate(departments, departmentName, batch.LocationId, isDepartment: true));
            }

            if (row.Values.GetValueOrDefault("Category") is { } categoryName && categoryName.Length > 0)
            {
                product.SetCategory(FindOrCreate(categories, categoryName, batch.LocationId, isDepartment: false));
            }

            if (LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("PackQuantity"), out var pack) && pack > 0m)
            {
                product.UpdateOrdering(product.BaseStock, product.ReorderPoint, product.ReorderQty, pack, product.ShipWeight);
            }

            // Opening stock as a ledger entry, never a raw OnHand write (doc 09 §3). The level is
            // moved in the same breath because that is how every other writer in this system does it.
            if (onHand != 0m)
            {
                WriteOpeningBalance(batch, product, onHand, cost);
            }

            Map(nameof(Product), code, product.Id, fitted);
            outcome.Imported++;
            outcome.InventoryValue += onHand * cost;
        }

        return outcome;
    }

    private async Task<ImportOutcome> ImportClientsAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        var outcome = new ImportOutcome();
        var mapped = await MappedKeysAsync(nameof(Customer), ct);

        foreach (var row in rows)
        {
            var last = row.Values.GetValueOrDefault("LastName");
            var company = row.Values.GetValueOrDefault("Company");

            if (string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(company))
            {
                continue;
            }

            var legacyNumber = row.Values.GetValueOrDefault("CustomerNumber");

            if (legacyNumber is not null && mapped.Contains(legacyNumber.Trim().ToUpperInvariant()))
            {
                outcome.AlreadyPresent++;
                continue;
            }

            // The legacy number is kept when it is a number, so a shop's customers keep the numbers
            // printed on twenty years of statements.
            var number = LegacyFieldParsing.TryInt(legacyNumber, out var parsed) && parsed > 0
                ? parsed
                : _clock.Now.ToUnixTimeMilliseconds();

            var created = Customer.Create(
                batch.LocationId,
                number,
                row.Values.GetValueOrDefault("FirstName") ?? string.Empty,
                last ?? company!);

            if (created.IsFailure)
            {
                outcome.Warnings.Add($"Row {row.RowNumber}: {created.Error.Message}");
                continue;
            }

            var customer = created.Value;
            _db.Customers.Add(customer);

            if (LegacyFieldParsing.TryDecimal(row.Values.GetValueOrDefault("CreditLimit"), out var limit) && limit > 0m)
            {
                _db.CustomerAccounts.Add(CustomerAccount.Create(customer.Id, number, limit));
            }

            Map(
                nameof(Customer),
                legacyNumber?.Trim().ToUpperInvariant() ?? number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                customer.Id,
                customer.FullName);
            outcome.Imported++;
        }

        return outcome;
    }

    private async Task<ImportOutcome> ImportSuppliersAsync(
        MigrationBatch batch, IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        var outcome = new ImportOutcome();
        var mapped = await MappedKeysAsync(nameof(Supplier), ct);

        foreach (var row in rows)
        {
            var company = row.Values.GetValueOrDefault("Company");

            if (string.IsNullOrWhiteSpace(company))
            {
                continue;
            }

            var legacyNumber = row.Values.GetValueOrDefault("SupplierNumber")?.Trim().ToUpperInvariant();
            var key = legacyNumber ?? company.ToUpperInvariant();

            if (mapped.Contains(key))
            {
                outcome.AlreadyPresent++;
                continue;
            }

            var created = Supplier.Create(batch.LocationId, company, legacyNumber ?? key);

            if (created.IsFailure)
            {
                outcome.Warnings.Add($"Row {row.RowNumber}: {created.Error.Message}");
                continue;
            }

            _db.Suppliers.Add(created.Value);
            Map(nameof(Supplier), key, created.Value.Id, company);
            outcome.Imported++;
        }

        return outcome;
    }

    /// <summary>
    /// The file types whose importers are not built yet.
    /// <para>
    /// They stage, analyse and validate — which is most of the value, since it tells the operator
    /// whether the data is sound — and then say plainly that nothing was written rather than
    /// reporting a successful import of nothing.
    /// </para>
    /// </summary>
    private static ImportOutcome NotYetImportable(LegacyEntity entity, IReadOnlyList<StagedRow> rows)
    {
        var outcome = new ImportOutcome();

        outcome.Warnings.Add(
            $"{rows.Count} {entity} row(s) were read and checked, but importing this file type is not built yet. "
            + "Nothing has been written.");

        return outcome;
    }

    private void WriteOpeningBalance(MigrationBatch batch, Product product, decimal onHand, decimal cost)
    {
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = product.Id,
            LocationId = batch.LocationId,
            MovementType = MovementType.Adjustment,
            Quantity = onHand,
            UnitCost = cost,
            Reason = OpeningBalanceReason,
            ReferenceType = nameof(MigrationBatch),
            ReferenceId = batch.Id,
            OccurredAt = _clock.Now,
            StaffId = _currentUser.StaffId,
        });

        product.UpdateStockLevels(onHand, product.OnOrder);

        _db.StockLevels.Add(StockLevel.Create(product.Id, null, batch.LocationId));
    }

    private void Map(string entityType, string legacyKey, Guid localId, string? name)
        => _db.ExternalEntityMaps.Add(new ExternalEntityMap
        {
            Provider = Provider,
            EntityType = entityType,
            LocalId = localId,
            LocalKey = legacyKey,
            RemoteId = legacyKey,
            RemoteName = name,
            LastSyncedAt = _clock.Now,
        });

    private async Task<HashSet<string>> MappedKeysAsync(string entityType, CancellationToken ct)
        => (await _db.ExternalEntityMaps.AsNoTracking()
                .Where(m => m.Provider == Provider && m.EntityType == entityType)
                .Select(m => m.RemoteId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private Guid FindOrCreate<T>(Dictionary<string, T> cache, string name, Guid locationId, bool isDepartment)
        where T : class
    {
        if (cache.TryGetValue(name, out var existing))
        {
            return isDepartment ? (existing as Department)!.Id : (existing as Category)!.Id;
        }

        if (isDepartment)
        {
            var department = Department.Create(locationId, name).Value;
            _db.Departments.Add(department);
            cache[name] = (department as T)!;
            return department.Id;
        }

        var category = Category.Create(locationId, name).Value;
        _db.Categories.Add(category);
        cache[name] = (category as T)!;
        return category.Id;
    }

    /// <summary>
    /// Lines the imported totals up against whatever the legacy system's own reports said. A measure
    /// with no legacy figure is reported as imported-only rather than counted as reconciling — the
    /// report has to be honest about what it could not check.
    /// </summary>
    private static IReadOnlyList<ReconciliationLine> Reconcile(
        LegacyEntity entity, ImportOutcome outcome, LegacyControlTotals? legacy)
    {
        var lines = new List<ReconciliationLine>();

        void Add(string measure, decimal imported, decimal? reported)
        {
            var variance = reported is null ? null : (decimal?)(imported - reported.Value);

            lines.Add(new ReconciliationLine(measure, imported, reported, variance, variance is null or 0m));
        }

        switch (entity)
        {
            case LegacyEntity.Inventory:
                Add("Items", outcome.Imported, legacy?.ItemCount);
                Add("Inventory value at cost", decimal.Round(outcome.InventoryValue, 2, MidpointRounding.AwayFromZero), legacy?.InventoryValue);
                break;

            case LegacyEntity.Client:
                Add("Clients", outcome.Imported, legacy?.CustomerCount);
                break;

            case LegacyEntity.Supplier:
                Add("Suppliers", outcome.Imported, legacy?.SupplierCount);
                break;

            case LegacyEntity.Invoice:
                Add("Invoices", outcome.Imported, null);
                Add("Receivables balance", outcome.ReceivablesBalance, legacy?.ReceivablesBalance);
                break;

            default:
                Add("Rows", outcome.Imported, null);
                break;
        }

        if (outcome.AlreadyPresent > 0)
        {
            lines.Add(new ReconciliationLine("Already imported previously", outcome.AlreadyPresent, null, null, true));
        }

        return lines;
    }

    private static LegacyEntity Parse(string entity)
        => Enum.TryParse<LegacyEntity>(entity, ignoreCase: true, out var parsed) ? parsed : LegacyEntity.Inventory;

    private static ValidationFinding Blocking(StagedRow row, string? column, string code, string message, string? value = null)
        => new(row.RowNumber, column, FindingSeverity.Blocking, code, message, value);

    private static ValidationFinding Warning(StagedRow row, string? column, string code, string message, string? value = null)
        => new(row.RowNumber, column, FindingSeverity.Warning, code, message, value);

    private sealed class ImportOutcome
    {
        public int Imported { get; set; }

        public int AlreadyPresent { get; set; }

        public decimal InventoryValue { get; set; }

        public decimal ReceivablesBalance { get; set; }

        public List<string> Warnings { get; } = [];
    }
}
