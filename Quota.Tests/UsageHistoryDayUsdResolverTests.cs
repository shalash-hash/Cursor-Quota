using Quota.Helpers;
using Quota.Models;
using Xunit;

namespace Quota.Tests;

public class UsageHistoryDayUsdResolverTests
{
    [Fact]
    public void Resolve_PercentOnlyLegacySnapshot_EstimatesModelsUsdFromPoolPercent()
    {
        var dailySpent = new PoolDaySpent(13.15, 13.15, 0);
        var anchor = new QuotaSnapshot
        {
            FirstPartyPercent = 40,
            ApiPercent = 0,
            TotalPercent = 40
        };

        var (modelsUsd, apiUsd, totalUsd) = UsageHistoryDayUsdResolver.Resolve(
            dailySpent,
            default,
            anchor);

        Assert.Equal(59.18m, modelsUsd);
        Assert.Null(apiUsd);
        Assert.Equal(59.18m, totalUsd);
    }

    [Fact]
    public void Resolve_PrefersSpendCents_WhenAvailable()
    {
        var dailySpent = new PoolDaySpent(15, 12, 3);
        var daySpendUsd = new DaySpendUsd(68.50m, 60.61m, 7.89m);
        var anchor = new QuotaSnapshot
        {
            TotalSpendCents = 6850,
            FirstPartyPercent = 90,
            LimitCents = 2000
        };

        var (modelsUsd, apiUsd, totalUsd) = UsageHistoryDayUsdResolver.Resolve(
            dailySpent,
            daySpendUsd,
            anchor);

        Assert.Equal(60.61m, modelsUsd);
        Assert.Equal(7.89m, apiUsd);
        Assert.Equal(68.50m, totalUsd);
    }

    [Fact]
    public void Resolve_CombinedPercentFallback_UsesSeparatePoolLimits()
    {
        var dailySpent = new PoolDaySpent(18, 15, 3);
        var anchor = new QuotaSnapshot
        {
            FirstPartyPercent = 50,
            ApiPercent = 10,
            LimitCents = 2000
        };

        var (modelsUsd, apiUsd, totalUsd) = UsageHistoryDayUsdResolver.Resolve(
            dailySpent,
            default,
            anchor);

        Assert.Equal(67.5m, modelsUsd);
        Assert.Equal(0.6m, apiUsd);
        Assert.Equal(68.1m, totalUsd);
    }

    [Fact]
    public void ResolveHistoryModelsLimitUsd_UsesFrozenBaseLimitCents()
    {
        var snapshot = new QuotaSnapshot { ModelsBaseLimitCents = 45000 };

        Assert.Equal(450m, UsageHistoryDayUsdResolver.ResolveHistoryModelsLimitUsd(snapshot));
    }
}
