using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class PlannedCumulativeUsageTests
{
    private static readonly DateTime CycleStart = new(2026, 9, 6, 12, 33, 42);
    private static readonly DateTime PeriodEnd = new(2026, 10, 6, 12, 33, 42);

    [Fact]
    public void AtCycleStart_PlannedUsed_IsZero()
    {
        var planned = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            CycleStart,
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.Equal(0, planned.UsedPercent);
        Assert.Equal(0m, planned.UsedUsd);
    }

    [Fact]
    public void AtOrAfterPeriodEnd_PlannedUsed_IsFullLimit()
    {
        var planned = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            PeriodEnd,
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.Equal(100, planned.UsedPercent);
        Assert.Equal(470m, planned.UsedUsd);
    }

    [Fact]
    public void Example_300Of470_IsMarkerPercent()
    {
        const decimal limit = 470m;
        var usedUsd = 300m;
        var expectedPercent = (double)(usedUsd / limit * 100m);

        var planned = new PlannedCumulativeUsage(expectedPercent, usedUsd);
        Assert.InRange(planned.UsedPercent, 63.82, 63.84);
    }

    [Fact]
    public void Uniform_MidCycle_PlannedBetweenActualExtremes()
    {
        var midCycle = CycleStart.AddDays(15);
        var planned = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            midCycle,
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.InRange(planned.UsedPercent, 40, 60);
        Assert.True(planned.UsedUsd > 150m);
        Assert.True(planned.UsedUsd < 320m);
    }

    [Fact]
    public void OptimalAndUniform_DifferBeforeFinalTail_MatchInsideTail()
    {
        var beforeTail = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, PeriodEnd).AddDays(-2);
        var optimal = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Optimal,
            beforeTail,
            CycleStart,
            PeriodEnd,
            450m,
            20m);
        var uniform = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            beforeTail,
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.NotEqual(optimal.UsedPercent, uniform.UsedPercent);

        var inTail = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, PeriodEnd).AddDays(1);
        var optimalTail = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Optimal,
            inTail,
            CycleStart,
            PeriodEnd,
            450m,
            20m);
        var uniformTail = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            inTail,
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.InRange(optimalTail.UsedPercent, 70, 100);
        Assert.InRange(uniformTail.UsedPercent, 70, 100);
        Assert.True(optimalTail.UsedPercent > uniformTail.UsedPercent);
    }

    [Fact]
    public void QuotaCalculator_ExposesPlannedCumulativeOnResult()
    {
        var usage = new QuotaUsage
        {
            PeriodStart = CycleStart,
            PeriodEnd = PeriodEnd,
            FirstPartyUsedPercent = 50,
            ApiUsedPercent = 0,
            ModelsBaseLimitUsd = 450m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            LimitCents = 2000
        };

        var calculator = new QuotaCalculator();
        var result = calculator.Calculate(usage, QuotaPlanMode.Uniform, CycleStart.AddDays(10));

        Assert.InRange(result.PlannedCumulativeUsedPercent, 0, 100);
        Assert.NotNull(result.PlannedCumulativeUsedUsd);
    }

    [Fact]
    public void IncludesModelsAndApi_InCombinedPlannedUsd()
    {
        var planned = DailyPlanCalculator.CalculatePlannedCumulativeUsage(
            QuotaPlanMode.Uniform,
            CycleStart.AddDays(1),
            CycleStart,
            PeriodEnd,
            450m,
            20m);

        Assert.True(planned.UsedUsd > 0m);
        Assert.True(planned.UsedPercent > 0);
    }
}
