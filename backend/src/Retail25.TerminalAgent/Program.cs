using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The peripheral host (RFID/LLRP, weigh scale, cash drawer, pole display, ESC/POS printing)
// is assembled in Phase 4. Phase 0 establishes a running, service-installable host so that
// deployment packaging can be built and tested from the start.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTerminalAgent();

var host = builder.Build();
await host.RunAsync();

internal static class TerminalAgentRegistration
{
    /// <summary>
    /// Composition root for the terminal agent. Device drivers are registered here in Phase 4
    /// and are always resolved from server-supplied profiles, never from constants in code.
    /// </summary>
    public static IServiceCollection AddTerminalAgent(this IServiceCollection services)
        => services;
}
