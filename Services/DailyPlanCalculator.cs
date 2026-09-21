using Quota.Helpers;

namespace Quota.Services;

public static class DailyPlanCalculator
{
    public const int AcceleratedPhaseCalendarDays = 21;

    /// <summary>
    /// Момент окончания ускоренной фазы (exclusive): первый instant Phase 2 / reserve.
    /// 21 полный billing-day bucket ⇒ cycleStart + 21 days (день 21: [start+20d, start+21d)).
    /// </summary>
    public static DateTime GetAcceleratedPhaseEndExclusive(DateTime cycleStart, DateTime realResetDate)
    {
        var candidate = cycleStart.AddDays(AcceleratedPhaseCalendarDays);
        return candidate < realResetDate ? candidate : realResetDate;
    }

    /// <summary>
    /// Начало 21-го (последнего) billing-day ускоренной фазы; для отображения «день 26.09» при старте 06.09.
    /// Не использовать как planEnd для CountRemainingDays — нужен <see cref="GetAcceleratedPhaseEndExclusive"/>.
    /// </summary>
    public static DateTime GetAcceleratedEndInclusive(DateTime cycleStart, DateTime realResetDate)
    {
        var phaseEndExclusive = GetAcceleratedPhaseEndExclusive(cycleStart, realResetDate);
        if (phaseEndExclusive <= cycleStart)
            return phaseEndExclusive;

        return phaseEndExclusive.AddDays(-1);
    }

    public static DateTime GetReservePhaseStart(DateTime cycleStart, DateTime realResetDate)
        => GetAcceleratedPhaseEndExclusive(cycleStart, realResetDate);

    public static bool IsWithinAcceleratedPhase(DateTime today, DateTime cycleStart, DateTime realResetDate)
        => today < GetAcceleratedPhaseEndExclusive(cycleStart, realResetDate);

    public static bool IsWithinReservePhase(DateTime today, DateTime cycleStart, DateTime realResetDate)
    {
        var phaseEndExclusive = GetAcceleratedPhaseEndExclusive(cycleStart, realResetDate);
        return today >= phaseEndExclusive && today < realResetDate;
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

        var acceleratedPhaseEndExclusive = GetAcceleratedPhaseEndExclusive(cycleStart, realResetDate);

        if (today < acceleratedPhaseEndExclusive)
            return SpreadRemainingUntilReset(cursorRemaining, today, acceleratedPhaseEndExclusive);

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

    private static double SpreadRemainingUntilReset(double remainingPercent, DateTime today, DateTime realResetDate)
    {
        var remainingPlanDays = CalculateRemainingPlanDays(today, realResetDate, remainingPercent);

        if (remainingPlanDays <= 0)
            return remainingPercent;

        return remainingPercent / remainingPlanDays;
    }
}
