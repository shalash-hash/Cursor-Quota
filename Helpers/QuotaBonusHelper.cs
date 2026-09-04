using Quota.Models;
using Quota.Services.CursorApi;

namespace Quota.Helpers;

public static class QuotaBonusHelper
{
    private const double ModelsPercentCap = 99.999;

    /// <summary>bonusSpend — накопительный provider-subsidized spend, НЕ bonus allowance.</summary>
    public static BonusSource ClassifyBonusSource(
        double autoPercentUsed,
        bool hasAutoBucketModelsHint = true)
    {
        if (autoPercentUsed <= 0.0001)
            return BonusSource.None;

        return hasAutoBucketModelsHint ? BonusSource.Models : BonusSource.Unknown;
    }

    public static BonusAvailability ResolveBonusAvailability(bool? remainingBonus, decimal bonusUsedUsd)
    {
        if (remainingBonus == true)
            return BonusAvailability.Available;

        if (bonusUsedUsd > 0m)
            return BonusAvailability.Unknown;

        return BonusAvailability.None;
    }

    public static decimal ResolveModelsBonusUsedUsd(decimal? modelsActualUsedUsd, decimal? modelsBaseLimitUsd)
    {
        if (modelsActualUsedUsd is not decimal actual || modelsBaseLimitUsd is not decimal baseLimit || baseLimit <= 0m)
            return 0m;

        return QuotaSpendResolver.ResolveModelsBonusUsedUsd(actual, baseLimit);
    }

    /// <summary>Оценка base limit только пока autoPercent &lt; 100%.</summary>
    public static decimal? EstimateLiveBaseLimitUsd(long? spendCents, double autoPercentUsed)
    {
        if (autoPercentUsed >= ModelsPercentCap)
            return null;

        if (spendCents is not long cents)
            return null;

        return QuotaMonetaryHelper.EstimateLimitUsd(cents, autoPercentUsed);
    }

    public static decimal? ResolveFrozenOrEstimatedBaseLimitUsd(
        QuotaUsage usage,
        long? frozenBaseLimitCents)
    {
        if (frozenBaseLimitCents is long frozenCents && frozenCents > 0)
            return QuotaMonetaryHelper.CentsToUsd(frozenCents);

        return EstimateLiveBaseLimitUsd(usage.TotalSpendCents, usage.FirstPartyUsedPercent);
    }

    public static PoolQuotaBreakdown ResolveModelsBreakdown(QuotaUsage usage)
    {
        var baseLimit = usage.ModelsBaseLimitUsd;
        var actualUsed = QuotaSpendResolver.ResolveModelsActualUsedUsd(usage) ?? 0m;
        var bonusUsed = usage.ModelsBonusUsedUsd
            ?? ResolveModelsBonusUsedUsd(actualUsed, baseLimit);
        var baseUsed = baseLimit is decimal limit
            ? QuotaSpendResolver.ResolveModelsBaseUsedUsd(actualUsed, limit)
            : actualUsed;
        var baseRemaining = baseLimit is decimal baseLim
            ? Math.Max(0m, baseLim - baseUsed)
            : 0m;

        return new PoolQuotaBreakdown(
            baseLimit,
            baseUsed,
            baseRemaining,
            bonusUsed,
            usage.BonusSource,
            usage.BonusAvailability,
            HasKnownBonusAllowance: false,
            KnownBonusAllowanceUsd: null);
    }

    public static PoolQuotaBreakdown ResolveApiBreakdown(QuotaUsage usage)
    {
        var baseLimit = usage.ApiIncludedAmountUsd;
        var used = QuotaSpendResolver.ResolveApiUsedUsd(usage) ?? 0m;
        var bonusUsed = usage.ApiBonusUsedUsd ?? 0m;
        var baseUsed = Math.Max(0m, used - bonusUsed);
        var baseRemaining = baseLimit is decimal limit
            ? Math.Max(0m, limit - baseUsed)
            : 0m;

        return new PoolQuotaBreakdown(
            baseLimit,
            baseUsed,
            baseRemaining,
            bonusUsed,
            usage.ApiBonusSource,
            BonusAvailability.None,
            HasKnownBonusAllowance: usage.ApiKnownBonusAllowanceUsd is not null,
            KnownBonusAllowanceUsd: usage.ApiKnownBonusAllowanceUsd);
    }

    public static decimal? ResolveApiEffectiveLimitUsd(QuotaUsage usage)
    {
        var api = ResolveApiBreakdown(usage);
        if (api.BaseLimitUsd is not decimal baseLimit)
            return null;

        return baseLimit + (api.KnownBonusAllowanceUsd ?? 0m);
    }

    internal static BonusSource ClassifyBonusSourceFromPlanUsage(PlanUsage planUsage)
    {
        if (planUsage.BonusSpend is not > 0 && planUsage.RemainingBonus is not true)
            return BonusSource.None;

        if (planUsage.AutoBucketModels == false)
            return BonusSource.Unknown;

        return BonusSource.Models;
    }

    public static bool IsModelsPercentCapped(double autoPercentUsed) =>
        autoPercentUsed >= ModelsPercentCap;
}
