using System.Globalization;
using Quota.Helpers;
using Quota.Models;
using Xunit;

namespace Quota.Tests;

public class QuotaMonetaryHelperTests
{
    [Fact]
    public void EstimateLimitCents_MatchesObservedCursorRatio()
    {
        var limit = QuotaMonetaryHelper.EstimateLimitCents(18108, 40.24);
        Assert.Equal(45000, limit);
        Assert.Equal(450m, QuotaMonetaryHelper.EstimateLimitUsd(18108, 40.24));
    }

    [Fact]
    public void CentsToUsd_RoundsToTwoDecimals()
    {
        Assert.Equal(181.08m, QuotaMonetaryHelper.CentsToUsd(18108));
    }

    [Fact]
    public void ResolveDaySpendUsd_PrefersSpendCentsWhenPresent()
    {
        var usd = QuotaMonetaryHelper.ResolveDaySpendUsd(847, 2.6, 450m);
        Assert.Equal(8.47m, usd);
    }

    [Fact]
    public void ResolveDaySpendUsd_EstimatesFromPercentWhenCentsMissing()
    {
        var usd = QuotaMonetaryHelper.ResolveDaySpendUsd(null, 0.56, 450m);
        Assert.Equal(2.52m, usd);
    }

    [Fact]
    public void ResolveCombinedDisplay_IncludesApiPoolInLimitAndProgress()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 44.61,
            FirstPartyUsedPercent = 44.61,
            ApiUsedPercent = 0,
            ModelsUsedUsd = 200.76m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            ApiUsedAmountUsd = 0m
        };

        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);

        Assert.Equal(200.76m, combined.UsedUsd);
        Assert.Equal(470m, combined.LimitUsd);
        Assert.Equal(42.71, combined.UsedPercent, precision: 1);
        Assert.Equal(269.24m, combined.RemainingUsd);
    }

    [Fact]
    public void ResolveCombinedDayPercent_UsesCombinedLimit()
    {
        var percent = QuotaMonetaryHelper.ResolveCombinedDayPercent(
            5.96,
            0,
            450m,
            20m);

        Assert.Equal(5.71, percent, precision: 2);
    }

    [Fact]
    public void ResolveCombinedLinearDailyTarget_DividesRemainingByDays()
    {
        var target = QuotaMonetaryHelper.ResolveCombinedLinearDailyTarget(22.55, 6);

        Assert.Equal(3.7583, target, precision: 3);
    }
}
