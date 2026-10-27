using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;
using RetailPlus.Data;
using RetailPlus.Data.Import;
using RetailPlus.Data.Services;

namespace RetailPlus.Cutover;

/// <summary>What to carry over, from where, to where.</summary>
public sealed class CutoverOptions
{
    /// <summary>The Retail25 SQL Server database. It is only ever read.</summary>
    public required string SourceConnectionString { get; init; }
    /// <summary>The new database: the server's SQL Server database, or a SQLite file. It is created or upgraded first.</summary>
    public required DatabaseConnection Target { get; init; }
    /// <summary>Read, map and check everything, write nothing.</summary>
    public bool DryRun { get; init; }
    /// <summary>Only the Retail25 location with this code (and its store); every location when null.</summary>
    public string? StoreCode { get; init; }
    public Action<string>? Progress { get; init; }
}

/// <summary>
/// Carries a Retail25 database (the web system at pos.sma-techno.net) over into the new model: stores, settings, lists,
/// users and roles, items with their stock, clients with their balances and points, gift cards, sales history, open
/// documents, commission rules and the time clock (docs/CUTOVER.md has the mapping and the decisions). Everything is
/// written in one transaction through <see cref="RetailPlusDbContext"/> with the automatic audit lines held back, and
/// one Import line written for the run. Running it again over the same data changes nothing; running it after Retail25
/// changed updates what changed. The run ends with checks that must match: stock, receivables, gift card liability,
/// loyalty points, and sales per day and per tender.
/// </summary>
public sealed partial class CutoverRunner(CutoverOptions options)
{
    /// <summary>The Retail25 tables the cutover reads; every other table is listed in the report as not carried over.</summary>
    internal static readonly string[] Tables =
    [
        "locations", "stations", "business_profiles", "branding_assets", "pos_policies", "loyalty_policies", "late_charge_policies",
        "tax_configurations", "currencies", "tender_types", "number_sequences",
        "departments", "categories", "suppliers",
        "products", "product_prices", "price_breaks", "bonus_pricings", "sale_pricings", "product_suppliers", "kit_components",
        "matrix_dimensions", "product_variants", "serialized_units", "product_images", "stock_levels",
        "customers", "customer_accounts", "customer_pricing_profiles", "loyalty_ledger_entries", "ar_ledger_entries",
        "gift_cards", "gift_certificates",
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "staff_profiles", "role_permissions",
        "sales_transactions", "sale_lines", "sale_tenders", "sale_adjustments", "sale_tax_snapshots", "commission_ledger_entries",
        "invoices", "invoice_payments", "layaways", "layaway_lines", "layaway_payments", "price_quotes", "price_quote_lines",
        "purchase_orders", "purchase_order_lines", "customer_orders", "customer_order_lines",
        "stock_transfers", "stock_transfer_lines", "commission_rules", "time_clock_entries",
    ];

    private readonly CutoverReport _report = new();
    private readonly Dictionary<object, string> _where = new(ReferenceEqualityComparer.Instance);
    private Retail25Source _src = null!;
    private TargetWriter _w = null!;

    public static string ToolVersion => typeof(CutoverRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Runs the cutover and returns its report. Throws <see cref="CutoverRefusedException"/> before writing anything if refused.</summary>
    public async Task<CutoverReport> RunAsync(CancellationToken cancellationToken = default)
    {
        _report.StartedAt = Seconds(DateTime.Now);
        _report.DryRun = options.DryRun;
        _report.ToolVersion = ToolVersion;
        _report.StoreCode = options.StoreCode?.Trim().ToUpperInvariant();
        _report.Target = options.Target.Description;

        Log("Reading Retail25");
        _src = await Retail25Source.ReadAsync(options.SourceConnectionString, Tables, options.Progress, cancellationToken);
        _report.Source = _src.Description;

        var target = options.Target;
        string? staging = null;
        if (options.DryRun)
        {
            if (!await IsReadyAsync(target, cancellationToken))
            {
                // A dry run never creates or upgrades the target; one without a schema is staged in a throwaway file.
                staging = Path.Combine(Path.GetTempPath(), "RetailPlus_Cutover_DryRun_" + Guid.NewGuid().ToString("N")[..12] + ".db");
                target = new DatabaseConnection(DatabaseProvider.Sqlite, new SqliteConnectionStringBuilder { DataSource = staging, Pooling = false }.ToString());
                _report.StagedIn = staging;
                Log("The target has no schema yet; staging the dry run in a temporary database");
                await new DataContext(target).MigrateAsync(cancellationToken);
            }
        }
        else
        {
            Log($"Preparing {target.Description}");
            await new DataContext(target).MigrateAsync(cancellationToken);
            _report.ImportBatchId = await StartBatchAsync(target, cancellationToken);
        }

        DbConnection connection = target.Provider == DatabaseProvider.Sqlite
            ? new SqliteConnection(target.ConnectionString)
            : new SqlConnection(target.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            _w = new TargetWriter(connection, transaction, target.Provider, _report, _where);
            await CarryOverAsync(cancellationToken);
            Log("Checking totals");
            await CheckAsync(cancellationToken);
            await WriteAuditAsync(cancellationToken);
            if (options.DryRun) await transaction.RollbackAsync(cancellationToken);
            else await transaction.CommitAsync(cancellationToken);
            _report.Succeeded = true;
        }
        catch (Exception ex)
        {
            _report.Error = ex.InnerException is null ? ex.Message : ex.Message + " " + ex.InnerException.Message;
            throw;
        }
        finally
        {
            await connection.DisposeAsync();
            _report.FinishedAt = Seconds(DateTime.Now);
            if (_report.ImportBatchId > 0) await FinishBatchAsync(target);
            if (staging is not null)
                foreach (var file in new[] { staging, staging + "-wal", staging + "-shm" })
                    if (File.Exists(file)) File.Delete(file);
        }
        return _report;
    }

    /// <summary>The areas in the order their rows depend on each other.</summary>
    private async Task CarryOverAsync(CancellationToken ct)
    {
        await StoresAsync(ct);
        await ListsAsync(ct);
        await UsersAsync(ct);
        await SuppliersAsync(ct);
        await ItemsAsync(ct);
        await ClientsAsync(ct);
        await SalesAsync(ct);
        await DocumentsAsync(ct);
        await StockAsync(ct);
        await GiftCardsAndLoyaltyAsync(ct);
        await PurchaseOrdersAsync(ct);
        await CustomerOrdersAsync(ct);
        await TransfersAsync(ct);
        await CommissionAndTimeClockAsync(ct);
        ListNotCarriedOver();
    }

    /// <summary>True when the target exists and has every migration, so a dry run can stage its rows there.</summary>
    private static async Task<bool> IsReadyAsync(DatabaseConnection target, CancellationToken ct)
    {
        if (target.Provider == DatabaseProvider.Sqlite && !File.Exists(target.DatabaseName)) return false;
        await using var db = RetailPlusDb.CreateContext(target);
        if (!await db.Database.CanConnectAsync(ct)) return false;
        return !(await db.Database.GetPendingMigrationsAsync(ct)).Any();
    }

    private async Task<int> StartBatchAsync(DatabaseConnection target, CancellationToken ct)
    {
        await using var db = RetailPlusDb.CreateContext(target);
        var batch = new ImportBatch { StartedAt = _report.StartedAt, SourcePath = Cut("Retail25 " + _report.Source, 260), ToolVersion = Cut("Cutover " + ToolVersion, 50), Status = "Running" };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }

    private async Task FinishBatchAsync(DatabaseConnection target)
    {
        await using var db = RetailPlusDb.CreateContext(target);
        var batch = await db.ImportBatches.FindAsync(_report.ImportBatchId);
        if (batch is null) return;
        batch.FinishedAt = _report.FinishedAt;
        batch.Status = _report.Succeeded ? "Succeeded" : "Failed";
        batch.ManifestJson = JsonSerializer.Serialize(_src?.RowCounts.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value));
        batch.ReportJson = _report.ToJson();
        await db.SaveChangesAsync();
    }

    /// <summary>The run's own line in the audit log, in the first store carried over.</summary>
    private async Task WriteAuditAsync(CancellationToken ct)
    {
        if (_stores.Count == 0) return;
        await using var db = _w.NewContext(new StationIdentity(_stores.Values.First().StoreId, 0));
        int differ = _report.Checks.Count(c => !c.Matches);
        db.Audit(AuditActions.Import,
            $"Retail25 cutover from {_report.Source}: {_report.Added} rows added, {_report.Changed} changed, {_report.Removed} removed; "
            + (differ == 0 ? $"all {_report.Checks.Count} checks match" : $"{differ} checks differ"),
            userName: Cut("Cutover " + Environment.UserName, 50));
        await db.SaveChangesAsync(ct);
    }

    // Helpers -------------------------------------------------------------------------------------------------------

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Progress, with the time since the start (so the freeze window of the final run can be planned).</summary>
    private void Log(string message) => options.Progress?.Invoke($"[{_clock.Elapsed:mm\\:ss}] {message}");

    private void Warn(string message) => _report.Warnings.Add(message);

    private void Note(string message) => _report.Notes.Add(message);

    /// <summary>Remembers which Retail25 row an entity came from, for warnings.</summary>
    private T From<T>(T entity, SourceRow row) where T : class
    {
        _where[entity] = $"{row.Table} {row.Id}";
        return entity;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    private static DateTime Seconds(DateTime t) => t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>When a Retail25 row was made, as the new model keeps it (UTC, milliseconds); default when unknown.</summary>
    private static DateTime Utc(DateTimeOffset? at) =>
        at is { } t ? t.UtcDateTime.AddTicks(-(t.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond)) : default;

    private decimal Round(decimal value, int decimals, string field)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded != value) _report.Rounded[field] = _report.Rounded.GetValueOrDefault(field) + 1;
        return rounded;
    }

    /// <summary>Money: 2 decimals.</summary>
    private decimal Money(decimal value, string field) => Round(value, 2, field);

    /// <summary>Unit costs, quantities and rates: 3 decimals, as Retail Plus kept them.</summary>
    private decimal Three(decimal value, string field) => Round(value, 3, field);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
