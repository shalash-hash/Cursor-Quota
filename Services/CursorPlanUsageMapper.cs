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
        long periodEndUnixMilliseconds,
        string? planName,
        long? planIncludedAmountCents)
    {
        var limitCents = ResolveLimitCents(planUsage, planIncludedAmountCents);
        var totalSpendCents = planUsage.TotalSpend;
        var includedSpendCents = planUsage.IncludedSpend;

        var autoPercent = planUsage.AutoPercentUsed ?? 0;
        var apiPercent = planUsage.ApiPercentUsed ?? 0;

        decimal? apiIncludedUsd = limitCents is > 0
            ? limitCents.Value / 100m
            : null;

        decimal? apiUsedUsd = null;
        decimal? apiRemainingUsd = null;

        if (planUsage.ApiSpend is long apiSpendCents)
        {
            apiUsedUsd = QuotaMonetaryHelper.CentsToUsd(apiSpendCents);
            if (apiIncludedUsd is not null)
                apiRemainingUsd = Math.Max(0m, apiIncludedUsd.Value - apiUsedUsd.Value);
        }
        else if (apiIncludedUsd is not null)
        {
            apiUsedUsd = Math.Round(apiIncludedUsd.Value * (decimal)apiPercent / 100m, 2);
            apiRemainingUsd = Math.Max(0m, apiIncludedUsd.Value - apiUsedUsd.Value);
        }

        decimal? modelsEstimatedLimitUsd = null;
        decimal? modelsEstimatedRemainingUsd = null;

        if (totalSpendCents is long spendCents && autoPercent < 99.999)
        {
            modelsEstimatedLimitUsd = QuotaMonetaryHelper.EstimateLimitUsd(spendCents, autoPercent);
            modelsEstimatedRemainingUsd = QuotaMonetaryHelper.EstimateRemainingUsd(spendCents, autoPercent);
        }

        var bonusSource = QuotaBonusHelper.ClassifyBonusSourceFromPlanUsage(planUsage);

        var usage = new QuotaUsage
        {
            FirstPartyUsedPercent = autoPercent,
            ApiUsedPercent = apiPercent,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PeriodEndUnixMilliseconds = periodEndUnixMilliseconds,
            RetrievedAt = DateTime.Now,
            PlanName = planName,
            ApiIncludedAmountUsd = apiIncludedUsd,
            ApiUsedAmountUsd = apiUsedUsd,
            ApiRemainingAmountUsd = apiRemainingUsd,
            TotalSpendCents = totalSpendCents,
            IncludedSpendCents = includedSpendCents,
            LimitCents = limitCents,
            AutoSpendCents = planUsage.AutoSpend,
            ApiSpendCents = planUsage.ApiSpend,
            AutoLimitCents = planUsage.AutoLimit,
            ApiLimitCents = planUsage.ApiLimit,
            ModelsEstimatedLimitUsd = modelsEstimatedLimitUsd,
            ModelsEstimatedRemainingUsd = modelsEstimatedRemainingUsd,
            BonusSpendCents = planUsage.BonusSpend,
            RemainingBonus = planUsage.RemainingBonus,
            BonusTooltip = planUsage.BonusTooltip,
            BonusSource = bonusSource,
            RawTotalPercentUsed = planUsage.TotalPercentUsed,
            TotalUsedPercent = 0
        };

        var modelsActual = QuotaSpendResolver.ResolveModelsActualUsedUsd(usage);

        usage = new QuotaUsage
        {
            TotalUsedPercent = QuotaMonetaryHelper.ResolveCombinedUsedPercent(usage)
                ?? planUsage.TotalPercentUsed
                ?? autoPercent,
            FirstPartyUsedPercent = usage.FirstPartyUsedPercent,
            ApiUsedPercent = usage.ApiUsedPercent,
            PeriodStart = usage.PeriodStart,
            PeriodEnd = usage.PeriodEnd,
            PeriodEndUnixMilliseconds = usage.PeriodEndUnixMilliseconds,
            RetrievedAt = usage.RetrievedAt,
            PlanName = usage.PlanName,
            ApiIncludedAmountUsd = usage.ApiIncludedAmountUsd,
            ApiUsedAmountUsd = usage.ApiUsedAmountUsd,
            ApiRemainingAmountUsd = usage.ApiRemainingAmountUsd,
            TotalSpendCents = usage.TotalSpendCents,
            IncludedSpendCents = usage.IncludedSpendCents,
            LimitCents = usage.LimitCents,
            AutoSpendCents = usage.AutoSpendCents,
            ApiSpendCents = usage.ApiSpendCents,
            AutoLimitCents = usage.AutoLimitCents,
            ApiLimitCents = usage.ApiLimitCents,
            ModelsActualUsedUsd = modelsActual,
            ModelsUsedUsd = modelsActual,
            ModelsEstimatedLimitUsd = usage.ModelsEstimatedLimitUsd,
            ModelsEstimatedRemainingUsd = usage.ModelsEstimatedRemainingUsd,
            BonusSpendCents = usage.BonusSpendCents,
            RemainingBonus = usage.RemainingBonus,
            BonusTooltip = usage.BonusTooltip,
            BonusSource = usage.BonusSource,
            RawTotalPercentUsed = usage.RawTotalPercentUsed
        };

        return usage;
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
}

