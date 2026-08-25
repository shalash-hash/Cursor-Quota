namespace Quota.Services;

public static class DailyPlanCalculator
{
    public const int CursorModelReserveDaysBeforeReset = 5;

    public static DateTime GetCursorPlanEnd(DateTime realResetDate)
        => realResetDate.Date.AddDays(-CursorModelReserveDaysBeforeReset);

    public static DateTime GetApiPlanStart(DateTime realResetDate)
        => GetCursorPlanEnd(realResetDate).AddDays(1);

    public static bool IsWithinApiReservePeriod(DateTime today, DateTime realResetDate)
        => today.Date > GetCursorPlanEnd(realResetDate);

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

    public static double CalculateLinearDailyPlan(
        double remainingPercent,
        DateTime today,
        DateTime realResetDate)
    {
        if (remainingPercent <= 0)
            return 0;

        var remainingDays = CalculateRemainingPlanDays(
            today.Date,
            realResetDate.Date,
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

        today = today.Date;
        cycleStart = cycleStart.Date;
        realResetDate = realResetDate.Date;

        var planEnd = ResolveCursorPlanEnd(cycleStart, realResetDate);
        if (today > planEnd)
            return 0;

        return CalculateHillDailyPlan(cursorRemaining, today, cycleStart, planEnd);
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
        realResetDate = realResetDate.Date;

        if (!IsWithinApiReservePeriod(today, realResetDate))
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

    private static DateTime ResolveCursorPlanEnd(DateTime cycleStart, DateTime realResetDate)
    {
        var reserveEnd = GetCursorPlanEnd(realResetDate);
        return reserveEnd < cycleStart ? realResetDate : reserveEnd;
    }

    private static double CalculateHillDailyPlan(
        double remaining,
        DateTime today,
        DateTime cycleStart,
        DateTime planEnd)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, planEnd, remaining);
        if (remainingPlanDays <= 0)
            return remaining;

        var totalPlanDays = Math.Max(1, (planEnd - cycleStart).Days);
        var elapsedDays = Math.Max(0, (today - cycleStart).Days);
        var progress = Math.Min(1.0, elapsedDays / (double)totalPlanDays);
        var nextProgress = Math.Min(1.0, (elapsedDays + 1) / (double)totalPlanDays);

        var onTrackDaily = 100 * (CumulativeSpendFraction(nextProgress) - CumulativeSpendFraction(progress));
        var idealRemaining = 100 * (1 - CumulativeSpendFraction(progress));
        var deficit = Math.Max(0, remaining - idealRemaining);
        var dampening = (1 - progress) * (1 - progress);
        var catchUp = deficit / remainingPlanDays * dampening;

        var plan = onTrackDaily + catchUp;
        return Math.Max(0, Math.Min(remaining, plan));
    }

    private static double CumulativeSpendFraction(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var remainingFraction = 1 - progress;
        return 1 - remainingFraction * remainingFraction;
    }

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

    private static double SpreadRemainingUntilReset(double remainingPercent, DateTime today, DateTime realResetDate)
    {
        var remainingPlanDays = CalculateReserveRemainingDays(today, realResetDate, remainingPercent);

        if (remainingPlanDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingPlanDays;
    }

    private static int CalculateReserveRemainingDays(DateTime today, DateTime realResetDate, double remainingPercent)
    {
        today = today.Date;
        realResetDate = realResetDate.Date;

        if (realResetDate < today)
            return 0;

        var days = (realResetDate - today).Days + 1;

        if (days <= 0 && remainingPercent > 0)
            return 1;

        return Math.Max(0, days);
    }
}
