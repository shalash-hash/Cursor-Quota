namespace Quota.Services;

public static class DailyPlanCalculator
{
    public const int AcceleratedPeriodLengthDays = 21;

    public static DateTime GetAcceleratedPeriodEnd(DateTime cycleStart)
        => cycleStart.Date.AddDays(AcceleratedPeriodLengthDays);

    public static bool IsWithinAcceleratedPeriod(DateTime today, DateTime cycleStart)
        => today.Date < GetAcceleratedPeriodEnd(cycleStart.Date);

    public static int CalculateRemainingPlanDays(DateTime today, DateTime planEnd, double remainingPercent)
    {
        today = today.Date;
        planEnd = planEnd.Date;

        if (planEnd < today)
            return 0;

        var days = (planEnd - today).Days;

        if (days == 0 && remainingPercent > 0)
            return 1;

        return Math.Max(0, days);
    }

    public static double CalculateCursorModelDailyPlan(
        double cursorRemaining,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        if (cursorRemaining <= 0)
            return 0;

        return CalculateDailyPlan(cursorRemaining, today, cycleStart, realResetDate);
    }

    public static double CalculateApiDailyPlan(
        double apiRemaining,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        if (apiRemaining <= 0)
            return 0;

        today = today.Date;
        cycleStart = cycleStart.Date;
        realResetDate = realResetDate.Date;

        if (IsWithinAcceleratedPeriod(today, cycleStart) && today <= realResetDate)
            return 0;

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

    public static double CalculateDailyPlan(
        double remainingPercent,
        DateTime today,
        DateTime cycleStart,
        DateTime realResetDate)
    {
        if (remainingPercent <= 0)
            return 0;

        today = today.Date;
        cycleStart = cycleStart.Date;
        realResetDate = realResetDate.Date;

        var planEnd = ResolvePlanEnd(today, cycleStart, realResetDate);
        return SpreadRemainingUntilPlanEnd(remainingPercent, today, planEnd);
    }

    private static double SpreadRemainingUntilReset(
        double remainingPercent,
        DateTime today,
        DateTime realResetDate)
        => SpreadRemainingUntilPlanEnd(remainingPercent, today, realResetDate);

    private static double SpreadRemainingUntilPlanEnd(
        double remainingPercent,
        DateTime today,
        DateTime planEnd)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, planEnd, remainingPercent);

        if (remainingPlanDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingPlanDays;
    }

    private static DateTime ResolvePlanEnd(DateTime today, DateTime cycleStart, DateTime realResetDate)
    {
        if (IsWithinAcceleratedPeriod(today, cycleStart) && today <= realResetDate)
        {
            var acceleratedEnd = GetAcceleratedPeriodEnd(cycleStart);
            return acceleratedEnd <= realResetDate ? acceleratedEnd : realResetDate;
        }

        return realResetDate;
    }
}
