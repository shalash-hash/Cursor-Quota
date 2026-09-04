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
    public void Calculate_RealScenario_AheadBehindUsesUsdNotMixedPercents()
    {
        var cycleStart = new DateTime(2025, 9, 6);
        var reset = new DateTime(2025, 10, 6);
        var today = new DateTime(2025, 10, 3);
        var modelsLimit = 450m;
        var apiLimit = 20m;
        var modelsRemainingUsd = 0.30m;
        var modelsUsedUsd = modelsLimit - modelsRemainingUsd;
        var modelsTodayUsd = 1.41m;
        var apiTodayUsd = 0.63m;
        var todayUsageUsd = modelsTodayUsd + apiTodayUsd;

        var usage = new QuotaUsage
        {
            TotalUsedPercent = 95,
            FirstPartyUsedPercent = 99.9,
            ApiUsedPercent = 3,
            TodayTotalUsedPercent = 3.88,
            TodayFirstPartyUsedPercent = 0.31,
            TodayApiUsedPercent = 3.15,
            TodayTotalSpendCents = 204,
            TodayModelsSpendCents = 141,
            TodayApiSpendCents = 63,
            ModelsUsedUsd = modelsUsedUsd,
            ModelsEstimatedLimitUsd = modelsLimit,
            ApiIncludedAmountUsd = apiLimit,
            ApiUsedAmountUsd = 0.60m,
            PeriodStart = cycleStart,
            PeriodEnd = reset
        };

        var result = _calculator.Calculate(usage, today);
        var dailyPlanUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            modelsLimit,
            apiLimit);
        var resolvedToday = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        Assert.NotNull(resolvedToday);
        Assert.Equal(todayUsageUsd, resolvedToday.Value, precision: 2);

        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(
            resolvedToday.Value,
            dailyPlanUsd);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.True(dailyPlanUsd > resolvedToday.Value);

        var wrongPercentDelta = DailyTargetProgressCalculator.CalculatePlanDelta(
            usage.TodayTotalUsedPercent,
            result.Total.DailyTarget);
        Assert.Equal(DailyPlanDeltaKind.Ahead, wrongPercentDelta.Kind);

        Assert.False(result.Total.IsTodayPlanCompleted);
        Assert.Equal(PaceStatus.BelowPlan, result.Total.PaceStatus);
        Assert.Equal(
            DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(
                resolvedToday.Value,
                dailyPlanUsd),
            result.Total.IsTodayPlanCompleted);
    }

    [Fact]
    public void Calculate_IsTodayPlanCompleted_WhenUsdExceedsPlan_IsTrue()
    {
        var usage = CreateReservePhaseUsage(
            todayModelsUsd: 10m,
            todayApiPercent: 0,
            todayTotalPercent: 99);

        var result = _calculator.Calculate(usage, ReserveToday);
        var dailyPlanUsd = ResolveTotalDailyPlanUsd(result, usage);

        Assert.True(dailyPlanUsd > 0);
        Assert.True(QuotaMonetaryHelper.ResolveTodayUsageUsd(usage) > dailyPlanUsd);
        Assert.True(result.Total.IsTodayPlanCompleted);
    }

    [Fact]
    public void Calculate_IsTodayPlanCompleted_WhenUsdMatchesPlan_IsTrue()
    {
        var usage = CreateReservePhaseUsageForExactPlan();
        var result = _calculator.Calculate(usage, ReserveToday);
        var dailyPlanUsd = ResolveTotalDailyPlanUsd(result, usage);
        var todayUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        Assert.NotNull(todayUsd);
        Assert.True(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(
            todayUsd.Value,
            dailyPlanUsd));
        Assert.True(result.Total.IsTodayPlanCompleted);
    }

    [Fact]
    public void Calculate_IsTodayPlanCompleted_WhenUsdBelowPlan_IsFalse()
    {
        var usage = CreateReservePhaseUsage(
            todayModelsUsd: 4m,
            todayApiPercent: 10,
            todayTotalPercent: 99);

        var result = _calculator.Calculate(usage, ReserveToday);
        var dailyPlanUsd = ResolveTotalDailyPlanUsd(result, usage);
        var todayUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        Assert.NotNull(todayUsd);
        Assert.True(todayUsd < dailyPlanUsd);
        Assert.False(result.Total.IsTodayPlanCompleted);
    }

    [Fact]
    public void Calculate_IsTodayPlanCompleted_PercentWouldSayCompleted_UsdSaysNot()
    {
        var usage = CreateLiveScenarioUsage();

        var result = _calculator.Calculate(usage, ReserveToday);

        Assert.True(usage.TodayTotalUsedPercent > result.Total.DailyTarget);
        Assert.False(result.Total.IsTodayPlanCompleted);
    }

    [Fact]
    public void Calculate_IsTodayPlanCompleted_ZeroPlanZeroToday_IsFalse()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 100,
            FirstPartyUsedPercent = 100,
            ApiUsedPercent = 0,
            TodayTotalUsedPercent = 0,
            PeriodStart = CycleStart,
            PeriodEnd = ResetDate
        };

        var result = _calculator.Calculate(usage, ReserveToday);

        Assert.False(result.Total.IsTodayPlanCompleted);
    }

    private static readonly DateTime CycleStart = new(2025, 9, 6);
    private static readonly DateTime ResetDate = new(2025, 10, 6);
    private static readonly DateTime ReserveToday = new(2025, 10, 3);

    private static QuotaUsage CreateLiveScenarioUsage() => new()
    {
        TotalUsedPercent = 95,
        FirstPartyUsedPercent = 99.9,
        ApiUsedPercent = 3,
        TodayTotalUsedPercent = 3.88,
        TodayFirstPartyUsedPercent = 0.31,
        TodayApiUsedPercent = 3.15,
        TodayTotalSpendCents = 204,
        TodayModelsSpendCents = 141,
        TodayApiSpendCents = 63,
        ModelsUsedUsd = 449.70m,
        ModelsEstimatedLimitUsd = 450m,
        ApiIncludedAmountUsd = 20m,
        ApiUsedAmountUsd = 0.60m,
        PeriodStart = CycleStart,
        PeriodEnd = ResetDate
    };

    private static QuotaUsage CreateReservePhaseUsage(
        decimal todayModelsUsd,
        double todayApiPercent,
        double todayTotalPercent)
    {
        var apiUsd = QuotaMonetaryHelper.PercentToUsd(todayApiPercent, 20m);
        var totalUsd = todayModelsUsd + apiUsd;

        return new QuotaUsage
        {
            TotalUsedPercent = 95,
            FirstPartyUsedPercent = 99.9,
            ApiUsedPercent = 3,
            TodayTotalUsedPercent = todayTotalPercent,
            TodayFirstPartyUsedPercent = 0.31,
            TodayApiUsedPercent = todayApiPercent,
            TodayTotalSpendCents = (long)Math.Round(totalUsd * 100m),
            TodayModelsSpendCents = todayModelsUsd > 0m ? (long)Math.Round(todayModelsUsd * 100m) : null,
            TodayApiSpendCents = apiUsd > 0m ? (long)Math.Round(apiUsd * 100m) : null,
            ModelsUsedUsd = 449.70m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            ApiUsedAmountUsd = 0.60m,
            PeriodStart = CycleStart,
            PeriodEnd = ResetDate
        };
    }

    private static QuotaUsage CreateReservePhaseUsageForExactPlan()
    {
        var baseUsage = CreateReservePhaseUsage(todayModelsUsd: 0m, todayApiPercent: 0, todayTotalPercent: 0);
        var calculator = new QuotaCalculator();
        var preliminary = calculator.Calculate(baseUsage, ReserveToday);
        var planUsd = ResolveTotalDailyPlanUsd(preliminary, baseUsage);
        var apiPercent = (double)(planUsd / 20m * 100m);

        return CreateReservePhaseUsage(
            todayModelsUsd: 0m,
            todayApiPercent: apiPercent,
            todayTotalPercent: apiPercent);
    }

    private static decimal ResolveTotalDailyPlanUsd(QuotaCalculationResult result, QuotaUsage usage) =>
        QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);
}
