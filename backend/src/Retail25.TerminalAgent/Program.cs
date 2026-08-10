// Sdk.Worker does not bring the ASP.NET Core implicit usings, and the loopback API needs them.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail25.TerminalAgent;
using Retail25.TerminalAgent.LocalApi;
using Retail25.TerminalAgent.Peripherals;
using Retail25.TerminalAgent.Rfid;
using Retail25.TerminalAgent.Server;
using Retail25.TerminalAgent.Spooling;
using Serilog;

// One process per POS machine, owning every peripheral (doc 06). Browsers cannot open LLRP sockets,
// COM ports or cash drawers, so all of that lives here — speaking only SignalR to the server plus a
// loopback API to the browser on the same machine.
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.File(
        Path.Combine(AgentPaths.DataDirectory, "logs", "agent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var agentOptions = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();

// Loopback only. Binding anywhere else would put a till's hardware on the shop network.
builder.WebHost.UseUrls(agentOptions.LocalApiUrl);

builder.Services.AddSingleton<AgentTokenProvider>();
builder.Services.AddTransient<AgentAuthHandler>();

builder.Services.AddHttpClient("server", client =>
{
    client.BaseAddress = new Uri(agentOptions.ApiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
})
    // The secret is exchanged for a real token rather than sent as one. Sending it directly is what
    // the agent used to do, and OpenIddict refused every call — silently, because the agent's answer
    // to a failed profile fetch is to keep its defaults and carry on.
    .AddHttpMessageHandler<AgentAuthHandler>();

builder.Services.AddSingleton<TagBuffer>();
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<ITagSpool, SqliteTagSpool>();
builder.Services.AddSingleton<IServerConnection, SignalRServerConnection>();

builder.Services.AddSingleton<IDeviceFactory>(provider => new DeviceFactory(
    provider.GetRequiredService<ILoggerFactory>(),
    provider.GetRequiredService<IOptions<AgentOptions>>().Value.DisablePeripherals));

builder.Services.AddSingleton<PeripheralCoordinator>();

// The reader service is reached by two other services, so it is registered as a singleton and then
// handed to the host — otherwise the host would own a second instance nobody can talk to.
builder.Services.AddSingleton<RfidReaderService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RfidReaderService>());
builder.Services.AddHostedService<TagFlushService>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddHostedService<ProfileRefreshService>();
builder.Services.AddHostedService<AgentStartupService>();

// No-ops when not actually running as a service, so the same binary works on a bench.
builder.Services.AddWindowsService(options => options.ServiceName = "Retail25 Terminal Agent");

// The till's own browser has to be allowed to call this.
//
// The loopback bind and the LoopbackOnlyFilter already mean nothing off this machine can reach the
// agent. That is not enough on its own: a page served from localhost:3000 calling 127.0.0.1:8477 is
// cross-origin as far as the browser is concerned, and without this header the hardware panel shows
// "the agent is not answering" while the agent answers perfectly well from the command line.
//
// Named origins rather than AllowAnyOrigin. Any page the user has open — including one on the public
// internet — can attempt a request to 127.0.0.1; the loopback filter cannot tell those apart, because
// they genuinely do come from this machine. The origin check is what does.
// The 127.0.0.1 variant is derived, not listed: whichever port the web app is served on, the same
// page reached via 127.0.0.1 instead of localhost is a different origin to the browser and must be
// allowed alongside it.
var agentWebOrigin = builder.Configuration["Agent:WebOrigin"] ?? "http://localhost:3000";

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(
        agentWebOrigin,
        agentWebOrigin.Replace("//localhost", "//127.0.0.1", StringComparison.OrdinalIgnoreCase))
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapLocalApi();

await app.RunAsync();

/// <summary>Exposed so the agent's tests can reference the host assembly.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
