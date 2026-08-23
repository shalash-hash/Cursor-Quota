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
            100 - usage.TotalUsedPercent);

        return new QuotaCalculationResult
        {
            RemainingDays = remainingDays,
            Total = CalculatePool(
                usage.TotalUsedPercent,
                usage.TodayTotalUsedPercent,
                today,
                cycleStart,
                periodEnd),
            FirstParty = CalculatePool(
                usage.FirstPartyUsedPercent,
                usage.TodayFirstPartyUsedPercent,
                today,
                cycleStart,
                periodEnd),
            Api = CalculatePool(
                usage.ApiUsedPercent,
                usage.TodayApiUsedPercent,
                today,
                cycleStart,
                periodEnd)
        };
    }

    private static PoolCalculation CalculatePool(
        double usedPercent,
        double todayUsedPercent,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        var remaining = 100 - usedPercent;
        var dailyTarget = DailyPlanCalculator.CalculateDailyPlan(remaining, today, cycleStart, realResetDate);
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
