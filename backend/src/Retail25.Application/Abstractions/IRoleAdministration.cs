using Retail25.Domain.Common;

namespace Retail25.Application.Abstractions;

/// <summary>A role as the role editor needs to see it.</summary>
/// <param name="LegacyLevel">
/// The legacy access level this role was created for (guide p.82), or null for a role an
/// administrator made. The five built-in roles carry one, and it is what the presets are defined
/// against.
/// </param>
/// <param name="MemberCount">Everyone in the role, signed-in or not.</param>
public sealed record RoleRecord(long Id, string Name, string? Description, int? LegacyLevel, int MemberCount);

/// <summary>
/// Roles live in Identity, and Identity lives in the infrastructure layer. This is how a handler
/// creates, renames, removes and assigns them without naming a <c>RoleManager</c>.
/// <para>
/// Grants are deliberately not here. Which permissions a role holds is a row in
/// <c>role_permissions</c>, owned by the application, so the rules about changing it — what may be
/// granted, what must never be taken away — sit with the handlers rather than behind this port.
/// </para>
/// </summary>
public interface IRoleAdministration
{
    Task<IReadOnlyList<RoleRecord>> ListAsync(CancellationToken ct);

    Task<RoleRecord?> FindAsync(long roleId, CancellationToken ct);

    /// <summary>Fails with <c>role.name_taken</c> when another role already has the name.</summary>
    Task<Result<long>> CreateAsync(string name, string? description, CancellationToken ct);

    Task<Result> UpdateAsync(long roleId, string name, string? description, CancellationToken ct);

    /// <summary>Removes the role itself. The caller has already checked it is empty and not built in.</summary>
    Task<Result> DeleteAsync(long roleId, CancellationToken ct);

    /// <summary>The ids of the roles a user is in.</summary>
    Task<IReadOnlyList<long>> RolesOfAsync(long userId, CancellationToken ct);

    /// <summary>
    /// Puts the user in exactly this role, taking them out of any other. One role per person is
    /// what the screens show and what the presets assume; two would make "what can this person do"
    /// a union nobody can see.
    /// </summary>
    Task<Result> AssignAsync(long userId, long roleId, CancellationToken ct);

    /// <summary>
    /// How many people who can still sign in belong to any of these roles. Disabled accounts are not
    /// counted: the question is always "would anybody be left who can actually get in".
    /// </summary>
    Task<int> CountEnabledMembersAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct);

    /// <summary>
    /// As <see cref="CountEnabledMembersAsync"/>, but leaving one user out — "if this person moved
    /// role, who would remain".
    /// </summary>
    Task<int> CountEnabledMembersExceptAsync(IReadOnlyCollection<long> roleIds, long userId, CancellationToken ct);
}
