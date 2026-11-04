using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>Payroll arithmetic (tax slabs, the projection method, tax years, service, gratuity, formats) and attendance rules.</summary>
public class HrRulesTests
{
    /// <summary>The salaried slabs of the Finance Act 2025, as seeded for Pakistan.</summary>
    private static readonly TaxSlab[] Pakistan =
    [
        new(0m, 600_000m, 0m, 0m),
        new(600_000m, 1_200_000m, 0m, 1m),
        new(1_200_000m, 2_200_000m, 6_000m, 11m),
        new(2_200_000m, 3_200_000m, 116_000m, 23m),
        new(3_200_000m, 4_100_000m, 346_000m, 30m),
        new(4_100_000m, null, 616_000m, 35m),
    ];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(600_000, 0)]
    [InlineData(1_200_000, 6_000)]
    [InlineData(2_000_000, 94_000)]
    [InlineData(3_200_000, 346_000)]
    [InlineData(5_000_000, 931_000)]
    public void Annual_tax_is_the_slab_fixed_amount_plus_its_rate_over_the_start(decimal income, decimal tax) =>
        Assert.Equal(tax, PayrollRules.AnnualTax(income, Pakistan));

    [Fact]
    public void Income_above_the_surcharge_threshold_pays_the_surcharge_on_the_tax() =>
        Assert.Equal(3_685_290m, PayrollRules.AnnualTax(12_000_000m, Pakistan, 10_000_000m, 9m));

    [Fact]
    public void The_projection_spreads_the_years_tax_over_the_months_still_to_pay()
    {
        // 290,000 a month: a full tax year is 3,480,000; from February (five months left with this one), 1,450,000.
        var february = PayrollRules.ProjectedMonthlyTax(0, 0, 290_000m, 290_000m, 4, Pakistan);
        Assert.Equal((6_700m, 1_450_000m, 33_500m), (february.Tax, february.ProjectedAnnualTaxable, february.AnnualTax));
        var march = PayrollRules.ProjectedMonthlyTax(290_000m, 6_700m, 290_000m, 290_000m, 3, Pakistan);
        Assert.Equal(6_700m, march.Tax);
        // A bonus month raises this month's tax; tax already deducted above the year's is never refunded through payroll.
        Assert.True(PayrollRules.ProjectedMonthlyTax(290_000m, 6_700m, 990_000m, 290_000m, 3, Pakistan).Tax > march.Tax);
        Assert.Equal(0m, PayrollRules.ProjectedMonthlyTax(100_000m, 50_000m, 10_000m, 10_000m, 0, Pakistan).Tax);
    }

    [Fact]
    public void Tax_years_and_the_months_left_follow_the_start_month_and_the_leaving_date()
    {
        Assert.Equal(new DateOnly(2026, 7, 1), PayrollRules.TaxYearStart(new DateOnly(2027, 3, 31), 7));
        Assert.Equal(new DateOnly(2027, 7, 1), PayrollRules.TaxYearStart(new DateOnly(2027, 7, 1), 7));
        Assert.Equal("2026-27", PayrollRules.TaxYearName(new DateOnly(2026, 7, 1)));
        Assert.Equal("2026", PayrollRules.TaxYearName(new DateOnly(2026, 1, 1)));
        Assert.Equal(11, PayrollRules.MonthsAfter(new DateOnly(2026, 7, 31), 7, null));
        Assert.Equal(0, PayrollRules.MonthsAfter(new DateOnly(2027, 6, 30), 7, null));
        Assert.Equal(3, PayrollRules.MonthsAfter(new DateOnly(2026, 11, 30), 7, new DateOnly(2027, 2, 15)));
        Assert.Equal(0, PayrollRules.MonthsAfter(new DateOnly(2026, 11, 30), 7, new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Service_is_counted_in_whole_months_and_gratuity_rounds_a_long_part_year_up()
    {
        Assert.Equal(1, PayrollRules.CompletedMonths(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        Assert.Equal(0, PayrollRules.CompletedMonths(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 30)));
        Assert.Equal(71, PayrollRules.CompletedMonths(new DateOnly(2020, 1, 15), new DateOnly(2026, 1, 13)));
        Assert.Equal(7, PayrollRules.GratuityYears(new DateOnly(2020, 1, 1), new DateOnly(2026, 8, 15), 12, 6));
        Assert.Equal(6, PayrollRules.GratuityYears(new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 30), 12, 6));
        Assert.Equal(0, PayrollRules.GratuityYears(new DateOnly(2025, 3, 1), new DateOnly(2026, 1, 31), 12, 6));
    }

    [Fact]
    public void Identity_numbers_follow_the_rule_set_pattern_and_IBANs_their_check_digits()
    {
        const string cnic = @"^\d{5}-\d{7}-\d$";
        Assert.True(PayrollRules.Matches("35202-1234567-1", cnic));
        Assert.False(PayrollRules.Matches("3520212345671", cnic));
        Assert.True(PayrollRules.Matches("anything", null));
        Assert.False(PayrollRules.IsValidPattern("[unclosed"));
        Assert.Equal("PK36SCBL0000001123456702", PayrollRules.NormalizeIban(" pk36 scbl-0000 0011 2345 6702 "));
        Assert.True(PayrollRules.IbanChecksumValid("PK36SCBL0000001123456702"));
        Assert.True(PayrollRules.IbanChecksumValid("GB82 WEST 1234 5698 7654 32"));
        Assert.False(PayrollRules.IbanChecksumValid("PK37SCBL0000001123456702"));
        Assert.False(PayrollRules.IbanChecksumValid("PK36"));
    }

    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static ScheduledShift NineToFive(DateOnly day) => ScheduledShift.On(day, new TimeOnly(9, 0), new TimeOnly(17, 0), 60, 10);

    private static DayWork Day(int inHour, int inMinute, int outHour, int outMinute, bool workingDay = true) =>
        AttendanceRules.Evaluate(NineToFive(Monday), workingDay, 420,
            [(Monday.ToDateTime(new TimeOnly(inHour, inMinute)), Monday.ToDateTime(new TimeOnly(outHour, outMinute)))], 30);

    [Fact]
    public void Lateness_and_leaving_early_count_beyond_the_grace_minutes()
    {
        // 9:05 to 17:00 less the hour's break is 415 minutes worked; five minutes late is within the grace.
        var onTime = Day(9, 5, 17, 0);
        Assert.Equal((415, 0, 0, 0), (onTime.WorkedMinutes, onTime.LateMinutes, onTime.EarlyMinutes, onTime.OvertimeMinutes));
        var late = Day(9, 25, 16, 30);
        Assert.Equal((25, 30), (late.LateMinutes, late.EarlyMinutes));
        Assert.Equal(Monday.ToDateTime(new TimeOnly(9, 25)), late.FirstIn);
    }

    [Fact]
    public void Overtime_is_time_beyond_the_shift_and_all_the_time_on_a_day_off()
    {
        Assert.Equal(120, Day(9, 0, 19, 0).OvertimeMinutes);
        Assert.Equal(0, Day(9, 0, 17, 20).OvertimeMinutes);   // below the 30 minutes that count
        var dayOff = Day(10, 0, 14, 0, workingDay: false);
        Assert.Equal((240, 240, 0), (dayOff.WorkedMinutes, dayOff.OvertimeMinutes, dayOff.LateMinutes));
        // Two punches: the gap between them is the break.
        var split = AttendanceRules.Evaluate(NineToFive(Monday), true, 420,
            [(Monday.ToDateTime(new TimeOnly(9, 0)), Monday.ToDateTime(new TimeOnly(13, 0))), (Monday.ToDateTime(new TimeOnly(14, 0)), Monday.ToDateTime(new TimeOnly(17, 0)))], 30);
        Assert.Equal(420, split.WorkedMinutes);
        Assert.Same(DayWork.None, AttendanceRules.Evaluate(null, true, 420, [], 30));
    }

    [Fact]
    public void A_night_shift_ends_the_next_morning_and_its_day_begins_four_hours_before_it()
    {
        var night = ScheduledShift.On(Monday, new TimeOnly(22, 0), new TimeOnly(6, 0), 30, 10);
        Assert.Equal(Monday.AddDays(1).ToDateTime(new TimeOnly(6, 0)), night.End);
        Assert.Equal(450, night.ScheduledMinutes);
        Assert.Equal(Monday.ToDateTime(new TimeOnly(18, 0)), AttendanceRules.DayBegins(Monday, night));
        Assert.Equal(Monday.ToDateTime(TimeOnly.MinValue), AttendanceRules.DayBegins(Monday, null));
    }

    [Fact]
    public void Working_weeks_and_leave_days_count_only_the_days_that_count()
    {
        Assert.Equal(WorkDays.MondayToSaturday, AttendanceRules.FirstDays(6));
        Assert.Equal(WorkDays.MondayToFriday, AttendanceRules.FirstDays(5));
        Assert.True(AttendanceRules.Works(WorkDays.MondayToSaturday, DayOfWeek.Saturday));
        Assert.False(AttendanceRules.Works(WorkDays.MondayToSaturday, DayOfWeek.Sunday));
        bool weekday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        // Monday to the next Monday: six weekdays, with a half first and last day.
        Assert.Equal(6m, AttendanceRules.LeaveDays(Monday, Monday.AddDays(7), false, false, weekday));
        Assert.Equal(5m, AttendanceRules.LeaveDays(Monday, Monday.AddDays(7), true, true, weekday));
        Assert.Equal(0.5m, AttendanceRules.LeaveDays(Monday, Monday, true, false, weekday));
        Assert.Equal(0m, AttendanceRules.LeaveDays(Monday.AddDays(5), Monday.AddDays(6), false, false, weekday));
    }
}
