using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;

namespace RetailPlus.Sync.Tests;

/// <summary>
/// Two tills without a connection each punch the same user in. When the sync brings the two open shifts together, the
/// earlier one is closed when the later one began, in every database, without anyone stepping in.
/// </summary>
public sealed class ShiftClashTests(SyncWorld world) : IClassFixture<SyncWorld>
{
    private void Require() => Skip.IfNot(world.Available, world.UnavailableReason);

    private static readonly DateTime Day = DateTime.Today.AddDays(-1);

    /// <summary>A cashier of the store, made on the server and sent to the store's tills.</summary>
    private async Task<UserAccount> CashierAsync(Guid store, params Till[] tills)
    {
        var setup = new SetupService(world.ServerData);
        var role = (await setup.GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole).Id;
        var user = await setup.SaveUserAsync(new UserAccount { UserName = "shift-" + Guid.NewGuid().ToString("N")[..6], RoleId = role, StoreId = store, IsActive = true }, "Password-1");
        foreach (var till in tills) await till.SyncAsync();
        return user;
    }

    private static async Task<List<TimeClockEntry>> ShiftsAsync(DataContext data, Guid user)
    {
        await using var db = data.Create();
        return [.. (await db.TimeClockEntries.AsNoTracking().Where(t => t.UserId == user).ToListAsync()).OrderBy(t => t.ClockIn)];
    }

    private static async Task<List<AuditEntry>> ClosingsAsync(DataContext data, Guid user)
    {
        await using var db = data.Create();
        var shifts = await db.TimeClockEntries.Where(t => t.UserId == user).Select(t => (Guid?)t.Id).ToListAsync();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.TimeClockEdit && a.UserName == OpenShiftsClosed.Editor && shifts.Contains(a.EntityId)).ToListAsync();
    }

    private static async Task<decimal> HoursAsync(DataContext data, Guid user) =>
        (await new StaffService(data).HoursByDayAsync(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day), user)).Sum(h => h.Hours);

    [SkippableFact]
    public async Task Two_tills_offline_that_punch_the_same_user_in_end_with_the_earlier_shift_closed_everywhere()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var user = await CashierAsync(store.Id, a, b);

        // Both tills are offline: each lets the user punch in, as neither knows of the other's shift.
        await new StaffService(a.Data).PunchInAsync(user.Id, Day.AddHours(9));
        await new StaffService(b.Data).PunchInAsync(user.Id, Day.AddHours(9.5));
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (var data in new[] { world.ServerData, a.Data, b.Data })
        {
            var shifts = await ShiftsAsync(data, user.Id);
            Assert.Equal([(Day.AddHours(9), (DateTime?)Day.AddHours(9.5)), (Day.AddHours(9.5), null)], shifts.Select(s => (s.ClockIn, s.ClockOut)));
            Assert.Equal(OpenShiftsClosed.Editor, shifts[0].EditedBy);
            Assert.Contains("punched in again", shifts[0].EditReason, StringComparison.Ordinal);
            Assert.Null(shifts[1].EditedBy);
            // The server closed it (station 0); the line reached both tills.
            var closing = Assert.Single(await ClosingsAsync(data, user.Id));
            Assert.Equal((shifts[0].Id, 0, store.Id), (closing.EntityId!.Value, closing.StationNumber, closing.StoreId));
            Assert.Contains("\"ClockOut\":null", closing.Before, StringComparison.Ordinal);
        }
        // One shift open: till A now sees the user as in since 9:30, and nobody can punch them in twice.
        Assert.Equal(Day.AddHours(9.5), (await new StaffService(a.Data).OpenEntryAsync(user.Id))!.ClockIn);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new StaffService(a.Data).PunchInAsync(user.Id, Day.AddHours(10)));

        // The user punches out at till B: half an hour and seven and a half, eight hours in every database.
        await new StaffService(b.Data).PunchOutAsync(user.Id, Day.AddHours(17));
        await b.SyncAsync();
        await a.SyncAsync();
        foreach (var data in new[] { world.ServerData, a.Data, b.Data })
        {
            Assert.Equal(8m, await HoursAsync(data, user.Id));
            Assert.All(await ShiftsAsync(data, user.Id), s => Assert.NotNull(s.ClockOut));
        }
    }

    [SkippableFact]
    public async Task A_till_that_receives_the_other_shift_before_sending_its_own_closes_it_and_every_database_agrees()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var user = await CashierAsync(store.Id, a, b);

        await new StaffService(a.Data).PunchInAsync(user.Id, Day.AddHours(8));
        await new StaffService(b.Data).PunchInAsync(user.Id, Day.AddHours(8).AddMinutes(45));
        await b.SyncAsync();
        // Till A still has no connection, but receives the server's package on a USB stick: B's shift arrives before A's
        // own shift has left.
        using (var admin = await world.SignInAsync())
        {
            var file = Path.Combine(world.Folder, "for-a-" + Guid.NewGuid().ToString("N")[..6] + SyncPackage.Extension);
            await File.WriteAllBytesAsync(file, await admin.GetByteArrayAsync($"/api/sync/download-file?station={a.Data.Station!.StationNumber}"));
            Assert.Equal(SyncBatchStatus.Applied, (await a.Sync.LoadPackageFromFileAsync(file)).Status);
        }
        var atA = await ShiftsAsync(a.Data, user.Id);
        Assert.Equal(Day.AddHours(8).AddMinutes(45), atA[0].ClockOut);
        Assert.Equal(OpenShiftsClosed.Editor, atA[0].EditedBy);

        // Then the tills synchronise: the closed shift goes up from A, and B receives it with A's audit line.
        await a.SyncAsync();
        await b.SyncAsync();
        foreach (var data in new[] { world.ServerData, a.Data, b.Data })
        {
            var shifts = await ShiftsAsync(data, user.Id);
            Assert.Equal([(Day.AddHours(8), (DateTime?)Day.AddHours(8).AddMinutes(45)), (Day.AddHours(8).AddMinutes(45), null)], shifts.Select(s => (s.ClockIn, s.ClockOut)));
            Assert.Equal(atA[0].EditReason, shifts[0].EditReason);
            var closing = Assert.Single(await ClosingsAsync(data, user.Id));
            Assert.Equal(a.Data.Station!.StationNumber, closing.StationNumber);
        }
    }

    [SkippableFact]
    public async Task Shifts_from_two_tills_that_overlap_count_once_in_the_hours()
    {
        Require();
        var store = await world.AddStoreAsync();
        var a = await world.NewJoinedTillAsync(store.Id);
        var b = await world.NewJoinedTillAsync(store.Id);
        var user = await CashierAsync(store.Id, a, b);

        // Offline, the user works 9 to 5 on till A's clock and, by mistake, 9:30 to 12 on till B's.
        var staffA = new StaffService(a.Data);
        await staffA.PunchInAsync(user.Id, Day.AddHours(9));
        await staffA.PunchOutAsync(user.Id, Day.AddHours(17));
        var staffB = new StaffService(b.Data);
        await staffB.PunchInAsync(user.Id, Day.AddHours(9.5));
        await staffB.PunchOutAsync(user.Id, Day.AddHours(12));
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (var data in new[] { world.ServerData, a.Data, b.Data })
        {
            // Both shifts are kept as punched (neither was open), and the hours count the time once: 8, not 10.5.
            Assert.Equal(2, (await ShiftsAsync(data, user.Id)).Count);
            Assert.Empty(await ClosingsAsync(data, user.Id));
            Assert.Equal(8m, await HoursAsync(data, user.Id));
        }
    }

    [Fact]
    public void Overlapping_shifts_merge_and_shifts_that_only_meet_stay_apart()
    {
        var nine = new DateTime(2026, 10, 27, 9, 0, 0);
        var merged = TimeClock.Merge([(nine, nine.AddHours(8)), (nine.AddHours(0.5), nine.AddHours(3)), (nine.AddHours(8), nine.AddHours(9)), (nine.AddHours(10), nine.AddHours(10))]);
        Assert.Equal([(nine, nine.AddHours(8)), (nine.AddHours(8), nine.AddHours(9))], merged);
    }
}
