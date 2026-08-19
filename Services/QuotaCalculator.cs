using Quota.Models;

namespace Quota.Services;

public class QuotaCalculator
{
    private const double PaceTolerancePercent = 0.1;

    public QuotaCalculationResult Calculate(QuotaUsage usage, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.Now;
        var today = now.Date;
        var periodEnd = usage.PeriodEnd.Date;

        var remainingDays = CalculateRemainingDays(today, periodEnd, usage.TotalUsedPercent);

        return new QuotaCalculationResult
        {
            RemainingDays = remainingDays,
            Total = CalculatePool(usage.TotalUsedPercent, usage.TodayTotalUsedPercent, remainingDays),
            FirstParty = CalculatePool(usage.FirstPartyUsedPercent, usage.TodayFirstPartyUsedPercent, remainingDays),
            Api = CalculatePool(usage.ApiUsedPercent, usage.TodayApiUsedPercent, remainingDays)
        };
    }

    private static PoolCalculation CalculatePool(double usedPercent, double todayUsedPercent, int remainingDays)
    {
        var remaining = 100 - usedPercent;
        var dailyTarget = CalculateDailyTarget(remaining, remainingDays);
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

    private static int CalculateRemainingDays(DateTime today, DateTime periodEnd, double totalUsedPercent)
    {
        if (periodEnd < today)
            return 0;

        var days = (periodEnd - today).Days;

        if (days == 0 && totalUsedPercent < 100)
            return 1;

        return Math.Max(0, days);
    }

    private static double CalculateDailyTarget(double remainingPercent, int remainingDays)
    {
        if (remainingPercent <= 0)
            return 0;

        if (remainingDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingDays;
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
