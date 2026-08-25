using Quota.Models;

namespace Quota.Services;

public class QuotaCalculator
{
    private const double PaceTolerancePercent = 0.1;

    public QuotaCalculationResult Calculate(QuotaUsage usage, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.Now;
        var today = now.Date;
        var cycleStart = usage.PeriodStart.Date;
        var periodEnd = usage.PeriodEnd.Date;

        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(
            today,
            periodEnd,
            Math.Max(0, 100 - usage.TotalUsedPercent));

        var firstParty = CalculatePool(
            usage.FirstPartyUsedPercent,
            usage.TodayFirstPartyUsedPercent,
            remaining =>
            {
                var hillPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(
                    remaining,
                    today,
                    cycleStart,
                    periodEnd);

                if (hillPlan <= 0)
                    return 0;

                return Math.Max(
                    hillPlan,
                    DailyPlanCalculator.CalculateLinearDailyPlan(remaining, today, periodEnd));
            });

        var api = CalculatePool(
            usage.ApiUsedPercent,
            usage.TodayApiUsedPercent,
            remaining =>
            {
                var spreadPlan = DailyPlanCalculator.CalculateApiDailyPlan(
                    remaining,
                    today,
                    cycleStart,
                    periodEnd);

                if (spreadPlan <= 0)
                    return 0;

                return Math.Max(
                    spreadPlan,
                    DailyPlanCalculator.CalculateLinearDailyPlan(remaining, today, periodEnd));
            });

        var total = CalculateTotalPool(
            usage.TotalUsedPercent,
            usage.TodayTotalUsedPercent,
            firstParty.DailyTarget + api.DailyTarget);

        return new QuotaCalculationResult
        {
            RemainingDays = remainingDays,
            Total = total,
            FirstParty = firstParty,
            Api = api
        };
    }

    private static PoolCalculation CalculatePool(
        double usedPercent,
        double todayUsedPercent,
        Func<double, double> dailyPlanFactory)
    {
        var remaining = Math.Max(0, 100 - usedPercent);
        var dailyTarget = dailyPlanFactory(remaining);
        var todayMetrics = CalculateTodayMetrics(todayUsedPercent, dailyTarget);
        var paceStatus = DeterminePaceStatus(todayUsedPercent, dailyTarget);

        return new PoolCalculation
        {
            UsedPercent = usedPercent,
            RemainingPercent = remaining,
            DailyTarget = dailyTarget,
            TodayUsed = todayUsedPercent,
            TodayRemaining = todayMetrics.TodayRemaining,
            IsTodayPlanCompleted = todayMetrics.IsPlanCompleted,
            TodayOverage = todayMetrics.TodayOverage,
            PaceStatus = paceStatus
        };
    }

    private static PoolCalculation CalculateTotalPool(
        double usedPercent,
        double todayUsedPercent,
        double dailyTarget)
    {
        var remaining = Math.Max(0, 100 - usedPercent);
        var todayMetrics = CalculateTodayMetrics(todayUsedPercent, dailyTarget);
        var paceStatus = DeterminePaceStatus(todayUsedPercent, dailyTarget);

        return new PoolCalculation
        {
            UsedPercent = usedPercent,
            RemainingPercent = remaining,
            DailyTarget = dailyTarget,
            TodayUsed = todayUsedPercent,
            TodayRemaining = todayMetrics.TodayRemaining,
            IsTodayPlanCompleted = todayMetrics.IsPlanCompleted,
            TodayOverage = todayMetrics.TodayOverage,
            PaceStatus = paceStatus
        };
    }

    private static (double TodayRemaining, bool IsPlanCompleted, double TodayOverage) CalculateTodayMetrics(
        double todayUsedPercent,
        double dailyTarget)
    {
        if (dailyTarget <= 0)
        {
            var overageWithoutPlan = Math.Max(0, todayUsedPercent);
            return (0, todayUsedPercent > 0, overageWithoutPlan);
        }

        if (todayUsedPercent >= dailyTarget)
            return (0, true, todayUsedPercent - dailyTarget);

        return (dailyTarget - todayUsedPercent, false, 0);
    }

    private static PaceStatus DeterminePaceStatus(double todayUsedPercent, double dailyTarget)
    {
        if (dailyTarget <= 0)
            return todayUsedPercent > 0 ? PaceStatus.AbovePlan : PaceStatus.OnPlan;

        if (todayUsedPercent < dailyTarget - PaceTolerancePercent)
            return PaceStatus.BelowPlan;

        if (todayUsedPercent > dailyTarget + PaceTolerancePercent)
            return PaceStatus.AbovePlan;

        return PaceStatus.OnPlan;
    }
}
