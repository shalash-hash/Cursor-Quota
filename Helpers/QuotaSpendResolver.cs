using Quota.Models;

namespace Quota.Helpers;

/// <summary>
/// Каноническая бухгалтерия Cursor quota (Model C).
/// raw totalSpend — combined actual period spend; API не прибавляется повторно.
/// </summary>
public static class QuotaSpendResolver
{
    /// <summary>Raw combined period spend из Cursor totalSpend.</summary>
    public static decimal? ResolveTotalPeriodSpendUsd(QuotaUsage usage) =>
        usage.TotalSpendCents is long cents
            ? QuotaMonetaryHelper.CentsToUsd(cents)
            : null;

    public static decimal? ResolveApiUsedUsd(QuotaUsage usage)
    {
        if (usage.ApiSpendCents is long apiCents)
            return QuotaMonetaryHelper.CentsToUsd(apiCents);

        if (usage.ApiUsedAmountUsd is decimal apiUsed)
            return apiUsed;

        if (usage.ApiIncludedAmountUsd is decimal apiLimit)
            return QuotaMonetaryHelper.PercentToUsd(usage.ApiUsedPercent, apiLimit);

        return null;
    }

    /// <summary>
    /// Фактический расход Models. Direct autoSpend имеет приоритет над reconstruction.
    /// </summary>
    public static decimal? ResolveModelsActualUsedUsd(QuotaUsage usage)
    {
        if (usage.AutoSpendCents is long autoCents)
            return QuotaMonetaryHelper.CentsToUsd(autoCents);

        var total = ResolveTotalPeriodSpendUsd(usage);
        if (total is not null)
        {
            var api = ResolveApiUsedUsd(usage) ?? 0m;
            return Math.Max(0m, total.Value - api);
        }

        if (usage.ModelsActualUsedUsd is decimal explicitActual)
            return explicitActual;

        return usage.ModelsUsedUsd;
    }

    public static decimal ResolveModelsBaseUsedUsd(decimal modelsActualUsed, decimal? modelsBaseLimitUsd)
    {
        if (modelsBaseLimitUsd is not decimal baseLimit || baseLimit <= 0m)
            return modelsActualUsed;

        return Math.Min(modelsActualUsed, baseLimit);
    }

    public static decimal ResolveModelsBonusUsedUsd(decimal modelsActualUsed, decimal? modelsBaseLimitUsd)
    {
        if (modelsBaseLimitUsd is not decimal baseLimit || baseLimit <= 0m)
            return 0m;

        return Math.Max(0m, modelsActualUsed - baseLimit);
    }

    public static decimal? ResolveCombinedActualUsedUsd(QuotaUsage usage) =>
        ResolveTotalPeriodSpendUsd(usage);

    public static decimal? ResolveCombinedBaseUsedUsd(QuotaUsage usage)
    {
        var modelsActual = ResolveModelsActualUsedUsd(usage);
        if (modelsActual is null && usage.ApiUsedAmountUsd is null)
            return null;

        var modelsBaseLimit = QuotaMonetaryHelper.ResolveModelsBaseLimitUsd(usage);
        var modelsBaseUsed = modelsActual is decimal actual
            ? ResolveModelsBaseUsedUsd(actual, modelsBaseLimit)
            : 0m;

        var apiUsed = ResolveApiUsedUsd(usage) ?? 0m;
        var apiLimit = usage.ApiIncludedAmountUsd;
        var apiBaseUsed = apiLimit is decimal limit && limit > 0m
            ? Math.Min(apiUsed, limit)
            : apiUsed;

        return modelsBaseUsed + apiBaseUsed;
    }

    public static decimal? ResolveApiUsedUsdFromSnapshot(QuotaSnapshot snapshot)
    {
        if (snapshot.ApiSpendCents is long apiCents)
            return QuotaMonetaryHelper.CentsToUsd(apiCents);

        var apiLimit = ResolveApiLimitUsd(snapshot);
        if (apiLimit is null)
            return null;

        return QuotaMonetaryHelper.PercentToUsd(snapshot.ApiPercent, apiLimit.Value);
    }

    public static decimal? ResolveModelsActualUsedUsdFromSnapshot(QuotaSnapshot snapshot)
    {
        if (snapshot.AutoSpendCents is long autoCents)
            return QuotaMonetaryHelper.CentsToUsd(autoCents);

        if (snapshot.TotalSpendCents is not long totalCents)
            return null;

        var total = QuotaMonetaryHelper.CentsToUsd(totalCents);
        var api = ResolveApiUsedUsdFromSnapshot(snapshot) ?? 0m;
        return Math.Max(0m, total - api);
    }

    public static DaySpendUsd ResolveDaySpendUsd(QuotaSnapshot first, QuotaSnapshot last)
    {
        var combined = PositiveSpendDeltaUsd(first.TotalSpendCents, last.TotalSpendCents);
        var apiDelta = PositiveApiUsedDeltaUsd(first, last);
        var models = Math.Max(0m, combined - apiDelta);
        return new DaySpendUsd(combined, models, apiDelta);
    }

    public static DaySpendUsd ComputeSummedDaySpendUsd(
        IReadOnlyList<QuotaSnapshot> priorSnapshots,
        QuotaSnapshot current)
    {
        if (priorSnapshots.Count == 0)
            return default;

        var points = new List<QuotaSnapshot>(priorSnapshots.Count + 1);
        points.AddRange(priorSnapshots);
        points.Add(current);

        decimal combined = 0m;
        decimal models = 0m;
        decimal api = 0m;

        for (var i = 1; i < points.Count; i++)
        {
            var delta = ResolveDaySpendUsd(points[i - 1], points[i]);
            combined += delta.CombinedUsd;
            models += delta.ModelsUsd;
            api += delta.ApiUsd;
        }

        return new DaySpendUsd(combined, models, api);
    }

    public static decimal? ResolveModelsTodayUsd(QuotaUsage usage)
    {
        if (usage.TodayModelsSpendCents is long modelsCents)
            return QuotaMonetaryHelper.CentsToUsd(modelsCents);

        if (usage.TodayTotalSpendCents is long totalCents)
        {
            var totalDelta = QuotaMonetaryHelper.CentsToUsd(totalCents);
            var apiToday = ResolveApiTodayUsd(usage) ?? 0m;
            return Math.Max(0m, totalDelta - apiToday);
        }

        if (usage.ModelsEstimatedLimitUsd is decimal modelsLimit)
            return QuotaMonetaryHelper.PercentToUsd(usage.TodayFirstPartyUsedPercent, modelsLimit);

        return null;
    }

    public static decimal? ResolveApiTodayUsd(QuotaUsage usage)
    {
        if (usage.TodayApiSpendCents is long apiCents)
            return QuotaMonetaryHelper.CentsToUsd(apiCents);

        if (usage.ApiIncludedAmountUsd is not decimal apiLimit)
            return null;

        return QuotaMonetaryHelper.PercentToUsd(usage.TodayApiUsedPercent, apiLimit);
    }

    /// <summary>Combined today = delta raw totalSpend за billing day.</summary>
    public static decimal? ResolveCombinedTodayUsd(QuotaUsage usage)
    {
        if (usage.TodayTotalSpendCents is long totalCents)
            return QuotaMonetaryHelper.CentsToUsd(totalCents);

        // Legacy fallback: stored cents may have been total delta under old field name.
        if (usage.TodayModelsSpendCents is long legacyTotalCents && usage.TodayApiSpendCents is null)
        {
            var totalDelta = QuotaMonetaryHelper.CentsToUsd(legacyTotalCents);
            var apiToday = ResolveApiTodayUsd(usage) ?? 0m;
            if (apiToday > 0m)
                return totalDelta;

            return totalDelta;
        }

        var models = ResolveModelsTodayUsd(usage);
        var api = ResolveApiTodayUsd(usage);
        if (models is null && api is null)
            return null;

        // Only safe when components were derived from total delta, not summed independently.
        if (usage.TodayTotalSpendCents is null && usage.TodayModelsSpendCents is not null)
            return usage.TodayModelsSpendCents is long mc
                ? QuotaMonetaryHelper.CentsToUsd(mc) + (api ?? 0m)
                : (models ?? 0m) + (api ?? 0m);

        return (models ?? 0m) + (api ?? 0m);
    }

    public static decimal? ResolveModelsYesterdayUsd(QuotaUsage usage)
    {
        if (usage.YesterdayModelsSpendCents is long modelsCents)
            return QuotaMonetaryHelper.CentsToUsd(modelsCents);

        if (usage.YesterdayTotalSpendCents is long totalCents)
        {
            var totalDelta = QuotaMonetaryHelper.CentsToUsd(totalCents);
            var apiYesterday = ResolveApiYesterdayUsd(usage) ?? 0m;
            return Math.Max(0m, totalDelta - apiYesterday);
        }

        return QuotaMonetaryHelper.ResolveDaySpendUsd(
            null,
            usage.YesterdayFirstPartyUsedPercent,
            usage.ModelsEstimatedLimitUsd);
    }

    public static decimal? ResolveApiYesterdayUsd(QuotaUsage usage)
    {
        if (usage.YesterdayApiSpendCents is long apiCents)
            return QuotaMonetaryHelper.CentsToUsd(apiCents);

        if (usage.YesterdayApiUsedPercent <= 0.001 || usage.ApiIncludedAmountUsd is not decimal apiLimit)
            return usage.YesterdayApiUsedPercent <= 0.001 ? 0m : null;

        return QuotaMonetaryHelper.PercentToUsd(usage.YesterdayApiUsedPercent, apiLimit);
    }

    public static decimal? ResolveCombinedYesterdayUsd(QuotaUsage usage)
    {
        if (!usage.HasYesterdayUsageData && !usage.HasYesterdaySpendData)
            return null;

        if (usage.YesterdayTotalSpendCents is long totalCents)
            return QuotaMonetaryHelper.CentsToUsd(totalCents);

        if (usage.YesterdayModelsSpendCents is long legacyTotal && usage.YesterdayApiSpendCents is null)
            return QuotaMonetaryHelper.CentsToUsd(legacyTotal);

        var models = ResolveModelsYesterdayUsd(usage);
        var api = ResolveApiYesterdayUsd(usage);
        if (models is null && api is null)
            return null;

        return (models ?? 0m) + (api ?? 0m);
    }

    private static decimal? ResolveApiLimitUsd(QuotaSnapshot snapshot)
    {
        if (snapshot.LimitCents is long limitCents && limitCents > 0)
            return QuotaMonetaryHelper.CentsToUsd(limitCents);

        return null;
    }

    private static decimal PositiveSpendDeltaUsd(long? baseline, long? current)
    {
        if (baseline is not long first || current is not long last)
            return 0m;

        return Math.Max(0m, QuotaMonetaryHelper.CentsToUsd(last - first));
    }

    private static decimal PositiveApiUsedDeltaUsd(QuotaSnapshot first, QuotaSnapshot last)
    {
        var apiFirst = ResolveApiUsedUsdFromSnapshot(first) ?? 0m;
        var apiLast = ResolveApiUsedUsdFromSnapshot(last) ?? 0m;
        return Math.Max(0m, apiLast - apiFirst);
    }
}

public readonly record struct DaySpendUsd(decimal CombinedUsd, decimal ModelsUsd, decimal ApiUsd);
