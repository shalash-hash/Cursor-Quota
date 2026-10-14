using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

public static class DailyPlanCalculator
{
    public const int FinalUniformTailDurationDays = 7;

    /// <summary>
    /// Начало финального 7-дневного хвоста (inclusive): первый instant, когда Optimal = Uniform.
    /// <c>max(cycleStart, realResetDate − 7 days)</c> — граница от reset, не от start.
    /// </summary>
    public static DateTime GetFinalUniformTailStart(DateTime cycleStart, DateTime realResetDate)
    {
        var tailStart = realResetDate.AddDays(-FinalUniformTailDurationDays);
        return tailStart < cycleStart ? cycleStart : tailStart;
    }

    /// <summary>Фаза 1 Optimal: ускоренный adaptive spread Models до начала финального хвоста.</summary>
    public static bool IsWithinOptimalAcceleratedPhase(DateTime today, DateTime cycleStart, DateTime realResetDate)
        => today < GetFinalUniformTailStart(cycleStart, realResetDate);

    /// <summary>Фаза 2 Optimal: линейный spread Models и API до reset (как Uniform).</summary>
    public static bool IsWithinFinalUniformTail(DateTime today, DateTime cycleStart, DateTime realResetDate)
    {
        var tailStart = GetFinalUniformTailStart(cycleStart, realResetDate);
        return today >= tailStart && today < realResetDate;
    }

    public static int CalculateRemainingPlanDays(DateTime today, DateTime planEnd, double remainingPercent)
        => BillingCycleCalendar.CountRemainingDays(today, planEnd, remainingPercent);

    public static double CalculateLinearDailyPlan(
        double remainingPercent,
        DateTime today,
        DateTime realResetDate)
    {
        if (remainingPercent <= 0)
            return 0;

        var remainingDays = CalculateRemainingPlanDays(
            today,
            realResetDate,
            remainingPercent);

        if (remainingDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingDays;
    }

    public static double CalculateCursorModelDailyPlan(
        double cursorRemaining,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        if (cursorRemaining <= 0)
            return 0;

        var finalUniformTailStart = GetFinalUniformTailStart(cycleStart, realResetDate);

        if (today < finalUniformTailStart)
            return SpreadRemainingUntilReset(cursorRemaining, today, finalUniformTailStart);

        return SpreadRemainingUntilReset(cursorRemaining, today, realResetDate);
    }

    public static double CalculateApiDailyPlan(
        double apiRemaining,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        if (apiRemaining <= 0)
            return 0;

        // API (добавочная квота) входит в месячный бюджет с первого дня — равномерно до reset.
        return SpreadRemainingUntilReset(apiRemaining, today, realResetDate);
    }

    public static double CalculateCombinedDailyPlan(
        double cursorRemaining,
        double apiRemaining,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        var cursorPlan = CalculateCursorModelDailyPlan(cursorRemaining, today, cycleStart, realResetDate);
        var apiPlan = CalculateApiDailyPlan(apiRemaining, today, cycleStart, realResetDate);
        return cursorPlan + apiPlan;
    }

    private static double SpreadRemainingUntilReset(double remainingPercent, DateTime today, DateTime realResetDate)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, realResetDate, remainingPercent);

        if (remainingPlanDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingPlanDays;
    }

    /// <summary>
    /// Плановый cumulative combined used на текущую billing-дату: симуляция плана с 100% остатка
    /// по каждому пулу от начала цикла, с учётом Uniform / Optimal и финального 7-дневного хвоста.
    /// </summary>
    public static PlannedCumulativeUsage CalculatePlannedCumulativeUsage(
        QuotaPlanMode mode,
        DateTime now,
        DateTime cycleStart,
        DateTime periodEnd,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd)
    {
        if (modelsLimitUsd is not decimal modelsLimit || modelsLimit <= 0m)
            return new PlannedCumulativeUsage(0, null);

        var apiLimit = apiLimitUsd ?? 0m;
        var combinedLimit = modelsLimit + (apiLimit > 0m ? apiLimit : 0m);
        if (combinedLimit <= 0m)
            return new PlannedCumulativeUsage(0, null);

        if (now < cycleStart)
            return new PlannedCumulativeUsage(0, 0m);

        if (now >= periodEnd)
            return new PlannedCumulativeUsage(100, combinedLimit);

        var todayBillingStart = BillingCycleCalendar.GetDayStart(now, cycleStart);
        var modelsPlanRemaining = 100d;
        var apiPlanRemaining = apiLimit > 0m ? 100d : 0d;
        var day = BillingCycleCalendar.GetDayStart(cycleStart, cycleStart);

        while (day < todayBillingStart && day < periodEnd)
        {
            ApplyPlannedDaySpend(
                mode,
                ref modelsPlanRemaining,
                ref apiPlanRemaining,
                day,
                cycleStart,
                periodEnd,
                apiLimit > 0m);
            day = day.AddDays(1);
        }

        if (now > todayBillingStart && todayBillingStart < periodEnd)
        {
            ApplyPlannedDaySpend(
                mode,
                ref modelsPlanRemaining,
                ref apiPlanRemaining,
                todayBillingStart,
                cycleStart,
                periodEnd,
                apiLimit > 0m);
        }

        var modelsUsedPoolPercent = 100d - modelsPlanRemaining;
        var apiUsedPoolPercent = apiPlanRemaining > 0 ? 100d - apiPlanRemaining : 0d;
        var usedUsd = QuotaMonetaryHelper.PercentToUsd(modelsUsedPoolPercent, modelsLimit)
            + (apiLimit > 0m
                ? QuotaMonetaryHelper.PercentToUsd(apiUsedPoolPercent, apiLimit)
                : 0m);
        var usedPercent = (double)(usedUsd / combinedLimit * 100m);
        usedPercent = Math.Clamp(usedPercent, 0, 100);

        return new PlannedCumulativeUsage(usedPercent, usedUsd);
    }

    private static void ApplyPlannedDaySpend(
        QuotaPlanMode mode,
        ref double modelsPlanRemaining,
        ref double apiPlanRemaining,
        DateTime billingDayStart,
        DateTime cycleStart,
        DateTime periodEnd,
        bool includeApiPool)
    {
        var modelsDaily = ResolveModelsDailyPlanForMode(
            mode,
            modelsPlanRemaining,
            billingDayStart,
            cycleStart,
            periodEnd);
        var apiDaily = includeApiPool && apiPlanRemaining > 0
            ? ResolveApiDailyPlanForMode(mode, apiPlanRemaining, billingDayStart, cycleStart, periodEnd)
            : 0d;

        modelsPlanRemaining = Math.Max(0, modelsPlanRemaining - modelsDaily);
        apiPlanRemaining = Math.Max(0, apiPlanRemaining - apiDaily);
    }

    private static double ResolveModelsDailyPlanForMode(
        QuotaPlanMode mode,
        double remainingPercent,
        DateTime today,
        DateTime cycleStart,
        DateTime periodEnd) =>
        mode == QuotaPlanMode.Uniform
            ? CalculateLinearDailyPlan(remainingPercent, today, periodEnd)
            : CalculateCursorModelDailyPlan(remainingPercent, today, cycleStart, periodEnd);

    private static double ResolveApiDailyPlanForMode(
        QuotaPlanMode mode,
        double remainingPercent,
        DateTime today,
        DateTime cycleStart,
        DateTime periodEnd) =>
        mode == QuotaPlanMode.Uniform
            ? CalculateLinearDailyPlan(remainingPercent, today, periodEnd)
            : CalculateApiDailyPlan(remainingPercent, today, cycleStart, periodEnd);
}

public readonly record struct PlannedCumulativeUsage(double UsedPercent, decimal? UsedUsd);
