using AssetKeeper.Application.Plans;
using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Tests.Plans;

public sealed class PlanScheduleTests
{
    [Fact]
    public void MonthlyPlan_UsesLastDayWhenMonthIsShorter()
    {
        var plan = Plan(AutoPlanCycle.Monthly, dayOfMonth: 31, startDate: new DateOnly(2026, 1, 1));

        var periods = PlanSchedule.GetDuePeriods(plan, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        var period = Assert.Single(periods);
        Assert.Equal(new DateOnly(2026, 2, 28), period.DueDate);
        Assert.Equal("2026-02", period.PeriodKey);
    }

    [Fact]
    public void WeeklyPlan_UsesConfiguredWeekday()
    {
        var plan = Plan(AutoPlanCycle.Weekly, dayOfWeek: 1, startDate: new DateOnly(2026, 10, 1));

        var periods = PlanSchedule.GetDuePeriods(plan, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(4, periods.Count);
        Assert.All(periods, period => Assert.Equal(DayOfWeek.Monday, period.DueDate.DayOfWeek));
        Assert.Equal("W2026-10-05", periods[0].PeriodKey);
    }

    [Fact]
    public void MonthlyPlan_SkipsPeriodsBeforeGenerateFrom()
    {
        var plan = Plan(AutoPlanCycle.Monthly, dayOfMonth: 5, startDate: new DateOnly(2026, 10, 15));

        var periods = PlanSchedule.GetDuePeriods(plan, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30));

        var period = Assert.Single(periods);
        Assert.Equal(new DateOnly(2026, 11, 5), period.DueDate);
    }

    private static AutoPlan Plan(AutoPlanCycle cycle, int? dayOfMonth = null, int? dayOfWeek = null, DateOnly? startDate = null)
    {
        return AutoPlan.Create(
            Guid.NewGuid(),
            AutoPlanType.FundSip,
            "测试计划",
            100m,
            cycle,
            dayOfMonth,
            dayOfWeek,
            new TimeOnly(9, 0),
            startDate ?? new DateOnly(2026, 1, 1),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
