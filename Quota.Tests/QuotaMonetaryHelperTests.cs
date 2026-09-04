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
    public void ResolveModelsRemainingUsd_UsesLimitMinusUsed()
    {
        var usage = new QuotaUsage
        {
            ModelsUsedUsd = 443.79m,
            ModelsEstimatedLimitUsd = 450m
        };

        Assert.Equal(6.21m, QuotaMonetaryHelper.ResolveModelsRemainingUsd(usage));
    }

    [Fact]
    public void ResolveModelsRemainingUsd_DoesNotGoNegative()
    {
        var usage = new QuotaUsage
        {
            ModelsUsedUsd = 460m,
            ModelsEstimatedLimitUsd = 450m
        };

        Assert.Equal(0m, QuotaMonetaryHelper.ResolveModelsRemainingUsd(usage));
    }

    [Fact]
    public void ResolveModelsRemainingUsd_ReturnsNullWhenDataMissing()
    {
        Assert.Null(QuotaMonetaryHelper.ResolveModelsRemainingUsd(new QuotaUsage()));
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
    public void ResolveTodayUsageUsd_SumsModelsAndApi()
    {
        var usage = new QuotaUsage
        {
            TodayModelsSpendCents = 141,
            TodayApiUsedPercent = 3.15,
            ApiIncludedAmountUsd = 20m,
        };

        var total = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        Assert.NotNull(total);
        Assert.Equal(2.04m, total.Value, precision: 2);
    }

    [Fact]
    public void ResolveDailyPlanUsd_SumsPoolPlans()
    {
        var plan = QuotaMonetaryHelper.ResolveDailyPlanUsd(0, 40.7, 450m, 20m);

        Assert.Equal(8.14m, plan, precision: 2);
    }

    [Fact]
    public void ResolveCombinedLinearDailyTarget_DividesRemainingByDays()
    {
        var target = QuotaMonetaryHelper.ResolveCombinedLinearDailyTarget(22.55, 6);

        Assert.Equal(3.7583, target, precision: 3);
    }

    [Fact]
    public void ResolveTodayUsageUsd_LiveScenario_SumsModelsAndApi()
    {
        var usage = CreateLiveTodayUsage();

        var total = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        Assert.Equal(3.55m, total);
        Assert.Equal(2.46m, QuotaMonetaryHelper.ResolveModelsTodayUsd(usage));
        Assert.Equal(1.09m, QuotaMonetaryHelper.ResolveApiTodayUsd(usage));
    }

    [Fact]
    public void ResolveCombinedTodayPercent_LiveScenario_UsesCombinedLimit()
    {
        var usage = CreateLiveTodayUsage(modelsLimitUsd: 459.45m);

        var percent = QuotaMonetaryHelper.ResolveCombinedTodayPercent(usage);

        Assert.NotNull(percent);
        Assert.InRange(percent.Value, 0.73, 0.75);
    }

    [Fact]
    public void ResolveCombinedTodayPercent_IsNotSumOfPoolPercents()
    {
        var usage = CreateLiveTodayUsage();
        var combined = QuotaMonetaryHelper.ResolveCombinedTodayPercent(usage);
        var naiveSum = usage.TodayFirstPartyUsedPercent + usage.TodayApiUsedPercent;

        Assert.NotNull(combined);
        Assert.True(naiveSum > 5);
        Assert.InRange(combined.Value, 0.7, 0.8);
        Assert.NotEqual(naiveSum, combined.Value, precision: 1);
    }

    [Fact]
    public void ResolveCombinedTodayPercentFromParts_ModelsExhausted_KeepsModelsUsdInTotal()
    {
        const long modelsCents = 200;
        const double apiPercent = 5.45;
        const decimal modelsLimit = 450m;
        const decimal apiLimit = 20m;

        var percent = QuotaMonetaryHelper.ResolveCombinedTodayPercentFromParts(
            modelsCents,
            0,
            apiPercent,
            modelsLimit,
            apiLimit);

        var todayUsd = QuotaMonetaryHelper.CentsToUsd(modelsCents)
            + QuotaMonetaryHelper.PercentToUsd(apiPercent, apiLimit);

        Assert.Equal(3.09m, todayUsd);
        Assert.InRange(percent, 0.64, 0.66);
    }

    [Fact]
    public void ResolveCombinedTodayPercent_ModelsOnly_EqualsModelsShare()
    {
        var usage = new QuotaUsage
        {
            TodayModelsSpendCents = 355,
            TodayFirstPartyUsedPercent = 0.79,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
        };

        var percent = QuotaMonetaryHelper.ResolveCombinedTodayPercent(usage);

        Assert.NotNull(percent);
        Assert.InRange(percent.Value, 0.75, 0.76);
        Assert.Equal(3.55m, QuotaMonetaryHelper.ResolveTodayUsageUsd(usage));
    }

    [Fact]
    public void ResolveCombinedTodayPercent_ApiOnly_EqualsApiShareOfCombinedLimit()
    {
        var usage = new QuotaUsage
        {
            TodayApiUsedPercent = 5.45,
            ApiIncludedAmountUsd = 20m,
            ModelsEstimatedLimitUsd = 450m,
        };

        var percent = QuotaMonetaryHelper.ResolveCombinedTodayPercent(usage);

        Assert.NotNull(percent);
        Assert.InRange(percent.Value, 0.23, 0.24);
        Assert.Equal(1.09m, QuotaMonetaryHelper.ResolveTodayUsageUsd(usage));
    }

    [Fact]
    public void ResolveCombinedTodayPercent_ZeroUsage_ReturnsZeroWithoutNaN()
    {
        var usage = new QuotaUsage
        {
            TodayModelsSpendCents = 0,
            TodayApiUsedPercent = 0,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
        };

        var percent = QuotaMonetaryHelper.ResolveCombinedTodayPercent(usage);

        Assert.Equal(0, percent);
        Assert.False(double.IsNaN(percent!.Value));
        Assert.False(double.IsInfinity(percent.Value));
    }

    [Fact]
    public void ResolveCombinedTodayPercent_LiveScenario_AheadBehindInputsUnchanged()
    {
        var usage = CreateLiveTodayUsage();
        var todayUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);
        const decimal dailyPlanUsd = 7.91m;

        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(
            todayUsd!.Value,
            dailyPlanUsd);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.InRange(delta.RelativeDeltaPercent, 55.0, 55.2);
        Assert.Equal(-4.36m, DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(
            todayUsd.Value,
            dailyPlanUsd),
            precision: 2);
    }

    private static QuotaUsage CreateLiveTodayUsage(decimal modelsLimitUsd = 450m) => new()
    {
        TodayModelsSpendCents = 246,
        TodayFirstPartyUsedPercent = 0.55,
        TodayApiUsedPercent = 5.45,
        TodayTotalUsedPercent = 6.78,
        ModelsEstimatedLimitUsd = modelsLimitUsd,
        ApiIncludedAmountUsd = 20m,
    };
}
