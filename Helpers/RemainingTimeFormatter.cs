using Quota.Localization;

namespace Quota.Helpers;

public static class RemainingTimeFormatter
{
    public static string Format(TimeSpan remaining, ILocalizationService localization)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        if (remaining.TotalHours >= 24)
        {
            var days = (int)Math.Ceiling(remaining.TotalDays);
            return PercentageFormatter.FormatUnit(days, "DaysPattern", localization);
        }

        if (remaining.TotalHours >= 1)
        {
            var totalMinutes = (int)Math.Floor(remaining.TotalMinutes);
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;

            if (minutes > 0)
                return localization.Format("HoursMinutesAbbreviatedFormat", hours, minutes);

            return PercentageFormatter.FormatUnit(hours, "HoursPattern", localization);
        }

        if (remaining.TotalMinutes >= 1)
            return PercentageFormatter.FormatUnit((int)Math.Floor(remaining.TotalMinutes), "MinutesPattern", localization);

        var seconds = remaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Floor(remaining.TotalSeconds);

        return PercentageFormatter.FormatUnit(seconds, "SecondsPattern", localization);
    }

    public static TimeSpan SuggestedRefreshInterval(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 24)
            return TimeSpan.FromMinutes(1);

        return TimeSpan.FromSeconds(1);
    }
}
