using System.Text.Json;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Rfid;

// SMA RETAIL RFID hub: owns the connections to the RFID readers (any number, through an Ethernet switch), reads the
// antennas assigned to POS stations, and streams each station its tag reads. Run it on the PC the readers are
// connected to; install it as a Windows service with
//   sc create SmaRetailRfidHub binPath= "C:\path\SmaRetail.RfidHub.exe --db \"C:\...\smaretail.db\"" start= auto
//
// Where the readers come from, one of:
//          --db <SQLite file> | --connection <SQL Server connection string> | --localdb <database>
//                             (default: smaretail.db in %LOCALAPPDATA%\RetailPlus of the account the hub runs as)
//          --server <url> --serverkey <key>
//                             the SMA RETAIL server, with the RFID hub key made for the store (no database needed)
//          --savedlist <file> with --server: where the last reader list is kept for a start without the server
//                             (default %LOCALAPPDATA%\RetailPlus\RfidHub\readers.json)
// Options: --hub <key>        readers whose hub key matches (default "main"); split big sites over several hubs
//          --poll <seconds>   how often the reader list is read again (default 60)
//          --urls <url>       where to listen (default http://0.0.0.0:5280)
//          --apikey <secret>  require this key from stations (header X-SMA-Key)
// Every option can also be set in appsettings.json next to the program (for example "serverkey"), which keeps the key
// off the service's command line.

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "SmaRetailRfidHub");
var config = builder.Configuration;
string hubKey = config["hub"] ?? "main";
string? apiKey = config["apikey"];
var poll = TimeSpan.FromSeconds(Math.Clamp(int.TryParse(config["poll"], out var seconds) ? seconds : 60, 5, 3600));
builder.WebHost.UseUrls(config["urls"] ?? "http://0.0.0.0:5280");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

// The reader list comes from the server, or from a database as before. With a database the hub also records there when
// settings were sent to a reader; from the server, the station that sent them records it in its own database.
RfidServerReaders? server = null;
RfidService? rfid = null;
Func<CancellationToken, Task<IReadOnlyList<RfidReader>>> loadReaders;
if (config["server"] is { Length: > 0 } serverUrl)
{
    if (new[] { config["db"], config["connection"], config["localdb"], config.GetConnectionString("RetailPlus") }.Any(v => !string.IsNullOrWhiteSpace(v)))
        throw new InvalidOperationException("Give the RFID hub either --server or a database (--db, --connection or --localdb), not both.");
    if (config["serverkey"] is not { Length: > 0 } serverKey)
        throw new InvalidOperationException("--server needs --serverkey: the RFID hub key made for the store on the server.");
    server = new RfidServerReaders(serverUrl, serverKey, hubKey, config["savedlist"] ?? RfidServerReaders.DefaultSavedListFile);
    loadReaders = server.LoadAsync;
}
else
{
    var database = RetailPlusDb.FromOptions(config["db"], config.GetConnectionString("RetailPlus") ?? config["connection"], config["localdb"]);
    var data = new DataContext(database);
    var service = rfid = new RfidService(data);
    builder.Services.AddSingleton(data);
    builder.Services.AddSingleton(service);
    loadReaders = async ct => await service.GetReadersAsync(hubKey, ct);
}
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RfidHub");
    if (server is not null) server.Log = m => HubLog.Problem(log, m);
    return new RfidHub(loadReaders) { Log = m => HubLog.Problem(log, m) };
});
builder.Services.AddHostedService(sp => new HubLifetime(sp.GetRequiredService<RfidHub>(), poll, server?.BaseAddress, sp.GetRequiredService<ILogger<HubLifetime>>()));

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
    return Results.Ok(new
    {
        status = "ok", hub = hubKey, source = server is null ? "database" : "server", store = server?.StoreName, loaded = hub.Loaded,
        readers = readers.Count, connected = readers.Count(r => r.Connected),
    });
});

app.MapGet("/readers", async (RfidHub hub, CancellationToken ct) => Results.Ok(await hub.GetStatusAsync(ct)));

app.MapPost("/reload", async (RfidHub hub, CancellationToken ct) =>
{
    await hub.ReloadAsync(ct);
    return Results.Ok();
});

app.MapGet("/readers/{id:guid}/info", (Guid id, RfidHub hub, CancellationToken ct) => Run(async () => Results.Ok(await hub.ReadInfoAsync(id, ct))));

app.MapPost("/readers/{id:guid}/apply", (Guid id, RfidHub hub, CancellationToken ct) => Run(async () =>
{
    var problems = await hub.ApplySettingsAsync(id, ct);
    if (rfid is not null)
    {
        var firmware = (await hub.GetStatusAsync(ct)).FirstOrDefault(r => r.ReaderId == id)?.Firmware;
        await rfid.MarkAppliedAsync(id, firmware);
    }
    return Results.Ok(problems);
}));

app.MapPost("/readers/{id:guid}/reset", (Guid id, RfidHub hub, CancellationToken ct) => Run(async () =>
{
    await hub.ResetAsync(id, ct);
    return Results.Ok();
}));

app.MapPost("/readers/{id:guid}/return-loss", (Guid id, int antenna, int frequency, RfidHub hub, CancellationToken ct) =>
    Run(async () => Results.Ok(await hub.MeasureReturnLossAsync(id, antenna, frequency, ct))));

app.MapPost("/readers/{id:guid}/gpio", (Guid id, int gpio, bool high, RfidHub hub, CancellationToken ct) => Run(async () =>
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

// WebSocket: the same tag reads as text messages (one JSON object each), for browsers and other programs.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.Map("/stations/{station:int}/ws", async (int station, HttpContext context, RfidHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect with a WebSocket client.");
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var done = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    // Watch for the client closing, so the antennas stop being read for it.
    var receiving = Task.Run(async () =>
    {
        var buffer = new byte[256];
        try
        {
            while (socket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var r = await socket.ReceiveAsync(buffer, done.Token);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException)
        {
        }
        await done.CancelAsync();
    }, CancellationToken.None);
    try
    {
        await foreach (var e in hub.ListenAsync(station, done.Token))
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(e, json), System.Net.WebSockets.WebSocketMessageType.Text, true, done.Token);
    }
    catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException)
    {
    }
    await receiving;
    // Answer the client's close (CloseReceived), or close ourselves when the hub is the one stopping.
    if (socket.State is System.Net.WebSockets.WebSocketState.Open or System.Net.WebSockets.WebSocketState.CloseReceived)
    {
        try
        {
            await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (System.Net.WebSockets.WebSocketException)
        {
        }
    }
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

/// <summary>
/// Starts the hub with the service and re-reads the reader list every <c>--poll</c> seconds (and on POST /reload). A hub
/// that cannot read its list at start (the server or database not reachable) starts anyway and keeps trying.
/// </summary>
internal sealed class HubLifetime(RfidHub hub, TimeSpan poll, Uri? server, ILogger<HubLifetime> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (server is not null) HubLog.FromServer(log, server);
        try
        {
            await hub.StartAsync(stoppingToken);
            var readers = await hub.GetStatusAsync(stoppingToken);
            HubLog.Started(log, readers.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HubLog.ReloadFailed(log, ex);
        }
        await hub.KeepCurrentAsync(poll, ex => HubLog.ReloadFailed(log, ex), stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The reader list comes from the server {Server}")]
    public static partial void FromServer(ILogger logger, Uri server);
}
