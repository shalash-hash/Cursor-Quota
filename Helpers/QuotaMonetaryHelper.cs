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

    public static decimal? ResolveModelsBaseLimitUsd(QuotaUsage usage) =>
        usage.ModelsBaseLimitUsd ?? usage.ModelsEstimatedLimitUsd;

    public static decimal? ResolveModelsRemainingUsd(QuotaUsage usage)
    {
        var modelsActual = QuotaSpendResolver.ResolveModelsActualUsedUsd(usage);
        if (modelsActual is not decimal actualUsd)
            return null;

        if (ResolveModelsBaseLimitUsd(usage) is not decimal limitUsd)
            return null;

        return Math.Max(0m, limitUsd - Math.Min(actualUsd, limitUsd));
    }

    public static CombinedQuotaDisplay ResolveCombinedDisplay(QuotaUsage usage)
    {
        var baseUsedUsd = ResolveCombinedBaseUsedUsd(usage);
        var limitUsd = ResolveCombinedBaseLimitUsd(usage);
        var usedPercent = ResolveCombinedUsedPercent(usage) ?? usage.TotalUsedPercent;
        var remainingPercent = Math.Max(0, 100 - usedPercent);

        decimal? remainingUsd = baseUsedUsd is not null && limitUsd is not null
            ? Math.Max(0m, limitUsd.Value - baseUsedUsd.Value)
            : null;

        return new CombinedQuotaDisplay(
            usedPercent,
            baseUsedUsd,
            limitUsd,
            remainingUsd,
            remainingPercent,
            usage.ModelsBonusUsedUsd,
            usage.ApiBonusUsedUsd,
            usage.BonusAvailability);
    }

    public static decimal? ResolveCombinedBaseLimitUsd(QuotaUsage usage)
    {
        decimal? limitUsd = null;

        if (ResolveModelsBaseLimitUsd(usage) is decimal modelsLimit)
            limitUsd = modelsLimit;

        if (usage.ApiIncludedAmountUsd is decimal apiLimit)
            limitUsd = (limitUsd ?? 0m) + apiLimit;

        if (usage.ApiKnownBonusAllowanceUsd is decimal apiBonusAllowance)
            limitUsd = (limitUsd ?? 0m) + apiBonusAllowance;

        return limitUsd;
    }

    public static decimal? ResolveCombinedLimitUsd(QuotaUsage usage) =>
        ResolveCombinedBaseLimitUsd(usage);

    public static decimal? ResolveCombinedBaseUsedUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveCombinedBaseUsedUsd(usage);

    public static decimal? ResolveCombinedUsedUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveCombinedActualUsedUsd(usage);

    public static double? ResolveCombinedUsedPercent(QuotaUsage usage)
    {
        var baseUsedUsd = ResolveCombinedBaseUsedUsd(usage);
        var baseLimitUsd = ResolveCombinedBaseLimitUsd(usage);
        if (baseUsedUsd is null || baseLimitUsd is null or <= 0m)
            return null;

        return (double)(baseUsedUsd.Value / baseLimitUsd.Value * 100m);
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

    /// <summary>Сумма фактического дневного расхода за billing day = delta raw totalSpend.</summary>
    public static decimal? ResolveModelsTodayUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveModelsTodayUsd(usage);

    public static decimal? ResolveApiTodayUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveApiTodayUsd(usage);

    public static decimal? ResolveTodayUsageUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveCombinedTodayUsd(usage);

    /// <summary>Combined today % от combined limit; USD — канон, не сумма pool-percent.</summary>
    public static double? ResolveCombinedTodayPercent(QuotaUsage usage)
    {
        var todayUsd = ResolveTodayUsageUsd(usage);
        var limitUsd = ResolveCombinedLimitUsd(usage);
        if (todayUsd is null || limitUsd is null or <= 0m)
            return null;

        if (todayUsd <= 0m)
            return 0;

        return (double)(todayUsd.Value / limitUsd.Value * 100m);
    }

    public static double ResolveCombinedTodayPercentOrFallback(QuotaUsage usage) =>
        ResolveCombinedTodayPercent(usage)
        ?? ResolveCombinedDayPercent(
            usage.TodayFirstPartyUsedPercent,
            usage.TodayApiUsedPercent,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

    public static double ResolveCombinedTodayPercentFromParts(
        long todayTotalSpendCents,
        long todayModelsSpendCents,
        long todayApiSpendCents,
        double todayModelsPercent,
        double todayApiPercent,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd)
    {
        decimal? combinedUsd = todayTotalSpendCents > 0
            ? CentsToUsd(todayTotalSpendCents)
            : null;

        if (combinedUsd is null)
        {
            decimal? modelsUsd = todayModelsSpendCents > 0
                ? CentsToUsd(todayModelsSpendCents)
                : null;
            if (modelsUsd is null && modelsLimitUsd is decimal modelsLimit)
                modelsUsd = PercentToUsd(todayModelsPercent, modelsLimit);

            decimal? apiUsd = todayApiSpendCents > 0
                ? CentsToUsd(todayApiSpendCents)
                : apiLimitUsd is decimal apiLimit
                    ? PercentToUsd(todayApiPercent, apiLimit)
                    : null;

            if (modelsUsd is null && apiUsd is null)
            {
                return ResolveCombinedDayPercent(
                    todayModelsPercent,
                    todayApiPercent,
                    modelsLimitUsd,
                    apiLimitUsd);
            }

            combinedUsd = (modelsUsd ?? 0m) + (apiUsd ?? 0m);
        }

        var combinedLimit = ResolveCombinedLimitFromParts(modelsLimitUsd, apiLimitUsd);
        if (combinedLimit is null or <= 0m)
        {
            return ResolveCombinedDayPercent(
                todayModelsPercent,
                todayApiPercent,
                modelsLimitUsd,
                apiLimitUsd);
        }

        if (combinedUsd <= 0m)
            return 0;

        return (double)(combinedUsd.Value / combinedLimit.Value * 100m);
    }

    /// <summary>Legacy overload — todayModelsSpendCents treated as total delta when api cents absent.</summary>
    public static double ResolveCombinedTodayPercentFromParts(
        long todayModelsSpendCents,
        double todayModelsPercent,
        double todayApiPercent,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd) =>
        ResolveCombinedTodayPercentFromParts(
            todayModelsSpendCents,
            0,
            0,
            todayModelsPercent,
            todayApiPercent,
            modelsLimitUsd,
            apiLimitUsd);

    /// <summary>Алиас для combined daily plan USD — единая точка для ahead/behind.</summary>
    public static decimal ResolveDailyPlanUsd(
        double modelsDailyPlanPercent,
        double apiDailyPlanPercent,
        decimal? modelsLimitUsd,
        decimal? apiLimitUsd) =>
        ResolveCombinedDailyTargetUsd(
            modelsDailyPlanPercent,
            apiDailyPlanPercent,
            modelsLimitUsd,
            apiLimitUsd);

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
    double RemainingPercent,
    decimal? ModelsBonusUsedUsd = null,
    decimal? ApiBonusUsedUsd = null,
    BonusAvailability ModelsBonusAvailability = BonusAvailability.None);
