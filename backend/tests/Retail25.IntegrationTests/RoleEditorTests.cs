using System.Globalization;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Common;
using Retail25.Application.Roles;
using Retail25.Application.Staff;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The role editor against real Identity on real SQL Server.
/// <para>
/// The rules — what may be granted, what may never be removed — are pinned by the application tests
/// against a fake. This is the other half: that roles are actually created, renamed, filled and
/// emptied in Identity's own tables, and that Identity's validator speaks through this application's
/// error codes rather than its own.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class RoleEditorTests
{
    private readonly CommerceApiFixture _api;

    public RoleEditorTests(CommerceApiFixture api) => _api = api;

    [RequiresDockerFact]
    public async Task A_role_can_be_made_from_another_edited_given_to_somebody_and_removed()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roles = await sender.Send(new ListRolesQuery());
        var supervisor = roles.Single(r => r.LegacyLevel == 3);
        var cashier = roles.Single(r => r.LegacyLevel == 1);

        roles.Where(r => r.LegacyLevel is not null).Should().OnlyContain(r => r.IsBuiltIn);

        var tag = Guid.NewGuid().ToString("N")[..8];
        var name = $"Floor lead {tag}";

        var created = await sender.Send(new CreateRoleCommand(name, "Supervisor, no voids", supervisor.Id));
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : null);
        created.Value.IsBuiltIn.Should().BeFalse();
        created.Value.Permissions.Should().BeEquivalentTo(supervisor.Permissions);

        // Identity's own validator refuses the name, and the refusal arrives in this application's code.
        var duplicate = await sender.Send(new CreateRoleCommand(name, null));
        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Code.Should().Be("role.name_taken");

        var withoutVoids = created.Value.Permissions.Where(p => p != PermissionKeys.Pos.VoidSale).ToList();
        var edited = await sender.Send(new SetRolePermissionsCommand(created.Value.Id, withoutVoids));
        edited.IsSuccess.Should().BeTrue(edited.IsFailure ? edited.Error.Code : null);
        edited.Value.Permissions.Should().NotContain(PermissionKeys.Pos.VoidSale);

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var colleague = await sender.Send(new CreateStaffCommand(
            $"role-{tag}@retail25.test",
            "Role",
            "Tester",
            $"R{tag[..5]}".ToUpperInvariant(),
            $"Integration!{tag}",
            cashier.Name,
            AccessLevel: 1,
            LocationId: location.Id));
        colleague.IsSuccess.Should().BeTrue(colleague.IsFailure ? colleague.Error.Message : null);

        (await sender.Send(new AssignStaffRoleCommand(colleague.Value.Id, created.Value.Id))).IsSuccess.Should().BeTrue();

        var profile = await db.StaffProfiles.AsNoTracking().SingleAsync(s => s.Id == colleague.Value.Id);
        var user = await users.FindByIdAsync(profile.UserId.ToString(CultureInfo.InvariantCulture));
        (await users.GetRolesAsync(user!)).Should().Equal(name);

        // A custom role carries no legacy level, so the colleague's level is left as it was.
        profile.AccessLevel.Should().Be(1);

        (await sender.Send(new DeleteRoleCommand(created.Value.Id))).Error.Code.Should().Be("role.has_members");

        (await sender.Send(new AssignStaffRoleCommand(colleague.Value.Id, cashier.Id))).IsSuccess.Should().BeTrue();
        (await users.GetRolesAsync(user!)).Should().Equal(cashier.Name);

        (await sender.Send(new DeleteRoleCommand(created.Value.Id))).IsSuccess.Should().BeTrue();
        (await sender.Send(new ListRolesQuery())).Should().NotContain(r => r.Id == created.Value.Id);
        (await db.RolePermissions.AsNoTracking().AnyAsync(rp => rp.RoleId == created.Value.Id)).Should().BeFalse();
    }

    [RequiresDockerFact]
    public async Task A_built_in_role_keeps_its_name()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var clerk = (await sender.Send(new ListRolesQuery())).Single(r => r.LegacyLevel == 2);

        var renamed = await sender.Send(new UpdateRoleCommand(clerk.Id, "Shop floor", clerk.Description));

        renamed.Error.Code.Should().Be("role.builtin_name");
        (await sender.Send(new ListRolesQuery())).Single(r => r.Id == clerk.Id).Name.Should().Be(clerk.Name);
    }
}
