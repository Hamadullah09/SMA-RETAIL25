using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Application.Roles;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Common;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;
using Xunit;

namespace Retail25.Application.UnitTests.Roles;

/// <summary>
/// The role editor's rules. The screen is only a page of ticks; what keeps a shop from breaking its
/// own access with it is here.
/// </summary>
public sealed class RoleCommandTests
{
    private const string Manage = PermissionKeys.System.UsersManage;
    private const string Sell = PermissionKeys.Pos.Sell;
    private const string Void = PermissionKeys.Pos.VoidSale;

    [Fact]
    public async Task Saving_a_roles_permissions_replaces_them_exactly()
    {
        using var world = await World.CreateAsync();
        var clerk = world.Roles.Add("Clerk", level: 2);
        await world.GrantAsync(clerk, Sell, Void);
        world.Roles.AddMember(clerk, enabled: true);
        world.KeepAnAdministrator();

        var result = await world.Handlers.Handle(new SetRolePermissionsCommand(clerk, [Sell, PermissionKeys.Pos.Suspend]), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEquivalentTo([Sell, PermissionKeys.Pos.Suspend]);
        (await world.GrantsOfAsync(clerk)).Should().BeEquivalentTo([Sell, PermissionKeys.Pos.Suspend]);
    }

    /// <summary>A revocation that waits out a cache is a revocation that did not work.</summary>
    [Fact]
    public async Task Changing_a_roles_permissions_takes_effect_for_everyone_at_once()
    {
        using var world = await World.CreateAsync();
        var clerk = world.Roles.Add("Clerk", level: 2);
        world.KeepAnAdministrator();

        await world.Handlers.Handle(new SetRolePermissionsCommand(clerk, [Sell]), default);

        await world.Resolver.Received(1).InvalidateAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_permission_the_system_does_not_have_cannot_be_granted()
    {
        using var world = await World.CreateAsync();
        var clerk = world.Roles.Add("Clerk", level: 2);
        world.KeepAnAdministrator();

        var result = await world.Handlers.Handle(new SetRolePermissionsCommand(clerk, [Sell, "pos.teleport"]), default);

        result.Error.Code.Should().Be("role.unknown_permission");
        (await world.GrantsOfAsync(clerk)).Should().BeEmpty();
    }

    /// <summary>
    /// The one change this screen cannot undo from this screen: leaving nobody able to manage users.
    /// Whoever holds that permission is the only person who could put a mistake here right.
    /// </summary>
    [Fact]
    public async Task The_last_way_into_user_management_cannot_be_removed()
    {
        using var world = await World.CreateAsync();
        var admin = world.Roles.Add("Administrator", level: 4);
        await world.GrantAsync(admin, Manage, Sell);
        world.Roles.AddMember(admin, enabled: true);

        var result = await world.Handlers.Handle(new SetRolePermissionsCommand(admin, [Sell]), default);

        result.Error.Code.Should().Be("role.lockout");
        (await world.GrantsOfAsync(admin)).Should().Contain(Manage);
    }

    /// <summary>
    /// The lock-out rule guards removal and nothing else. The first version asked "would anybody be
    /// left" of every save, so where nobody active managed users already — a fresh install, a test
    /// bench — every edit was refused, including the one that would have fixed it. The integration
    /// suite caught it against a real database.
    /// </summary>
    [Fact]
    public async Task An_edit_that_removes_no_user_management_is_never_a_lock_out()
    {
        using var world = await World.CreateAsync();
        var clerk = world.Roles.Add("Clerk", level: 2);

        (await world.Handlers.Handle(new SetRolePermissionsCommand(clerk, [Sell]), default)).IsSuccess.Should().BeTrue();

        // And granting it is how a shop that has nobody able to manage users gets somebody back.
        (await world.Handlers.Handle(new SetRolePermissionsCommand(clerk, [Sell, Manage]), default)).IsSuccess.Should().BeTrue();
    }

    /// <summary>A disabled administrator cannot sign in, so they are no way back in.</summary>
    [Fact]
    public async Task A_disabled_member_does_not_count_as_a_way_back_in()
    {
        using var world = await World.CreateAsync();
        var admin = world.Roles.Add("Administrator", level: 4);
        await world.GrantAsync(admin, Manage);
        world.Roles.AddMember(admin, enabled: true);

        var owners = world.Roles.Add("Owners", level: null);
        await world.GrantAsync(owners, Manage);
        world.Roles.AddMember(owners, enabled: false);

        var result = await world.Handlers.Handle(new SetRolePermissionsCommand(admin, [Sell]), default);

        result.Error.Code.Should().Be("role.lockout");
    }

    [Fact]
    public async Task User_management_can_move_to_another_role_somebody_active_is_in()
    {
        using var world = await World.CreateAsync();
        var admin = world.Roles.Add("Administrator", level: 4);
        await world.GrantAsync(admin, Manage);
        world.Roles.AddMember(admin, enabled: true);

        var owners = world.Roles.Add("Owners", level: null);
        await world.GrantAsync(owners, Manage);
        world.Roles.AddMember(owners, enabled: true);

        var result = await world.Handlers.Handle(new SetRolePermissionsCommand(admin, [Sell]), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_role_can_start_from_another_roles_permissions()
    {
        using var world = await World.CreateAsync();
        var supervisor = world.Roles.Add("Supervisor", level: 3);
        await world.GrantAsync(supervisor, Sell, Void);

        var result = await world.Handlers.Handle(new CreateRoleCommand("Floor lead", "Supervisor, no voids", supervisor), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsBuiltIn.Should().BeFalse();
        result.Value.Permissions.Should().BeEquivalentTo([Sell, Void]);
    }

    [Fact]
    public async Task A_built_in_role_keeps_its_name_but_not_its_description()
    {
        using var world = await World.CreateAsync();
        var cashier = world.Roles.Add("Cashier", level: 1);

        var renamed = await world.Handlers.Handle(new UpdateRoleCommand(cashier, "Till staff", null), default);
        var described = await world.Handlers.Handle(new UpdateRoleCommand(cashier, "Cashier", "Front counter"), default);

        renamed.Error.Code.Should().Be("role.builtin_name");
        described.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_deleted()
    {
        using var world = await World.CreateAsync();
        var trainee = world.Roles.Add("Trainee", level: 0);

        var result = await world.Handlers.Handle(new DeleteRoleCommand(trainee), default);

        result.Error.Code.Should().Be("role.builtin_delete");
    }

    [Fact]
    public async Task A_role_with_people_in_it_cannot_be_deleted_and_an_empty_one_takes_its_grants_with_it()
    {
        using var world = await World.CreateAsync();
        var busy = world.Roles.Add("Weekend", level: null);
        world.Roles.AddMember(busy, enabled: true);

        var empty = world.Roles.Add("Old role", level: null);
        await world.GrantAsync(empty, Sell);

        (await world.Handlers.Handle(new DeleteRoleCommand(busy), default)).Error.Code.Should().Be("role.has_members");

        (await world.Handlers.Handle(new DeleteRoleCommand(empty), default)).IsSuccess.Should().BeTrue();
        (await world.GrantsOfAsync(empty)).Should().BeEmpty();
        world.Roles.Exists(empty).Should().BeFalse();
    }

    /// <summary>
    /// Moving the last active administrator into a lesser role is the same lock-out by another route.
    /// </summary>
    [Fact]
    public async Task The_last_active_administrator_cannot_be_moved_out_of_user_management()
    {
        using var world = await World.CreateAsync();
        var admin = world.Roles.Add("Administrator", level: 4);
        await world.GrantAsync(admin, Manage);
        var clerk = world.Roles.Add("Clerk", level: 2);

        var (staff, userId) = await world.StaffInAsync(admin);

        var result = await world.Handlers.Handle(new AssignStaffRoleCommand(staff, clerk), default);

        result.Error.Code.Should().Be("role.lockout");
        world.Roles.RolesOf(userId).Should().Equal(admin);
    }

    /// <summary>
    /// Level 0 is training mode, where nothing is committed. Moving somebody into Trainee without
    /// moving their level would give them a trainee's permissions on a till that records their sales.
    /// </summary>
    [Fact]
    public async Task Moving_into_a_built_in_role_brings_the_access_level_with_it()
    {
        using var world = await World.CreateAsync();
        world.KeepAnAdministrator();
        var cashier = world.Roles.Add("Cashier", level: 1);
        var trainee = world.Roles.Add("Trainee", level: 0);

        var (staff, userId) = await world.StaffInAsync(cashier, accessLevel: 1);

        var result = await world.Handlers.Handle(new AssignStaffRoleCommand(staff, trainee), default);

        result.IsSuccess.Should().BeTrue();
        world.Roles.RolesOf(userId).Should().Equal(trainee);
        (await world.Harness.Db.StaffProfiles.SingleAsync(s => s.Id == staff)).AccessLevel.Should().Be(0);
        await world.Resolver.Received(1).InvalidateAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_custom_role_leaves_the_access_level_alone()
    {
        using var world = await World.CreateAsync();
        world.KeepAnAdministrator();
        var clerk = world.Roles.Add("Clerk", level: 2);
        var weekend = world.Roles.Add("Weekend", level: null);

        var (staff, _) = await world.StaffInAsync(clerk, accessLevel: 2);

        (await world.Handlers.Handle(new AssignStaffRoleCommand(staff, weekend), default)).IsSuccess.Should().BeTrue();
        (await world.Harness.Db.StaffProfiles.SingleAsync(s => s.Id == staff)).AccessLevel.Should().Be(2);
    }

    /// <summary>The handlers over a real (in-memory) database and a fake of Identity's roles.</summary>
    private sealed class World : IDisposable
    {
        private World(PosTestHarness harness)
        {
            Harness = harness;
            Roles = new FakeRoles();
            Resolver = Substitute.For<IPermissionResolver>();
            Handlers = new RoleHandlers(harness.Db, Roles, Resolver, Substitute.For<IAuditWriter>());
        }

        public PosTestHarness Harness { get; }

        public FakeRoles Roles { get; }

        public IPermissionResolver Resolver { get; }

        public RoleHandlers Handlers { get; }

        public static async Task<World> CreateAsync()
        {
            var harness = await PosTestHarness.CreateAsync();

            foreach (var key in PermissionKeys.All.Where(k => !harness.Db.Permissions.Any(p => p.Key == k)))
            {
                harness.Db.Permissions.Add(Permission.Create(key, key, key.Split('.')[0]));
            }

            await harness.Db.SaveChangesAsync();
            return new World(harness);
        }

        /// <summary>Somebody active elsewhere who can manage users, so the test in hand is not a lock-out case.</summary>
        public void KeepAnAdministrator()
        {
            var owners = Roles.Add("Owners", level: null);
            Harness.Db.RolePermissions.Add(RolePermission.Create(owners, Manage));
            Harness.Db.SaveChanges();
            Roles.AddMember(owners, enabled: true);
        }

        public async Task GrantAsync(long roleId, params string[] keys)
        {
            foreach (var key in keys)
            {
                Harness.Db.RolePermissions.Add(RolePermission.Create(roleId, key));
            }

            await Harness.Db.SaveChangesAsync();
        }

        public async Task<List<string>> GrantsOfAsync(long roleId)
            => await Harness.Db.RolePermissions.Where(rp => rp.RoleId == roleId).Select(rp => rp.PermissionKey).ToListAsync();

        public async Task<(long StaffId, long UserId)> StaffInAsync(long roleId, int accessLevel = 4)
        {
            var userId = Roles.AddMember(roleId, enabled: true);
            var staff = StaffProfile.Create(userId, $"S{userId % 10000}", "Test", "Person", accessLevel);
            Harness.Db.StaffProfiles.Add(staff);
            await Harness.Db.SaveChangesAsync();
            return (staff.Id, userId);
        }

        public void Dispose() => Harness.Dispose();
    }

    /// <summary>Identity's roles, as a dictionary. Memberships and enabled flags are what the rules read.</summary>
    private sealed class FakeRoles : IRoleAdministration
    {
        private readonly Dictionary<long, RoleRecord> _roles = [];
        private readonly Dictionary<long, HashSet<long>> _membership = [];
        private readonly HashSet<long> _enabled = [];

        public long Add(string name, int? level)
        {
            var id = TestIds.Next();
            _roles[id] = new RoleRecord(id, name, null, level, 0);
            return id;
        }

        public long AddMember(long roleId, bool enabled)
        {
            var userId = TestIds.Next();
            _membership[userId] = [roleId];
            if (enabled)
            {
                _enabled.Add(userId);
            }

            return userId;
        }

        public bool Exists(long roleId) => _roles.ContainsKey(roleId);

        public IReadOnlyList<long> RolesOf(long userId) => _membership.GetValueOrDefault(userId)?.ToList() ?? [];

        public Task<IReadOnlyList<RoleRecord>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RoleRecord>>(_roles.Values.Select(WithCount).ToList());

        public Task<RoleRecord?> FindAsync(long roleId, CancellationToken ct)
            => Task.FromResult(_roles.TryGetValue(roleId, out var role) ? WithCount(role) : null);

        public Task<Result<long>> CreateAsync(string name, string? description, CancellationToken ct)
        {
            if (_roles.Values.Any(r => r.Name == name))
            {
                return Task.FromResult(Result.Failure<long>(new Error("role.name_taken", "Taken.")));
            }

            var id = TestIds.Next();
            _roles[id] = new RoleRecord(id, name, description, null, 0);
            return Task.FromResult(Result.Success(id));
        }

        public Task<Result> UpdateAsync(long roleId, string name, string? description, CancellationToken ct)
        {
            _roles[roleId] = _roles[roleId] with { Name = name, Description = description };
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(long roleId, CancellationToken ct)
        {
            _roles.Remove(roleId);
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<long>> RolesOfAsync(long userId, CancellationToken ct)
            => Task.FromResult(RolesOf(userId));

        public Task<Result> AssignAsync(long userId, long roleId, CancellationToken ct)
        {
            _membership[userId] = [roleId];
            return Task.FromResult(Result.Success());
        }

        public Task<int> CountEnabledMembersAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct)
            => Task.FromResult(EnabledIn(roleIds).Count());

        public Task<int> CountEnabledMembersExceptAsync(IReadOnlyCollection<long> roleIds, long userId, CancellationToken ct)
            => Task.FromResult(EnabledIn(roleIds).Count(u => u != userId));

        private IEnumerable<long> EnabledIn(IReadOnlyCollection<long> roleIds)
            => _membership.Where(m => _enabled.Contains(m.Key) && m.Value.Overlaps(roleIds)).Select(m => m.Key);

        private RoleRecord WithCount(RoleRecord role)
            => role with { MemberCount = _membership.Count(m => m.Value.Contains(role.Id)) };
    }
}
