using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

public class QuotaCalculator
{
    private const double PaceTolerancePercent = 0.1;

    public QuotaCalculationResult Calculate(QuotaUsage usage, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.Now;
        var today = now;
        var cycleStart = usage.PeriodStart;
        var periodEnd = usage.PeriodEnd;
        var totalUsedPercent = QuotaMonetaryHelper.ResolveCombinedUsedPercent(usage)
            ?? usage.TotalUsedPercent;

        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(
            today,
            periodEnd,
            Math.Max(0, 100 - totalUsedPercent));

        var firstParty = CalculatePool(
            usage.FirstPartyUsedPercent,
            usage.TodayFirstPartyUsedPercent,
            remaining => DailyPlanCalculator.CalculateCursorModelDailyPlan(
                remaining,
                today,
                cycleStart,
                periodEnd));

        var api = CalculatePool(
            usage.ApiUsedPercent,
            usage.TodayApiUsedPercent,
            remaining => DailyPlanCalculator.CalculateApiDailyPlan(
                remaining,
                today,
                cycleStart,
                periodEnd));

        var combinedDailyTarget = QuotaMonetaryHelper.ResolveCombinedDayPercent(
            firstParty.DailyTarget,
            api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

        var total = CalculateTotalPool(
            usage,
            totalUsedPercent,
            usage.TodayTotalUsedPercent,
            combinedDailyTarget,
            firstParty.DailyTarget,
            api.DailyTarget);

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
        QuotaUsage usage,
        double usedPercent,
        double todayUsedPercent,
        double dailyTarget,
        double modelsDailyTarget,
        double apiDailyTarget)
    {
        var remaining = Math.Max(0, 100 - usedPercent);
        var todayMetrics = CalculateTodayMetrics(todayUsedPercent, dailyTarget);
        var paceStatus = DeterminePaceStatus(todayUsedPercent, dailyTarget);

        var isPlanCompleted = todayMetrics.IsPlanCompleted;
        var paceStatusForTotal = paceStatus;
        var todayUsageUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);
        if (todayUsageUsd is not null)
        {
            var dailyPlanUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
                modelsDailyTarget,
                apiDailyTarget,
                usage.ModelsEstimatedLimitUsd,
                usage.ApiIncludedAmountUsd);
            isPlanCompleted = DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(
                todayUsageUsd.Value,
                dailyPlanUsd);
            paceStatusForTotal = DeterminePaceStatusFromUsd(todayUsageUsd.Value, dailyPlanUsd);
        }

        return new PoolCalculation
        {
            UsedPercent = usedPercent,
            RemainingPercent = remaining,
            DailyTarget = dailyTarget,
            TodayUsed = todayUsedPercent,
            TodayRemaining = todayMetrics.TodayRemaining,
            IsTodayPlanCompleted = isPlanCompleted,
            TodayOverage = todayMetrics.TodayOverage,
            PaceStatus = paceStatusForTotal
        };
    }

    private static PaceStatus DeterminePaceStatusFromUsd(decimal todayUsd, decimal dailyPlanUsd) =>
        DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(todayUsd, dailyPlanUsd).Kind switch
        {
            DailyPlanDeltaKind.Behind => PaceStatus.BelowPlan,
            DailyPlanDeltaKind.Ahead => PaceStatus.AbovePlan,
            DailyPlanDeltaKind.OnPlan => PaceStatus.OnPlan,
            _ => todayUsd > 0 ? PaceStatus.AbovePlan : PaceStatus.OnPlan
        };

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
