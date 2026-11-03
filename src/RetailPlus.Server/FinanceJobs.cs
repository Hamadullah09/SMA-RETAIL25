using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>The "Finance" section: how often the finance jobs run.</summary>
public sealed class FinanceOptions
{
    /// <summary>Seconds between runs of the finance jobs (posting days, recurring expenses, the FBR queue); 0 switches them off.</summary>
    public int JobIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// The "Fbr" section: FBR's POS integration (Pakistan), off by default. <c>UseFake</c> records what would be sent and
/// sends nothing (development); otherwise every sale arriving from the tills is sent to <c>Url</c> with the POS id and
/// token. UNVERIFIED: the client follows FBR's published POS integration JSON as far as it was known when it was written;
/// test it against FBR's sandbox before switching it on for real.
/// </summary>
public sealed class FbrOptions
{
    public bool Enabled { get; set; }
    public bool UseFake { get; set; }
    /// <summary>The POS id FBR gave the business (POSID).</summary>
    public string? PosId { get; set; }
    /// <summary>The bearer token FBR issued for the POS.</summary>
    public string? Token { get; set; }
    /// <summary>The PostData address (FBR's sandbox or live one); nothing is sent without it.</summary>
    public string? Url { get; set; }
    /// <summary>Store codes the integration is for; empty for every store.</summary>
    public string[] Stores { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// FBR's POS integration client. UNVERIFIED against FBR: it posts the invoice JSON of FBR's POS integration
/// (InvoiceNumber, POSID, USIN, DateTime, totals, PaymentMode, InvoiceType, Items) with the bearer token, and reads the
/// fiscal number from the answer's <c>InvoiceNumber</c> when <c>Code</c> is 100. Never called by the tests.
/// </summary>
public sealed class FbrInvoiceSender(IHttpClientFactory clients, IOptions<FbrOptions> options) : IFiscalInvoiceSender
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public bool Enabled => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.Url) && !string.IsNullOrWhiteSpace(options.Value.PosId);

    public IReadOnlyCollection<string> Stores => options.Value.Stores;

    public async Task<FiscalResult> SendAsync(FiscalInvoiceRequest request, CancellationToken cancellationToken)
    {
        var o = options.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, o.Url) { Content = JsonContent.Create(Body(request, o.PosId!), options: Json) };
        if (!string.IsNullOrWhiteSpace(o.Token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 5, 300)));
        using var http = clients.CreateClient(nameof(FbrInvoiceSender));
        using var response = await http.SendAsync(message, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode) return new FiscalResult(false, null, $"FBR answered {(int)response.StatusCode}: {Short(text)}");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            string? code = root.TryGetProperty("Code", out var c) ? c.ToString() : null;
            string? number = root.TryGetProperty("InvoiceNumber", out var n) ? n.GetString() : null;
            if (code == "100" && !string.IsNullOrWhiteSpace(number)) return new FiscalResult(true, number, null);
            var why = root.TryGetProperty("Response", out var r) ? r.ToString() : null;
            var errors = root.TryGetProperty("Errors", out var e) && e.ValueKind != JsonValueKind.Null ? e.ToString() : null;
            return new FiscalResult(false, null, $"FBR refused (code {code}): {why} {errors}".Trim());
        }
        catch (JsonException)
        {
            return new FiscalResult(false, null, "FBR's answer was not JSON: " + Short(text));
        }
    }

    /// <summary>The body FBR's POS integration takes (as far as known; see the class remarks).</summary>
    public static object Body(FiscalInvoiceRequest r, string posId) => new
    {
        InvoiceNumber = "",
        POSID = long.TryParse(posId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0,
        USIN = $"{r.StoreCode}-{r.StationNumber:000}-{r.InvoiceNumber}",
        DateTime = r.SoldAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        BuyerNTN = (string?)null,
        BuyerCNIC = (string?)null,
        BuyerName = r.BuyerName,
        BuyerPhoneNumber = (string?)null,
        TotalBillAmount = Math.Abs(r.TotalBillAmount),
        TotalQuantity = Math.Abs(r.TotalQuantity),
        TotalSaleValue = Math.Abs(r.TotalSaleValue),
        TotalTaxCharged = Math.Abs(r.TotalTaxCharged),
        Discount = Math.Abs(r.Discount),
        FurtherTax = 0m,
        PaymentMode = (int)r.PaymentMode,
        RefUSIN = (string?)null,
        // 1 new sale, 3 credit (a return).
        InvoiceType = r.IsReturn ? 3 : 1,
        Items = r.Items.Select(i => new
        {
            ItemCode = i.ItemCode,
            ItemName = i.ItemName,
            Quantity = Math.Abs(i.Quantity),
            PCTCode = i.PctCode ?? "",
            TaxRate = i.TaxRate,
            SaleValue = Math.Abs(i.SaleValue),
            TotalAmount = Math.Abs(i.TotalAmount),
            TaxCharged = Math.Abs(i.TaxCharged),
            Discount = Math.Abs(i.Discount),
            FurtherTax = 0m,
            InvoiceType = r.IsReturn ? 3 : 1,
            RefUSIN = (string?)null,
        }).ToList(),
    };

    private static string Short(string text) => text.Length <= 200 ? text : text[..200];
}

/// <summary>
/// The finance jobs of the server, every <see cref="FinanceOptions.JobIntervalSeconds"/>: the business days touched by
/// packages applied since the last run are posted, the nightly posting of the last days, the recurring expenses that are
/// due, and the FBR queue (when it is on). Each job's failure is logged and the others go on.
/// </summary>
public sealed class FinanceJobs(DataContext data, IFiscalInvoiceSender fiscal, IOptions<FinanceOptions> options, ILogger<FinanceJobs> log) : BackgroundService
{
    private DateOnly? _recurringRanOn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seconds = options.Value.JobIntervalSeconds;
        if (seconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, seconds)));
        do
        {
            try
            {
                await RunOnceAsync(DateTime.Now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FinanceLog.JobFailed(log, "finance jobs", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run of every job (the tests call it directly).</summary>
    public async Task RunOnceAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var jobs = await ContextAsync(cancellationToken);
        if (jobs is null) return;
        await Step("posting synced days", async () =>
        {
            var result = await new LedgerAutomation(jobs).PostSyncedDaysAsync(cancellationToken);
            Report(result);
        });
        await Step("nightly posting", async () =>
        {
            if (await new LedgerAutomation(jobs).PostNightlyAsync(now, cancellationToken) is { } result) Report(result);
        });
        var today = DateOnly.FromDateTime(now);
        if (_recurringRanOn != today)
            await Step("recurring expenses", async () =>
            {
                var made = await new ExpenseService(jobs).RunDueAsync(today);
                if (made.Count > 0) FinanceLog.RecurringMade(log, made.Count);
                _recurringRanOn = today;
            });
        if (fiscal.Enabled)
            await Step("FBR queue", async () =>
            {
                var service = new FiscalService(jobs);
                await service.EnqueueAsync(fiscal, cancellationToken);
                await service.SendDueAsync(fiscal, cancellationToken: cancellationToken);
                await service.RestampAsync(cancellationToken);
            });
    }

    private void Report(AutoPostResult result)
    {
        if (result.Entries > 0) FinanceLog.DaysPosted(log, result.Entries);
        foreach (var problem in result.Problems) FinanceLog.JobFailed(log, "posting days", problem);
    }

    private async Task Step(string name, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is BusinessRuleException or DbUpdateException or InvalidOperationException or HttpRequestException)
        {
            FinanceLog.JobFailed(log, name, ex.Message);
        }
    }

    /// <summary>The jobs work as a user of their own, for the first store (the audit log needs one).</summary>
    private async Task<DataContext?> ContextAsync(CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        var store = await db.Stores.AsNoTracking().OrderBy(s => s.Code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
        return store is Guid id ? data.For(new StationIdentity(id, 0), new SessionInfo { UserName = "Finance jobs", StationNumber = 0 }) : null;
    }
}

/// <summary>The finance jobs' log messages.</summary>
internal static partial class FinanceLog
{
    [LoggerMessage(101, LogLevel.Information, "Posted {Count} business days to the ledger.")]
    public static partial void DaysPosted(ILogger logger, int count);

    [LoggerMessage(102, LogLevel.Information, "Made {Count} recurring expenses.")]
    public static partial void RecurringMade(ILogger logger, int count);

    [LoggerMessage(103, LogLevel.Warning, "Finance job {Job}: {Error}")]
    public static partial void JobFailed(ILogger logger, string job, string error);
}
