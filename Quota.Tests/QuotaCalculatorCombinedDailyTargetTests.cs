using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class QuotaCalculatorCombinedDailyTargetTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_ReserveTail_RealScenario_CombinesUsdNotPercents()
    {
        var cycleStart = new DateTime(2025, 9, 6);
        var reset = new DateTime(2025, 10, 6);
        var today = new DateTime(2025, 10, 3);
        var modelsLimit = 450m;
        var apiLimit = 20m;
        var modelsRemainingUsd = 0.30m;
        var modelsUsedUsd = modelsLimit - modelsRemainingUsd;
        var modelsUsedPercent = (double)(modelsUsedUsd / modelsLimit * 100m);

        var usage = new QuotaUsage
        {
            TotalUsedPercent = (double)((modelsUsedUsd + apiLimit) / (modelsLimit + apiLimit) * 100m),
            FirstPartyUsedPercent = modelsUsedPercent,
            ApiUsedPercent = 0,
            ModelsUsedUsd = modelsUsedUsd,
            ModelsEstimatedLimitUsd = modelsLimit,
            ApiIncludedAmountUsd = apiLimit,
            ApiUsedAmountUsd = 0m,
            PeriodStart = cycleStart,
            PeriodEnd = reset
        };

        var result = _calculator.Calculate(usage, today);

        var combinedUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            modelsLimit,
            apiLimit);
        var wrongPercentSum = result.FirstParty.DailyTarget + result.Api.DailyTarget;

        Assert.InRange(combinedUsd, 6.70m, 6.85m);
        Assert.InRange(result.Total.DailyTarget, 1.40, 1.50);
        Assert.True(wrongPercentSum > 30, "Regression guard: naive percent sum must not match fixed total.");
        Assert.NotEqual(wrongPercentSum, result.Total.DailyTarget, precision: 1);
        Assert.Equal(
            (double)(combinedUsd / (modelsLimit + apiLimit) * 100m),
            result.Total.DailyTarget,
            precision: 2);
    }

    [Fact]
    public void Calculate_CombinedDailyTargetUsd_DoesNotExceedCombinedRemaining()
    {
        var cycleStart = new DateTime(2025, 9, 6);
        var reset = new DateTime(2025, 10, 6);
        var today = new DateTime(2025, 10, 3);
        var modelsLimit = 450m;
        var apiLimit = 20m;
        var modelsRemainingUsd = 0.30m;
        var modelsUsedUsd = modelsLimit - modelsRemainingUsd;

        var usage = new QuotaUsage
        {
            TotalUsedPercent = (double)(modelsUsedUsd / (modelsLimit + apiLimit) * 100m),
            FirstPartyUsedPercent = (double)(modelsUsedUsd / modelsLimit * 100m),
            ApiUsedPercent = 0,
            ModelsUsedUsd = modelsUsedUsd,
            ModelsEstimatedLimitUsd = modelsLimit,
            ApiIncludedAmountUsd = apiLimit,
            ApiUsedAmountUsd = 0m,
            PeriodStart = cycleStart,
            PeriodEnd = reset
        };

        var result = _calculator.Calculate(usage, today);
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);
        var dailyUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            modelsLimit,
            apiLimit);

        Assert.NotNull(combined.RemainingUsd);
        Assert.True(dailyUsd <= combined.RemainingUsd.Value + 0.01m);
    }

    [Fact]
    public void ResolveCombinedDayPercent_DifferentBases_DoesNotSumPercents()
    {
        var modelsLimit = 450m;
        var apiLimit = 20m;
        var combinedLimit = modelsLimit + apiLimit;

        var modelsDaily = 10.0;
        var apiDaily = 50.0;
        var combinedUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            modelsDaily,
            apiDaily,
            modelsLimit,
            apiLimit);
        var combinedPercent = QuotaMonetaryHelper.ResolveCombinedDayPercent(
            modelsDaily,
            apiDaily,
            modelsLimit,
            apiLimit);

        Assert.Equal(55m, combinedUsd);
        Assert.Equal(11.70, combinedPercent, precision: 2);
        Assert.NotEqual(60.0, combinedPercent);
    }

    [Fact]
    public void Calculate_OnePoolZero_UsesOtherPoolUsdConvertedToCombinedPercent()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 50,
            FirstPartyUsedPercent = 100,
            ApiUsedPercent = 50,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            PeriodStart = new DateTime(2025, 9, 6),
            PeriodEnd = new DateTime(2025, 10, 6)
        };

        var today = DailyPlanCalculator.GetReservePhaseStart(usage.PeriodStart, usage.PeriodEnd);
        var result = _calculator.Calculate(usage, today);

        Assert.Equal(0, result.FirstParty.DailyTarget);
        Assert.True(result.Api.DailyTarget > 0);

        var combinedUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);
        var apiUsd = QuotaMonetaryHelper.PercentToUsd(
            result.Api.DailyTarget,
            usage.ApiIncludedAmountUsd!.Value);

        Assert.Equal(apiUsd, combinedUsd);
        Assert.Equal(
            QuotaMonetaryHelper.ResolveCombinedDayPercent(
                0,
                result.Api.DailyTarget,
                usage.ModelsEstimatedLimitUsd,
                usage.ApiIncludedAmountUsd),
            result.Total.DailyTarget,
            precision: 4);
    }

    [Fact]
    public void Calculate_BothPoolsZero_ReturnsZeroWithoutNaN()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 100,
            FirstPartyUsedPercent = 100,
            ApiUsedPercent = 100,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            PeriodStart = new DateTime(2025, 9, 6),
            PeriodEnd = new DateTime(2025, 10, 6)
        };

        var today = DailyPlanCalculator.GetReservePhaseStart(usage.PeriodStart, usage.PeriodEnd);
        var result = _calculator.Calculate(usage, today);

        Assert.Equal(0, result.FirstParty.DailyTarget);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(0, result.Total.DailyTarget);
        Assert.False(double.IsNaN(result.Total.DailyTarget));
        Assert.False(double.IsInfinity(result.Total.DailyTarget));
    }

    [Fact]
    public void Calculate_RealScenario_AheadBehindUsesFixedCombinedPlan()
    {
        var cycleStart = new DateTime(2025, 9, 6);
        var reset = new DateTime(2025, 10, 6);
        var today = new DateTime(2025, 10, 3);
        var modelsLimit = 450m;
        var apiLimit = 20m;
        var combinedLimit = modelsLimit + apiLimit;
        var modelsRemainingUsd = 0.30m;
        var modelsUsedUsd = modelsLimit - modelsRemainingUsd;
        var todaySpentUsd = 20.64m;
        var todayTotalPercent = (double)(todaySpentUsd / combinedLimit * 100m);

        var usage = new QuotaUsage
        {
            TotalUsedPercent = (double)(modelsUsedUsd / combinedLimit * 100m),
            FirstPartyUsedPercent = (double)(modelsUsedUsd / modelsLimit * 100m),
            ApiUsedPercent = 0,
            TodayTotalUsedPercent = todayTotalPercent,
            ModelsUsedUsd = modelsUsedUsd,
            ModelsEstimatedLimitUsd = modelsLimit,
            ApiIncludedAmountUsd = apiLimit,
            ApiUsedAmountUsd = 0m,
            PeriodStart = cycleStart,
            PeriodEnd = reset
        };

        var result = _calculator.Calculate(usage, today);
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(
            usage.TodayTotalUsedPercent,
            result.Total.DailyTarget);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
        Assert.True(delta.RelativeDeltaPercent > 100);
        Assert.True(result.Total.DailyTarget < todayTotalPercent);
    }
}
