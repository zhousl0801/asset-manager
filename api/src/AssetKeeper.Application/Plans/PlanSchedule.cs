using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Application.Plans;

public static class PlanSchedule
{
    public static IReadOnlyList<PlanDuePeriod> GetDuePeriods(AutoPlan plan, DateOnly from, DateOnly toDate)
    {
        if (toDate < from || plan.GenerateFrom > toDate)
        {
            return Array.Empty<PlanDuePeriod>();
        }

        var start = plan.GenerateFrom > from ? plan.GenerateFrom : from;
        var periods = new List<PlanDuePeriod>();

        if (plan.Cycle == AutoPlanCycle.Monthly)
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);

            while (cursor <= toDate)
            {
                var day = Math.Min(plan.DayOfMonth!.Value, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                var dueDate = new DateOnly(cursor.Year, cursor.Month, day);

                if (dueDate >= start && dueDate <= toDate)
                {
                    periods.Add(new PlanDuePeriod($"{dueDate.Year:D4}-{dueDate.Month:D2}", dueDate, plan.Time));
                }

                cursor = cursor.AddMonths(1);
            }
        }
        else
        {
            var targetDay = plan.DayOfWeek!.Value % 7;
            var delta = (targetDay - (int)start.DayOfWeek + 7) % 7;
            var dueDate = start.AddDays(delta);

            while (dueDate <= toDate)
            {
                periods.Add(new PlanDuePeriod($"W{dueDate:yyyy-MM-dd}", dueDate, plan.Time));
                dueDate = dueDate.AddDays(7);
            }
        }

        return periods;
    }

    public static PlanDuePeriod? GetNextDue(AutoPlan plan, DateOnly from)
    {
        var start = plan.GenerateFrom > from ? plan.GenerateFrom : from;
        var periods = GetDuePeriods(plan, start, start.AddYears(2));

        return periods.Count == 0 ? null : periods[0];
    }

    public static string PeriodKeyFor(AutoPlan plan, DateOnly dueDate)
    {
        return plan.Cycle == AutoPlanCycle.Monthly
            ? $"{dueDate.Year:D4}-{dueDate.Month:D2}"
            : $"W{dueDate:yyyy-MM-dd}";
    }

    public static int TypeOrder(AutoPlanType type)
    {
        return type switch
        {
            AutoPlanType.SalaryIncome => 0,
            AutoPlanType.ProvidentDeposit => 1,
            AutoPlanType.ProvidentWithdrawal => 2,
            _ => 3
        };
    }

    public static bool IsDueOn(AutoPlan plan, DateOnly date)
    {
        if (date < plan.GenerateFrom)
        {
            return false;
        }

        if (plan.Cycle == AutoPlanCycle.Monthly)
        {
            var day = Math.Min(plan.DayOfMonth!.Value, DateTime.DaysInMonth(date.Year, date.Month));

            return date.Day == day;
        }

        return (int)date.DayOfWeek == plan.DayOfWeek!.Value % 7;
    }
}

public sealed record PlanDuePeriod(string PeriodKey, DateOnly DueDate, TimeOnly DueTime);
