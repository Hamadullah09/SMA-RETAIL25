namespace Retail25.Application.Abstractions;

/// <summary>
/// Resolved from the HTTP context / SignalR hub by the infrastructure layer. Provides the
/// current authenticated user's identity and station context to application handlers.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's Id (ASP.NET Core Identity).</summary>
    Guid? UserId { get; }

    /// <summary>The staff profile Id, if the user has one.</summary>
    Guid? StaffId { get; }

    /// <summary>The station this request originated from (POS machines only).</summary>
    Guid? StationId { get; }

    /// <summary>The location the user is currently working at.</summary>
    Guid? LocationId { get; }

    bool IsAuthenticated { get; }

    /// <summary>The permission set granted to the current user.</summary>
    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permission) => Permissions.Contains(permission);
}
