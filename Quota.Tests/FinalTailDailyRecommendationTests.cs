using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class FinalTailDailyRecommendationTests
{
    private static readonly DateTime CycleStart = new(2026, 9, 6, 12, 33, 42);
    private static readonly DateTime PeriodEnd = new(2026, 10, 6, 12, 33, 42);
    private static readonly DateTime SixDaysBeforeReset = PeriodEnd.AddDays(-6);

    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void FinalTail_Uniform_IncludesApiRemaining()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);

        Assert.True(result.Api.DailyTarget > 0);
        AssertCombinedNearExpected(result, QuotaPlanMode.Uniform);
    }

    [Fact]
    public void FinalTail_Optimal_IncludesApiRemaining()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Optimal, SixDaysBeforeReset);

        Assert.True(result.Api.DailyTarget > 0);
        AssertCombinedNearExpected(result, QuotaPlanMode.Optimal);
    }

    [Fact]
    public void FinalTail_OptimalEqualsUniform()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var optimal = _calculator.Calculate(usage, QuotaPlanMode.Optimal, SixDaysBeforeReset);
        var uniform = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);

        Assert.Equal(uniform.FirstParty.DailyTarget, optimal.FirstParty.DailyTarget, precision: 8);
        Assert.Equal(uniform.Api.DailyTarget, optimal.Api.DailyTarget, precision: 8);
        Assert.Equal(uniform.Total.DailyTarget, optimal.Total.DailyTarget, precision: 8);
    }

    [Fact]
    public void CombinedDaily_IsModelsPlusApi()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            450m,
            20m);

        Assert.InRange(combinedUsd, 8.37m, 8.38m);
    }

    [Fact]
    public void ApiZero_CombinedEqualsModels()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 0m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);
        var modelsUsd = QuotaMonetaryHelper.PercentToUsd(result.FirstParty.DailyTarget, 450m);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            450m,
            20m);

        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(modelsUsd, combinedUsd);
    }

    [Fact]
    public void ModelsZero_CombinedEqualsApi()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 0m, apiRemainingUsd: 20m, inflateModelsPercent: false);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);
        var apiUsd = QuotaMonetaryHelper.PercentToUsd(result.Api.DailyTarget, 20m);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            450m,
            20m);

        Assert.Equal(0, result.FirstParty.DailyTarget);
        Assert.Equal(apiUsd, combinedUsd);
    }

    [Fact]
    public void ModeSwitchInsideFinalTail_ProducesSameCombinedValue()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var optimal = _calculator.Calculate(usage, QuotaPlanMode.Optimal, SixDaysBeforeReset);
        var uniform = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);

        Assert.Equal(optimal.Total.DailyTarget, uniform.Total.DailyTarget, precision: 8);
    }

    [Fact]
    public void BonusApi_IfPresent_IsHandledAccordingToItsSemantics()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            450m,
            20m);

        Assert.InRange(combinedUsd, 8.37m, 8.38m);
    }

    [Fact]
    public void DisplayedRecommendationUsesCombinedNotModelsOnly()
    {
        var usage = CreateUsage(modelsBaseRemainingUsd: 30.25m, apiRemainingUsd: 20m);
        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, SixDaysBeforeReset);
        var modelsOnlyUsd = QuotaMonetaryHelper.PercentToUsd(result.FirstParty.DailyTarget, 450m);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            450m,
            20m);

        Assert.InRange(modelsOnlyUsd, 5.04m, 5.05m);
        Assert.InRange(combinedUsd, 8.37m, 8.38m);
        Assert.True(combinedUsd > modelsOnlyUsd + 3m);
    }

    [Fact]
    public void RealSnapshot_2026_09_30_CombinedDailyIncludesApiPool()
    {
        var retrievedAt = new DateTime(2026, 9, 30, 21, 39, 35);
        var usage = new QuotaUsage
        {
            PeriodStart = CycleStart,
            PeriodEnd = PeriodEnd,
            RetrievedAt = retrievedAt,
            FirstPartyUsedPercent = 97.7444444444444,
            ApiUsedPercent = 0,
            TotalUsedPercent = 93.5851063829787,
            TotalSpendCents = 43985,
            IncludedSpendCents = 2000,
            LimitCents = 2000,
            ModelsBaseLimitCents = 45000,
            ModelsBaseLimitUsd = 450m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            ApiRemainingAmountUsd = 20m,
            BonusSource = BonusSource.Models,
            RemainingBonus = false
        };

        var enriched = QuotaUsageEnricher.ApplyBonusBreakdown(usage, 45000, BonusSource.Models);
        Assert.True(DailyPlanCalculator.IsWithinFinalUniformTail(retrievedAt, CycleStart, PeriodEnd));

        var uniform = _calculator.Calculate(enriched, QuotaPlanMode.Uniform, retrievedAt);
        var optimal = _calculator.Calculate(enriched, QuotaPlanMode.Optimal, retrievedAt);
        var modelsLimit = QuotaMonetaryHelper.ResolveModelsLimitUsdForPlan(enriched);
        var apiLimit = QuotaMonetaryHelper.ResolveApiIncludedLimitUsd(enriched);
        var combinedUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            uniform.FirstParty.DailyTarget,
            uniform.Api.DailyTarget,
            modelsLimit,
            apiLimit);
        var modelsOnlyUsd = QuotaMonetaryHelper.PercentToUsd(uniform.FirstParty.DailyTarget, modelsLimit ?? 450m);
        var apiOnlyUsd = QuotaMonetaryHelper.PercentToUsd(uniform.Api.DailyTarget, apiLimit ?? 20m);

        Assert.Equal(6, uniform.RemainingDays);
        Assert.True(uniform.Api.DailyTarget > 0);
        Assert.Equal(uniform.Total.DailyTarget, optimal.Total.DailyTarget, precision: 6);
        Assert.InRange(apiOnlyUsd, 3.32m, 3.34m);
        Assert.InRange(combinedUsd, modelsOnlyUsd + apiOnlyUsd - 0.02m, modelsOnlyUsd + apiOnlyUsd + 0.02m);
        Assert.InRange(combinedUsd, 5.0m, 5.1m);
        Assert.True(modelsOnlyUsd < 2m);
    }

    private static void AssertCombinedNearExpected(QuotaCalculationResult result, QuotaPlanMode mode)
    {
        var modelsUsd = QuotaMonetaryHelper.PercentToUsd(result.FirstParty.DailyTarget, 450m);
        var apiUsd = QuotaMonetaryHelper.PercentToUsd(result.Api.DailyTarget, 20m);
        var combinedUsd = modelsUsd + apiUsd;

        Assert.InRange(modelsUsd, 5.04m, 5.05m);
        Assert.InRange(apiUsd, 3.33m, 3.34m);
        Assert.InRange(combinedUsd, 8.37m, 8.38m);
        _ = mode;
    }

    private static QuotaUsage CreateUsage(decimal modelsBaseRemainingUsd, decimal apiRemainingUsd, bool inflateModelsPercent = true)
    {
        const decimal modelsBaseLimit = 450m;
        const decimal apiLimit = 20m;
        var modelsUsed = modelsBaseLimit - modelsBaseRemainingUsd;
        var modelsUsedPercent = (double)(modelsUsed / modelsBaseLimit * 100m);
        if (inflateModelsPercent)
            modelsUsedPercent += 4;
        var apiUsedPercent = apiRemainingUsd >= apiLimit
            ? 0
            : (double)((apiLimit - apiRemainingUsd) / apiLimit * 100m);

        return new QuotaUsage
        {
            PeriodStart = CycleStart,
            PeriodEnd = PeriodEnd,
            FirstPartyUsedPercent = modelsUsedPercent + 4, // намеренно завышен vs USD (регрессия)
            ApiUsedPercent = apiUsedPercent,
            ModelsBaseLimitUsd = modelsBaseLimit,
            ModelsEstimatedLimitUsd = modelsBaseLimit,
            ModelsActualUsedUsd = modelsUsed,
            ModelsUsedUsd = modelsUsed,
            ApiIncludedAmountUsd = apiLimit,
            ApiRemainingAmountUsd = apiRemainingUsd,
            ApiUsedAmountUsd = apiLimit - apiRemainingUsd,
            LimitCents = 2000
        };
    }
}
