using FluentAssertions;
using Retail25.Application.Reports;
using Retail25.Application.Staff;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Staff;
using Xunit;

namespace Retail25.Application.UnitTests.Staff;

/// <summary>
/// Clocking on and off, and the hours that come out the other end. The figure this produces is what
/// somebody gets paid, so the cases that matter are the ones where it could quietly be wrong.
/// </summary>
public sealed class TimeClockTests
{
    private static async Task<(MastersTestHarness Harness, StaffProfile Me)> SignedInAsync()
    {
        var harness = await MastersTestHarness.CreateAsync();
        var me = await harness.AddStaffAsync("SK", "Sam", "Kerr");
        harness.CurrentUser.StaffId = me.Id;
        return (harness, me);
    }

    [Fact]
    public async Task Clocking_in_opens_a_shift()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        var result = await harness.StaffCommands.Handle(
            new ClockInCommand(harness.Location.Id), CancellationToken.None);

        result.Value.IsClockedIn.Should().BeTrue();
        result.Value.ClockedInAt.Should().Be(harness.Clock.Now);
        harness.Db.TimeClockEntries.Should().ContainSingle().Which.ClockOut.Should().BeNull();
    }

    [Fact]
    public async Task Clocking_out_closes_it_and_records_the_hours()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);

        harness.Clock.Advance(TimeSpan.FromHours(7.5));

        var result = await harness.StaffCommands.Handle(
            new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        result.Value.IsClockedIn.Should().BeFalse();
        harness.Db.TimeClockEntries.Single().HoursWorked.Should().Be(7.5m);
    }

    [Fact]
    public async Task Clocking_in_twice_is_refused()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);

        var again = await harness.StaffCommands.Handle(
            new ClockInCommand(harness.Location.Id), CancellationToken.None);

        again.Error.Code.Should().Be(StaffHandlers.AlreadyClockedIn.Code);
        harness.Db.TimeClockEntries.Should().ContainSingle();
    }

    /// <summary>
    /// Someone who forgot to clock out at another store is still on the clock. Letting them open a
    /// second shift elsewhere would pay them twice for the same hour.
    /// </summary>
    [Fact]
    public async Task An_open_shift_at_another_store_still_blocks_clocking_in()
    {
        var (harness, me) = await SignedInAsync();
        using var _h = harness;

        var elsewhere = await harness.AddLocationAsync("Second Store", "SND");

        harness.Db.TimeClockEntries.Add(TimeClockEntry.ClockInAt(me.Id, elsewhere.Id, harness.Clock.Now));
        await harness.Db.SaveChangesAsync();

        var result = await harness.StaffCommands.Handle(
            new ClockInCommand(harness.Location.Id), CancellationToken.None);

        result.Error.Code.Should().Be(StaffHandlers.AlreadyClockedIn.Code);
    }

    [Fact]
    public async Task Clocking_out_when_you_were_never_on_is_refused()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        var result = await harness.StaffCommands.Handle(
            new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        result.Error.Should().Be(StaffHandlers.NotClockedIn);
    }

    [Fact]
    public async Task A_sign_in_with_no_staff_record_cannot_use_the_clock()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        harness.CurrentUser.StaffId = null;

        var result = await harness.StaffCommands.Handle(
            new ClockInCommand(harness.Location.Id), CancellationToken.None);

        result.Error.Should().Be(StaffHandlers.NoStaffProfile);
    }

    /// <summary>
    /// The widget must not read as zero for someone who has been on since eight this morning, so
    /// today's total counts the shift still running as well as the ones already closed.
    /// </summary>
    [Fact]
    public async Task Todays_total_includes_the_shift_still_running()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(3));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(2));

        var state = await harness.StaffCommands.Handle(
            new GetMyTimeClockQuery(harness.Location.Id), CancellationToken.None);

        state.Value.HoursSoFar.Should().Be(2m);
        state.Value.HoursToday.Should().Be(5m);
    }

    [Fact]
    public async Task The_browse_shows_who_is_on_right_now()
    {
        var (harness, me) = await SignedInAsync();
        using var _h = harness;

        await harness.AddStaffAsync("JB", "Jo", "Blake");
        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);

        var rows = await harness.StaffCommands.Handle(
            new BrowseStaffQuery(harness.Location.Id), CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Single(r => r.Id == me.Id).IsClockedIn.Should().BeTrue();
        rows.Single(r => r.StaffCode == "JB").IsClockedIn.Should().BeFalse();
    }

    /* ---------------------------------------------------------------------------------------------
     * Amending
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task Amending_a_shift_recomputes_the_hours()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var entry = harness.Db.TimeClockEntries.Single();

        var amended = await harness.StaffCommands.Handle(
            new AmendTimeClockEntryCommand(entry.Id, entry.ClockIn, entry.ClockIn.AddHours(8)),
            CancellationToken.None);

        amended.Value.HoursWorked.Should().Be(8m);
    }

    /// <summary>
    /// Reopening a shift has to clear the hours with it, or the report keeps counting a figure that
    /// no longer has an end time behind it.
    /// </summary>
    [Fact]
    public async Task Reopening_a_shift_clears_the_hours()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var entry = harness.Db.TimeClockEntries.Single();

        var amended = await harness.StaffCommands.Handle(
            new AmendTimeClockEntryCommand(entry.Id, entry.ClockIn, null), CancellationToken.None);

        amended.Value.ClockOut.Should().BeNull();
        amended.Value.HoursWorked.Should().BeNull();
    }

    [Fact]
    public async Task A_shift_cannot_be_made_to_end_before_it_began()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        var entry = harness.Db.TimeClockEntries.Single();

        var result = await harness.StaffCommands.Handle(
            new AmendTimeClockEntryCommand(entry.Id, entry.ClockIn, entry.ClockIn.AddHours(-2)),
            CancellationToken.None);

        result.Error.Should().Be(StaffHandlers.EndsBeforeItStarts);
    }

    /* ---------------------------------------------------------------------------------------------
     * The hours report
     * ------------------------------------------------------------------------------------------- */

    [Fact]
    public async Task The_hours_report_totals_the_closed_shifts()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(4));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(3.25));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var report = await harness.StaffReports.Handle(
            new HoursReportQuery(harness.Location.Id, harness.Today, harness.Today), CancellationToken.None);

        var row = report.Rows.Should().ContainSingle().Subject;
        row.Shifts.Should().Be(2);
        row.HoursWorked.Should().Be(7.25m);
        row.OpenShifts.Should().Be(0);
        report.TotalHours.Should().Be(7.25m);
    }

    /// <summary>
    /// A shift with no clock-out is counted but its hours are not. Guessing at "now minus clock-in"
    /// for someone who forgot to clock out three days ago would put a 72-hour shift on a payroll run.
    /// </summary>
    [Fact]
    public async Task An_open_shift_is_flagged_and_its_hours_are_not_guessed_at()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        // Captured before the clock moves: Today follows the clock, and after 70 hours it would name
        // a day three days after the shift that is being reported on.
        var startOfWindow = harness.Today;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(2));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(70));

        var report = await harness.StaffReports.Handle(
            new HoursReportQuery(harness.Location.Id, startOfWindow, startOfWindow.AddDays(7)),
            CancellationToken.None);

        var row = report.Rows.Single();
        row.HoursWorked.Should().Be(2m);
        row.OpenShifts.Should().Be(1);
        report.TotalOpenShifts.Should().Be(1);
    }

    [Fact]
    public async Task The_hours_report_can_be_narrowed_to_one_person()
    {
        var (harness, me) = await SignedInAsync();
        using var _h = harness;

        var other = await harness.AddStaffAsync("JB", "Jo", "Blake");

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        harness.CurrentUser.StaffId = other.Id;
        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(5));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var everyone = await harness.StaffReports.Handle(
            new HoursReportQuery(harness.Location.Id, harness.Today, harness.Today), CancellationToken.None);

        var justMe = await harness.StaffReports.Handle(
            new HoursReportQuery(harness.Location.Id, harness.Today, harness.Today, me.Id), CancellationToken.None);

        everyone.Rows.Should().HaveCount(2);
        justMe.Rows.Should().ContainSingle().Which.HoursWorked.Should().Be(1m);
    }

    [Fact]
    public async Task A_shift_outside_the_window_is_not_counted()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var report = await harness.StaffReports.Handle(
            new HoursReportQuery(harness.Location.Id, harness.Today.AddDays(1), harness.Today.AddDays(7)),
            CancellationToken.None);

        report.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// The window has to be built as UTC instants. A local-offset <c>DateTimeOffset</c> is what
    /// Npgsql refuses outright for <c>timestamptz</c> — a bug the in-memory provider cannot see.
    /// </summary>
    [Fact]
    public void The_day_range_is_expressed_in_utc()
    {
        var (from, to) = StaffHandlers.DayRangeUtc(new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 31));

        from.Offset.Should().Be(TimeSpan.Zero);
        to.Offset.Should().Be(TimeSpan.Zero);
        from.Hour.Should().Be(0);
        to.Hour.Should().Be(23);
    }

    [Fact]
    public async Task The_hours_export_is_a_csv_of_the_same_rows()
    {
        var (harness, _) = await SignedInAsync();
        using var _h = harness;

        await harness.StaffCommands.Handle(new ClockInCommand(harness.Location.Id), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(6));
        await harness.StaffCommands.Handle(new ClockOutCommand(harness.Location.Id), CancellationToken.None);

        var csv = await harness.StaffReports.Handle(
            new ExportHoursReportQuery(new HoursReportQuery(harness.Location.Id, harness.Today, harness.Today)),
            CancellationToken.None);

        csv.Should().StartWith("Code,Name,Shifts,Hours");
        csv.Should().Contain("SK,Sam Kerr,1,6");
    }
}
