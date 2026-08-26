using Quota.Localization;

namespace Quota.Helpers;

public static class RemainingTimeFormatter
{
    public static string Format(TimeSpan remaining, ILocalizationService localization)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        if (remaining.TotalDays >= 1)
            return PercentageFormatter.FormatUnit((int)remaining.TotalDays, "DaysPattern", localization);

        if (remaining.TotalHours >= 1)
            return PercentageFormatter.FormatUnit((int)remaining.TotalHours, "HoursPattern", localization);

        if (remaining.TotalMinutes >= 1)
            return PercentageFormatter.FormatUnit((int)remaining.TotalMinutes, "MinutesPattern", localization);

        return PercentageFormatter.FormatUnit(
            (int)Math.Floor(remaining.TotalSeconds),
            "SecondsPattern",
            localization);
    }

    public static TimeSpan SuggestedRefreshInterval(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return TimeSpan.FromMinutes(1);

        return TimeSpan.FromSeconds(1);
    }
}
