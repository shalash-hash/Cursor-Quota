using Quota.Helpers;
using Quota.Models;
using Quota.Services.CursorApi;

namespace Quota.Services;

internal static class CursorPlanUsageMapper
{
    public static QuotaUsage Map(
        PlanUsage planUsage,
        DateTime periodStart,
        DateTime periodEnd,
        string? planName,
        long? planIncludedAmountCents)
    {
        var limitCents = ResolveLimitCents(planUsage, planIncludedAmountCents);
        var totalSpendCents = planUsage.TotalSpend;
        var includedSpendCents = planUsage.IncludedSpend;

        var autoPercent = planUsage.AutoPercentUsed ?? 0;
        var apiPercent = planUsage.ApiPercentUsed ?? 0;
        var totalPercent = ResolveTotalPercent(planUsage);

        decimal? apiIncludedUsd = limitCents is > 0
            ? limitCents.Value / 100m
            : null;

        decimal? apiUsedUsd = null;
        decimal? apiRemainingUsd = null;

        if (apiIncludedUsd is not null)
        {
            apiUsedUsd = Math.Round(apiIncludedUsd.Value * (decimal)apiPercent / 100m, 2);
            apiRemainingUsd = Math.Max(0m, apiIncludedUsd.Value - apiUsedUsd.Value);
        }

        decimal? modelsUsedUsd = null;
        decimal? modelsEstimatedLimitUsd = null;
        decimal? modelsEstimatedRemainingUsd = null;

        if (totalSpendCents is long spendCents)
        {
            modelsUsedUsd = QuotaMonetaryHelper.CentsToUsd(spendCents);
            modelsEstimatedLimitUsd = QuotaMonetaryHelper.EstimateLimitUsd(spendCents, autoPercent);
            modelsEstimatedRemainingUsd = QuotaMonetaryHelper.EstimateRemainingUsd(spendCents, autoPercent);
        }

        return new QuotaUsage
        {
            TotalUsedPercent = totalPercent,
            FirstPartyUsedPercent = autoPercent,
            ApiUsedPercent = apiPercent,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RetrievedAt = DateTime.Now,
            PlanName = planName,
            ApiIncludedAmountUsd = apiIncludedUsd,
            ApiUsedAmountUsd = apiUsedUsd,
            ApiRemainingAmountUsd = apiRemainingUsd,
            TotalSpendCents = totalSpendCents,
            IncludedSpendCents = includedSpendCents,
            LimitCents = limitCents,
            ModelsUsedUsd = modelsUsedUsd,
            ModelsEstimatedLimitUsd = modelsEstimatedLimitUsd,
            ModelsEstimatedRemainingUsd = modelsEstimatedRemainingUsd
        };
    }

    public static bool HasRequiredFields(PlanUsage planUsage)
    {
        if (planUsage.AutoPercentUsed is null || planUsage.ApiPercentUsed is null)
            return false;

        if (ResolveLimitCents(planUsage, null) is > 0 && planUsage.IncludedSpend is not null)
            return true;

        return planUsage.TotalPercentUsed is not null;
    }

    private static long? ResolveLimitCents(PlanUsage planUsage, long? planIncludedAmountCents)
    {
        if (planUsage.Limit is > 0)
            return planUsage.Limit;

        if (planIncludedAmountCents is > 0)
            return planIncludedAmountCents;

        return null;
    }

    private static double ResolveTotalPercent(PlanUsage planUsage)
    {
        var autoPercent = planUsage.AutoPercentUsed ?? 0;
        var apiPercent = planUsage.ApiPercentUsed ?? 0;

        // Cursor now tracks Cursor Models and Other Models as separate pools.
        // includedSpend/limit hits 100% once bonus usage starts, so it must not drive Total.
        if (autoPercent > 0 || apiPercent > 0)
            return Math.Max(autoPercent, apiPercent);

        return planUsage.TotalPercentUsed ?? 0;
    }
}
