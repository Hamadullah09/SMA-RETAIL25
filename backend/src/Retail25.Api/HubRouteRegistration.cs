using Retail25.Api.Hubs;

namespace Retail25.Api;

/// <summary>
/// Maps the real-time hubs onto their routes.
/// <para>
/// Kept as one place rather than three loose <c>MapHub</c> calls in <c>Program.cs</c> so the paths
/// the terminal agent and the browser connect to are stated once. These strings are a published
/// contract: changing one breaks every till in the field until its agent is updated.
/// </para>
/// </summary>
public static class HubRouteRegistration
{
    /// <summary>Browser till: cart, totals, tag-stream status.</summary>
    public const string PosPath = "/hubs/pos";

    /// <summary>Browse grids: live stock and catalog changes.</summary>
    public const string InventoryPath = "/hubs/inventory";

    /// <summary>Terminal agent: tag reads, peripherals, print and drawer commands.</summary>
    public const string TerminalPath = "/hubs/terminal";

    public static IEndpointRouteBuilder MapRetail25Hubs(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHub<PosHub>(PosPath);
        endpoints.MapHub<InventoryHub>(InventoryPath);
        endpoints.MapHub<TerminalHub>(TerminalPath);

        return endpoints;
    }
}
