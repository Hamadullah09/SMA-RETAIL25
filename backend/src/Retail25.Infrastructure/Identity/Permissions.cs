using Retail25.Application.Common;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// A seeding helper only. The catalogue itself lives in <see cref="PermissionKeys"/> in the
/// Application layer, next to the requests that declare permissions — a duplicate list here would
/// drift the first time one was added.
/// </summary>
public static class Permissions
{
    public static IReadOnlyList<string> AllPermissions => PermissionKeys.All;

    public static IReadOnlyDictionary<int, IReadOnlyList<string>> LegacyLevelPresets => PermissionKeys.LegacyLevelPresets;
}
