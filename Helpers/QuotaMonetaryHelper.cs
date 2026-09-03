using System.Globalization;
using Quota.Models;

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

    /// Если есть расход в центах — берём его; иначе оцениваем по проценту и лимиту.
    public static decimal? ResolveDaySpendUsd(
        long? spendCents,
        double percent,
        decimal? modelsLimitUsd)
    {
        if (spendCents is > 0)
            return CentsToUsd(spendCents.Value);

        if (modelsLimitUsd is null)
            return null;

        return PercentToUsd(percent, modelsLimitUsd.Value);
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

    public static decimal? ResolveModelsRemainingUsd(QuotaUsage usage)
    {
        if (usage.ModelsUsedUsd is not decimal usedUsd || usage.ModelsEstimatedLimitUsd is not decimal limitUsd)
            return null;

        return Math.Max(0m, limitUsd - usedUsd);
    }

    public static CombinedQuotaDisplay ResolveCombinedDisplay(QuotaUsage usage)
    {
        var usedUsd = ResolveCombinedUsedUsd(usage);
        var limitUsd = ResolveCombinedLimitUsd(usage);
        var usedPercent = ResolveCombinedUsedPercent(usage) ?? usage.TotalUsedPercent;
        var remainingPercent = Math.Max(0, 100 - usedPercent);

        decimal? remainingUsd = usedUsd is not null && limitUsd is not null
            ? Math.Max(0m, limitUsd.Value - usedUsd.Value)
            : null;

        return new CombinedQuotaDisplay(
            usedPercent,
            usedUsd,
            limitUsd,
            remainingUsd,
            remainingPercent);
    }

    public static decimal? ResolveCombinedLimitUsd(QuotaUsage usage)
    {
        decimal? limitUsd = null;

        if (usage.ModelsEstimatedLimitUsd is decimal modelsLimit)
            limitUsd = modelsLimit;

        if (usage.ApiIncludedAmountUsd is decimal apiLimit)
            limitUsd = (limitUsd ?? 0m) + apiLimit;

        return limitUsd;
    }

    public static decimal? ResolveCombinedUsedUsd(QuotaUsage usage)
    {
        if (usage.ModelsUsedUsd is decimal modelsUsed || usage.ApiUsedAmountUsd is decimal apiUsed)
            return (usage.ModelsUsedUsd ?? 0m) + (usage.ApiUsedAmountUsd ?? 0m);

        var limitUsd = ResolveCombinedLimitUsd(usage);
        if (limitUsd is null or <= 0m)
            return null;

        var modelsLimit = usage.ModelsEstimatedLimitUsd ?? 0m;
        var apiLimit = usage.ApiIncludedAmountUsd ?? 0m;
        if (modelsLimit <= 0m && apiLimit <= 0m)
            return null;

        return PercentToUsd(usage.FirstPartyUsedPercent, modelsLimit)
            + PercentToUsd(usage.ApiUsedPercent, apiLimit);
    }

    public static double? ResolveCombinedUsedPercent(QuotaUsage usage)
    {
        var usedUsd = ResolveCombinedUsedUsd(usage);
        var limitUsd = ResolveCombinedLimitUsd(usage);
        if (usedUsd is null || limitUsd is null or <= 0m)
            return null;

        return (double)(usedUsd.Value / limitUsd.Value * 100m);
    }

    public static double ResolveCombinedLinearDailyTarget(double remainingPercent, int remainingDays)
    {
        if (remainingPercent <= 0 || remainingDays <= 0)
            return 0;

        return remainingPercent / remainingDays;
    }

    public static double ResolveCombinedDayPercent(
        double modelsDayPercent,
        double apiDayPercent,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd)
    {
        var combinedLimit = ResolveCombinedLimitFromParts(modelsLimitUsd, apiLimitUsd);
        if (combinedLimit is null or <= 0m)
            return modelsDayPercent + apiDayPercent;

        var usedUsd = ResolveCombinedDailyTargetUsd(
            modelsDayPercent,
            apiDayPercent,
            modelsLimitUsd,
            apiLimitUsd);

        return (double)(usedUsd / combinedLimit.Value * 100m);
    }

    public static decimal ResolveCombinedDailyTargetUsd(
        double modelsDayPercent,
        double apiDayPercent,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd)
    {
        var modelsLimit = modelsLimitUsd ?? 0m;
        var apiLimit = apiLimitUsd ?? 0m;
        return PercentToUsd(modelsDayPercent, modelsLimit) + PercentToUsd(apiDayPercent, apiLimit);
    }

    private static decimal? ResolveCombinedLimitFromParts(decimal? modelsLimitUsd, decimal? apiLimitUsd)
    {
        decimal? limitUsd = null;

        if (modelsLimitUsd is decimal modelsLimit)
            limitUsd = modelsLimit;

        if (apiLimitUsd is decimal apiLimit)
            limitUsd = (limitUsd ?? 0m) + apiLimit;

        return limitUsd;
    }
}

public readonly record struct CombinedQuotaDisplay(
    double UsedPercent,
    decimal? UsedUsd,
    decimal? LimitUsd,
    decimal? RemainingUsd,
    double RemainingPercent);
