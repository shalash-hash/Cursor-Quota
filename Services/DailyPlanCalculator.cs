using Quota.Helpers;

namespace Quota.Services;

public static class DailyPlanCalculator
{
    public const int AcceleratedPhaseCalendarDays = 21;

    /// <summary>
    /// Последний календарный день ускоренной фазы (включительно).
    /// cycleStart — день 1; ровно 21 календарный день ⇒ cycleStart + 20 days.
    /// </summary>
    public static DateTime GetAcceleratedEndInclusive(DateTime cycleStart, DateTime realResetDate)
    {
        var candidate = cycleStart.AddDays(AcceleratedPhaseCalendarDays - 1);
        return candidate < realResetDate ? candidate : realResetDate;
    }

    public static DateTime GetReservePhaseStart(DateTime cycleStart, DateTime realResetDate)
        => GetAcceleratedEndInclusive(cycleStart, realResetDate).AddDays(1);

    public static bool IsWithinAcceleratedPhase(DateTime today, DateTime cycleStart, DateTime realResetDate)
        => today <= GetAcceleratedEndInclusive(cycleStart, realResetDate);

    public static bool IsWithinReservePhase(DateTime today, DateTime cycleStart, DateTime realResetDate)
        => today > GetAcceleratedEndInclusive(cycleStart, realResetDate);

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

        var acceleratedEndInclusive = GetAcceleratedEndInclusive(cycleStart, realResetDate);

        if (today <= acceleratedEndInclusive)
            return CalculateHillDailyPlan(cursorRemaining, today, cycleStart, acceleratedEndInclusive);

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

        if (IsWithinAcceleratedPhase(today, cycleStart, realResetDate))
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

    private static double CalculateHillDailyPlan(
        double remaining,
        DateTime today,
        DateTime cycleStart,
        DateTime planEndInclusive)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, planEndInclusive, remaining);
        if (remainingPlanDays <= 0)
            return remaining;

        var totalPlanDays = Math.Max(1, (planEndInclusive - cycleStart).Days + 1);
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

    private static double SpreadRemainingUntilReset(double remainingPercent, DateTime today, DateTime realResetDate)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, realResetDate, remainingPercent);

        if (remainingPlanDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingPlanDays;
    }
}
