using Quota.Helpers;

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
}
