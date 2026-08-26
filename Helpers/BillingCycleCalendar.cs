namespace Quota.Helpers;

/// <summary>
/// Сутки тарификации Cursor: от времени billingCycleStart до того же времени следующего дня.
/// </summary>
public static class BillingCycleCalendar
{
    public static DateTime GetDayStart(DateTime now, DateTime periodStart)
    {
        var candidate = CombineDateAndTimeOfDay(now.Date, periodStart.TimeOfDay, now.Kind);
        return now >= candidate ? candidate : candidate.AddDays(-1);
    }

    public static DateTime GetDayEnd(DateTime now, DateTime periodStart)
        => GetDayStart(now, periodStart).AddDays(1);

    public static DateTime GetPreviousDayStart(DateTime now, DateTime periodStart)
        => GetDayStart(now, periodStart).AddDays(-1);

    public static int CountRemainingDays(DateTime now, DateTime periodEnd, double remainingPercent)
    {
        if (periodEnd <= now)
            return remainingPercent > 0 ? 1 : 0;

        return Math.Max(1, (int)Math.Ceiling((periodEnd - now).TotalDays));
    }

    private static DateTime CombineDateAndTimeOfDay(DateTime date, TimeSpan timeOfDay, DateTimeKind kind)
    {
        var combined = date.Add(timeOfDay);
        if (kind == DateTimeKind.Unspecified)
            return combined;

        return DateTime.SpecifyKind(combined, kind);
    }
}
