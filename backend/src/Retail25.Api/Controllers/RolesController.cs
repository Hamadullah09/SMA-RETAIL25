using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail25.Api.Common;
using Retail25.Application.Roles;

namespace Retail25.Api.Controllers;

/// <summary>
/// Roles and what each may do (doc 07 §Authorization model).
/// <para>
/// Authorisation has always been by permission and roles have always been rows; until now nothing
/// could edit them, so the five legacy presets were the only arrangement any shop could have.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController : ControllerBase
{
    private readonly ISender _sender;

    public RolesController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _sender.Send(new ListRolesQuery(), ct));

    /// <summary>Everything a role could be allowed to do, grouped as the editor shows it.</summary>
    [HttpGet("permissions")]
    public async Task<IActionResult> Permissions(CancellationToken ct)
        => Ok(await _sender.Send(new ListPermissionCatalogueQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
        => (await _sender.Send(new CreateRoleCommand(request.Name, request.Description, request.CopyFromRoleId), ct))
            .ToActionResult(this);

    [HttpPut("{roleId:long}")]
    public async Task<IActionResult> Update(long roleId, [FromBody] UpdateRoleRequest request, CancellationToken ct)
        => (await _sender.Send(new UpdateRoleCommand(roleId, request.Name, request.Description), ct))
            .ToActionResult(this);

    [HttpPut("{roleId:long}/permissions")]
    public async Task<IActionResult> SetPermissions(long roleId, [FromBody] SetRolePermissionsRequest request, CancellationToken ct)
        => (await _sender.Send(new SetRolePermissionsCommand(roleId, request.Permissions ?? []), ct))
            .ToActionResult(this);

    [HttpDelete("{roleId:long}")]
    public async Task<IActionResult> Delete(long roleId, CancellationToken ct)
        => (await _sender.Send(new DeleteRoleCommand(roleId), ct)).ToActionResult(this);

    /// <summary>Moves a member of staff into a role.</summary>
    [HttpPut("members/{staffId:long}")]
    public async Task<IActionResult> Assign(long staffId, [FromBody] AssignRoleRequest request, CancellationToken ct)
        => (await _sender.Send(new AssignStaffRoleCommand(staffId, request.RoleId), ct)).ToActionResult(this);
}

public sealed record CreateRoleRequest(string Name, string? Description, long? CopyFromRoleId);

public sealed record UpdateRoleRequest(string Name, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<string>? Permissions);

public sealed record AssignRoleRequest(long RoleId);
