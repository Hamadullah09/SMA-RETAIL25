using System.Text.Json;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Rfid;

// SMA RETAIL RFID hub: owns the connections to the RFID readers (any number, through an Ethernet switch), reads the
// antennas assigned to POS stations, and streams each station its tag reads. Run it on the PC the readers are
// connected to; install it as a Windows service with
//   sc create SmaRetailRfidHub binPath= "C:\path\SmaRetail.RfidHub.exe --connection \"...\"" start= auto
//
// Options: --connection <sql connection string> | --localdb <database>   (default LocalDB RetailPlus_App)
//          --hub <key>        readers whose hub key matches (default "main"); split big sites over several hubs
//          --urls <url>       where to listen (default http://0.0.0.0:5280)
//          --apikey <secret>  require this key from stations (header X-SMA-Key)

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "SmaRetailRfidHub");
var config = builder.Configuration;
string connection = config.GetConnectionString("RetailPlus") ?? config["connection"]
                    ?? RetailPlusDb.LocalDbConnectionString(config["localdb"] ?? "RetailPlus_App");
string hubKey = config["hub"] ?? "main";
string? apiKey = config["apikey"];
builder.WebHost.UseUrls(config["urls"] ?? "http://0.0.0.0:5280");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var data = new DataContext(connection);
var rfid = new RfidService(data);
builder.Services.AddSingleton(data);
builder.Services.AddSingleton(rfid);
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RfidHub");
    return new RfidHub(async ct => await rfid.GetReadersAsync(hubKey, ct)) { Log = m => HubLog.Problem(log, m) };
});
builder.Services.AddHostedService<HubLifetime>();

var app = builder.Build();

if (!string.IsNullOrEmpty(apiKey))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path != "/health" && context.Request.Headers["X-SMA-Key"] != apiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("The RFID hub key is missing or wrong.");
            return;
        }
        await next();
    });
}

app.MapGet("/health", async (RfidHub hub) =>
{
    var readers = await hub.GetStatusAsync();
    return Results.Ok(new { status = "ok", hub = hubKey, readers = readers.Count, connected = readers.Count(r => r.Connected) });
});

app.MapGet("/readers", async (RfidHub hub, CancellationToken ct) => Results.Ok(await hub.GetStatusAsync(ct)));

app.MapPost("/reload", async (RfidHub hub, CancellationToken ct) =>
{
    await hub.ReloadAsync(ct);
    return Results.Ok();
});

app.MapGet("/readers/{id:int}/info", (int id, RfidHub hub, CancellationToken ct) => Run(async () => Results.Ok(await hub.ReadInfoAsync(id, ct))));

app.MapPost("/readers/{id:int}/apply", (int id, RfidHub hub, CancellationToken ct) => Run(async () =>
{
    var problems = await hub.ApplySettingsAsync(id, ct);
    var firmware = (await hub.GetStatusAsync(ct)).FirstOrDefault(r => r.ReaderId == id)?.Firmware;
    await rfid.MarkAppliedAsync(id, firmware);
    return Results.Ok(problems);
}));

app.MapPost("/readers/{id:int}/reset", (int id, RfidHub hub, CancellationToken ct) => Run(async () =>
{
    await hub.ResetAsync(id, ct);
    return Results.Ok();
}));

app.MapPost("/readers/{id:int}/return-loss", (int id, int antenna, int frequency, RfidHub hub, CancellationToken ct) =>
    Run(async () => Results.Ok(await hub.MeasureReturnLossAsync(id, antenna, frequency, ct))));

app.MapPost("/readers/{id:int}/gpio", (int id, int gpio, bool high, RfidHub hub, CancellationToken ct) => Run(async () =>
{
    await hub.WriteGpioAsync(id, gpio, high, ct);
    return Results.Ok();
}));

// Server-sent events: one "data:" line of JSON per tag read, and a comment every 15 seconds so idle connections stay open.
app.MapGet("/stations/{station:int}/events", async (int station, HttpContext context, RfidHub hub) =>
{
    var ct = context.RequestAborted;
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var write = new SemaphoreSlim(1, 1);
    async Task Send(string text)
    {
        await write.WaitAsync(ct);
        try
        {
            await context.Response.WriteAsync(text, ct);
            await context.Response.Body.FlushAsync(ct);
        }
        finally
        {
            write.Release();
        }
    }
    await Send($": station {station}\n\n");
    using var keepAlive = new PeriodicTimer(TimeSpan.FromSeconds(15));
    var pinger = Task.Run(async () =>
    {
        try
        {
            while (await keepAlive.WaitForNextTickAsync(ct)) await Send(": ping\n\n");
        }
        catch (OperationCanceledException)
        {
        }
    }, CancellationToken.None);
    try
    {
        await foreach (var e in hub.ListenAsync(station, ct))
            await Send("data: " + JsonSerializer.Serialize(e, json) + "\n\n");
    }
    catch (OperationCanceledException)
    {
    }
    await pinger;
});

app.Run();

static async Task<IResult> Run(Func<Task<IResult>> action)
{
    try
    {
        return await action();
    }
    catch (RfidException ex)
    {
        return Results.Text(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (BusinessRuleException ex)
    {
        return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }
}

/// <summary>Starts the hub with the service and re-reads the reader list every minute (and on POST /reload).</summary>
internal sealed class HubLifetime(RfidHub hub, ILogger<HubLifetime> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await hub.StartAsync(stoppingToken);
        var readers = await hub.GetStatusAsync(stoppingToken);
        HubLog.Started(log, readers.Count);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await hub.ReloadAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    HubLog.ReloadFailed(log, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        await hub.StopAsync();
    }
}

internal static partial class HubLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void Problem(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "RFID hub started with {Count} reader(s)")]
    public static partial void Started(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reloading the reader list failed")]
    public static partial void ReloadFailed(ILogger logger, Exception exception);
}
