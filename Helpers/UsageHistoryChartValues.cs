using Quota.Models;

namespace Quota.Helpers;

/// <summary>
/// Canonical USD values for the daily spend history chart geometry.
/// Tooltip text may still show percents; bar heights use only USD.
/// </summary>
public static class UsageHistoryChartValues
{
    public static decimal ResolveModelsDayUsd(UsageHistoryPoint point) =>
        point.DailyModelsSpentUsd ?? 0m;

    public static decimal ResolveApiDayUsd(UsageHistoryPoint point) =>
        point.DailyApiSpentUsd ?? 0m;

    public static decimal ResolveTotalDayUsd(UsageHistoryPoint point)
    {
        if (point.DailyTotalSpentUsd is decimal total)
            return total;

        return ResolveModelsDayUsd(point) + ResolveApiDayUsd(point);
    }

    public static double ResolveAxisMaxUsd(IReadOnlyList<UsageHistoryPoint> points)
    {
        if (points.Count == 0)
            return 0.01;

        var max = points.Max(point => (double)ResolveTotalDayUsd(point));
        return Math.Max(max, 0.01);
    }

    public static double ResolveBarHeightFraction(decimal segmentUsd, double axisMaxUsd) =>
        axisMaxUsd <= 0 ? 0 : (double)segmentUsd / axisMaxUsd;

    public static string FormatUsdAxisTick(double value, System.Globalization.CultureInfo culture)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.001)
            return QuotaMonetaryHelper.FormatUsd((decimal)Math.Round(value), culture);

        return QuotaMonetaryHelper.FormatUsd((decimal)value, culture);
    }
}
