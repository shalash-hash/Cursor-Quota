using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Quota.Services.CursorApi;
using Xunit;

namespace Quota.Tests;

public class QuotaSpendResolverTests
{
    [Fact]
    public void LiveDiagnosticScenario_ModelCAccounting()
    {
        var usage = Enriched(
            totalSpendUsd: 466.36m,
            apiUsedUsd: 7.26m,
            modelsBaseUsd: 450m);

        Assert.Equal(466.36m, QuotaSpendResolver.ResolveTotalPeriodSpendUsd(usage));
        Assert.Equal(7.26m, QuotaSpendResolver.ResolveApiUsedUsd(usage));
        Assert.Equal(459.10m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage));
        Assert.Equal(9.10m, usage.ModelsBonusUsedUsd);
        Assert.Equal(466.36m, QuotaSpendResolver.ResolveCombinedActualUsedUsd(usage));
        Assert.NotEqual(473.62m, QuotaSpendResolver.ResolveCombinedActualUsedUsd(usage));
    }

    [Fact]
    public void ApiOnlyDelta_DoesNotIncreaseModelsBonus()
    {
        var before = Enriched(totalSpendUsd: 450.10m, apiUsedUsd: 0.06m, modelsBaseUsd: 450m);
        var after = Enriched(totalSpendUsd: 450.20m, apiUsedUsd: 0.16m, modelsBaseUsd: 450m);

        Assert.Equal(0m, (after.ModelsBonusUsedUsd ?? 0m) - (before.ModelsBonusUsedUsd ?? 0m));
        Assert.Equal(0m, QuotaSpendResolver.ResolveModelsActualUsedUsd(after)!.Value
            - QuotaSpendResolver.ResolveModelsActualUsedUsd(before)!.Value, precision: 2);
    }

    [Fact]
    public void ModelsOnlyDelta_IncreasesModelsActual()
    {
        var before = Enriched(totalSpendUsd: 450.10m, apiUsedUsd: 0.10m, modelsBaseUsd: 450m);
        var after = Enriched(totalSpendUsd: 450.20m, apiUsedUsd: 0.10m, modelsBaseUsd: 450m);

        Assert.Equal(0.10m, QuotaSpendResolver.ResolveModelsActualUsedUsd(after)!.Value
            - QuotaSpendResolver.ResolveModelsActualUsedUsd(before)!.Value, precision: 2);
        Assert.Equal(0.10m, (after.ModelsBonusUsedUsd ?? 0m) - (before.ModelsBonusUsedUsd ?? 0m), precision: 2);
    }

    [Fact]
    public void MixedDelta_SplitsCorrectly()
    {
        var before = Enriched(totalSpendUsd: 450.00m, apiUsedUsd: 0m, modelsBaseUsd: 450m);
        var after = Enriched(totalSpendUsd: 450.30m, apiUsedUsd: 0.10m, modelsBaseUsd: 450m);

        Assert.Equal(0.30m, QuotaSpendResolver.ResolveCombinedActualUsedUsd(after)!.Value
            - QuotaSpendResolver.ResolveCombinedActualUsedUsd(before)!.Value, precision: 2);
        Assert.Equal(0.20m, QuotaSpendResolver.ResolveModelsActualUsedUsd(after)!.Value
            - QuotaSpendResolver.ResolveModelsActualUsedUsd(before)!.Value, precision: 2);
        Assert.Equal(0.10m, QuotaSpendResolver.ResolveApiUsedUsd(after)!.Value
            - QuotaSpendResolver.ResolveApiUsedUsd(before)!.Value, precision: 2);
    }

    [Fact]
    public void CombinedToday_UsesTotalDelta_NotSum()
    {
        var usage = new QuotaUsage
        {
            TodayTotalSpendCents = 500,
            TodayModelsSpendCents = 300,
            TodayApiSpendCents = 200,
            ApiIncludedAmountUsd = 20m,
            ModelsEstimatedLimitUsd = 450m
        };

        Assert.Equal(5m, QuotaSpendResolver.ResolveCombinedTodayUsd(usage));
        Assert.Equal(3m, QuotaSpendResolver.ResolveModelsTodayUsd(usage));
        Assert.Equal(2m, QuotaSpendResolver.ResolveApiTodayUsd(usage));
        Assert.NotEqual(7m, QuotaSpendResolver.ResolveCombinedTodayUsd(usage));
    }

    [Fact]
    public void Yesterday_DecomposesLikeToday()
    {
        var usage = new QuotaUsage
        {
            HasYesterdayUsageData = true,
            YesterdayTotalSpendCents = 400,
            YesterdayModelsSpendCents = 250,
            YesterdayApiSpendCents = 150,
            ApiIncludedAmountUsd = 20m
        };

        Assert.Equal(4m, QuotaSpendResolver.ResolveCombinedYesterdayUsd(usage));
        Assert.Equal(2.5m, QuotaSpendResolver.ResolveModelsYesterdayUsd(usage));
        Assert.Equal(1.5m, QuotaSpendResolver.ResolveApiYesterdayUsd(usage));
    }

    [Fact]
    public void ModelsBonus_UsesModelsActualMinusBase()
    {
        var usage = Enriched(totalSpendUsd: 460m, apiUsedUsd: 5m, modelsBaseUsd: 450m);

        Assert.Equal(455m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage));
        Assert.Equal(5m, usage.ModelsBonusUsedUsd);
    }

    [Fact]
    public void BeforeApiStarts_ModelsEqualsTotal()
    {
        var usage = Enriched(totalSpendUsd: 200m, apiUsedUsd: 0m, modelsBaseUsd: 450m);

        Assert.Equal(200m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage));
        Assert.Equal(200m, QuotaSpendResolver.ResolveCombinedActualUsedUsd(usage));
    }

    [Fact]
    public void DirectAutoSpendApiSpend_TakePriority()
    {
        var usage = new QuotaUsage
        {
            TotalSpendCents = 50000,
            AutoSpendCents = 45500,
            ApiSpendCents = 4500,
            ApiUsedAmountUsd = 99m
        };

        Assert.Equal(455m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage));
        Assert.Equal(45m, QuotaSpendResolver.ResolveApiUsedUsd(usage));
    }

    [Fact]
    public void DirectFieldsAbsent_ReconstructsFromTotalAndApiPercent()
    {
        var usage = new QuotaUsage
        {
            TotalSpendCents = 46636,
            ApiUsedPercent = 36.288888888888884,
            ApiIncludedAmountUsd = 20m,
            LimitCents = 2000
        };

        Assert.Equal(459.10m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage)!.Value, precision: 2);
        Assert.Equal(7.26m, QuotaSpendResolver.ResolveApiUsedUsd(usage)!.Value, precision: 2);
    }

    [Fact]
    public void CombinedBaseFraction_ExcludesBonus()
    {
        var usage = Enriched(
            totalSpendUsd: 466.36m,
            apiUsedUsd: 7.26m,
            modelsBaseUsd: 450m);

        Assert.Equal(457.26m, QuotaSpendResolver.ResolveCombinedBaseUsedUsd(usage));
        Assert.Equal(470m, QuotaMonetaryHelper.ResolveCombinedBaseLimitUsd(usage));
    }

    [Fact]
    public void ModelsActual_NeverNegative()
    {
        var usage = new QuotaUsage
        {
            TotalSpendCents = 500,
            ApiSpendCents = 1000
        };

        Assert.Equal(0m, QuotaSpendResolver.ResolveModelsActualUsedUsd(usage));
    }

    [Fact]
    public void SnapshotReplay_ReconstructsModelsActual()
    {
        var snapshot = new QuotaSnapshot
        {
            TotalSpendCents = 46636,
            ApiPercent = 36.288888888888884,
            LimitCents = 2000,
            FirstPartyPercent = 100
        };

        Assert.Equal(459.10m, QuotaSpendResolver.ResolveModelsActualUsedUsdFromSnapshot(snapshot)!.Value, precision: 2);
    }

    private static QuotaUsage Enriched(decimal totalSpendUsd, decimal apiUsedUsd, decimal modelsBaseUsd)
    {
        var modelsActual = Math.Max(0m, totalSpendUsd - apiUsedUsd);
        return QuotaUsageEnricher.ApplyBonusBreakdown(
            new QuotaUsage
            {
                TotalSpendCents = (long)Math.Round(totalSpendUsd * 100m),
                ApiUsedAmountUsd = apiUsedUsd,
                ApiIncludedAmountUsd = 20m,
                ApiUsedPercent = (double)(apiUsedUsd / 20m * 100m),
                FirstPartyUsedPercent = 100,
                ModelsActualUsedUsd = modelsActual,
                ModelsUsedUsd = modelsActual,
                LimitCents = 2000
            },
            (long)Math.Round(modelsBaseUsd * 100m),
            BonusSource.Models);
    }
}
