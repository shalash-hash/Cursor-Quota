using System.Globalization;

namespace Quota.Helpers;

public static class QuotaMonetaryHelper
{
    public const int DisplayDecimalPlaces = 2;

    public static decimal CentsToUsd(long cents) => cents / 100m;

    /// Оценка месячного лимита пула моделей: spend × 100 / percent (целые центы).
    public static long? EstimateLimitCents(long spendCents, double usedPercent)
    {
        if (spendCents <= 0 || usedPercent <= 0.0001)
            return null;

        var limit = spendCents * 100d / usedPercent;
        if (!double.IsFinite(limit) || limit <= 0)
            return null;

        return (long)Math.Round(limit, MidpointRounding.AwayFromZero);
    }

    public static decimal? EstimateLimitUsd(long spendCents, double usedPercent)
    {
        var limitCents = EstimateLimitCents(spendCents, usedPercent);
        return limitCents is null ? null : CentsToUsd(limitCents.Value);
    }

    public static decimal? EstimateRemainingUsd(long spendCents, double usedPercent)
    {
        var limitCents = EstimateLimitCents(spendCents, usedPercent);
        if (limitCents is null)
            return null;

        return CentsToUsd(Math.Max(0, limitCents.Value - spendCents));
    }

    public static decimal PercentToUsd(double percent, decimal limitUsd)
    {
        if (percent <= 0)
            return 0m;

        return Math.Round(limitUsd * (decimal)percent / 100m, DisplayDecimalPlaces, MidpointRounding.AwayFromZero);
    }

    public static string FormatUsd(decimal value, CultureInfo culture) =>
        PercentageFormatter.FormatUsd(value, culture);

    public static string FormatSpendRange(decimal usedUsd, decimal? limitUsd, CultureInfo culture)
    {
        var used = FormatUsd(usedUsd, culture);
        if (limitUsd is null)
            return used;

        return string.Format(
            culture,
            "{0} из ~{1}",
            used,
            FormatUsd(limitUsd.Value, culture));
    }

    public static string FormatPercentWithUsd(
        double percent,
        decimal? limitUsd,
        int decimalPlaces,
        CultureInfo culture)
    {
        var percentText = PercentageFormatter.Format(percent, decimalPlaces, culture);
        if (limitUsd is null)
            return percentText;

        var amountText = FormatUsd(PercentToUsd(percent, limitUsd.Value), culture);
        return string.Format(culture, "{0} ({1})", percentText, amountText);
    }
}
