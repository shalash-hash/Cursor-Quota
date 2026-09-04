using System.Globalization;
using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public sealed class DailyTargetProgressCalculatorTests
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ru-RU");
    private const int Digits = 2;
    private const decimal LimitUsd = 470m;

    [Fact]
    public void CalculatePlanDelta_ExampleFromScreenshot_IsAbout33PercentAhead()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(4.20, 3.15);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
        Assert.Equal(1.05, delta.AbsoluteDeltaPercent, precision: 6);
        Assert.Equal(33.333333, delta.RelativeDeltaPercent, precision: 4);
    }

    [Fact]
    public void CalculatePlanDelta_FourAndFivePercent_Is25PercentAhead()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(5, 4);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
        Assert.Equal(25, delta.RelativeDeltaPercent, precision: 6);
    }

    [Fact]
    public void CalculatePlanDelta_ThreeAndFourPercent_Is25PercentBehind()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(3, 4);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.Equal(25, delta.RelativeDeltaPercent, precision: 6);
        Assert.Equal(-1, delta.AbsoluteDeltaPercent, precision: 6);
    }

    [Fact]
    public void CalculatePlanDelta_EqualValues_IsOnPlan()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(4, 4);

        Assert.Equal(DailyPlanDeltaKind.OnPlan, delta.Kind);
        Assert.Equal(0, delta.RelativeDeltaPercent);
        Assert.Equal(0, delta.AbsoluteDeltaPercent);
    }

    [Fact]
    public void CalculatePlanDelta_ZeroPlan_DoesNotDivideByZero()
    {
        var noUsage = DailyTargetProgressCalculator.CalculatePlanDelta(0, 0);
        var withUsage = DailyTargetProgressCalculator.CalculatePlanDelta(2.5, 0);

        Assert.Equal(DailyPlanDeltaKind.NoPlan, noUsage.Kind);
        Assert.Equal(DailyPlanDeltaKind.Ahead, withUsage.Kind);
        Assert.True(double.IsFinite(withUsage.RelativeDeltaPercent));
    }

    [Fact]
    public void CalculateDeltaUsd_UsesFullPrecisionValues()
    {
        var deltaUsd = DailyTargetProgressCalculator.CalculateDeltaUsd(4.20, 3.15, LimitUsd);

        Assert.NotNull(deltaUsd);
        Assert.Equal(4.93m, deltaUsd.Value, precision: 2);
    }

    [Fact]
    public void FormatRelativeDeltaWithUsd_DoesNotRecomputeFromRoundedUiStrings()
    {
        const double today = 4.20;
        const double plan = 3.15;
        var delta = DailyTargetProgressCalculator.CalculatePlanDelta(today, plan);
        var deltaUsd = DailyTargetProgressCalculator.CalculateDeltaUsd(today, plan, LimitUsd);
        var text = DailyTargetProgressCalculator.FormatRelativeDeltaWithUsd(
            delta.RelativeDeltaPercent,
            deltaUsd,
            Digits,
            Culture);

        Assert.Contains("33,33", text);
        Assert.Contains("$4,93", text);
        Assert.DoesNotContain("1,05", text);
    }

    [Fact]
    public void CalculateDeltaUsd_BehindPlan_ReturnsNegativeDifference()
    {
        var deltaUsd = DailyTargetProgressCalculator.CalculateDeltaUsd(3, 4, 20m);

        Assert.Equal(-0.20m, deltaUsd);
    }

    // ── USD-canonical ahead/behind (regression: do not compare pool percents) ──

    [Fact]
    public void CalculatePlanDeltaFromUsd_LiveScenario_IsBehindNotAhead()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(2.04m, 8.14m);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.False(delta.Kind == DailyPlanDeltaKind.Ahead);
        Assert.InRange(delta.RelativeDeltaPercent, 74.9, 75.0);
        Assert.Equal(-6.10m, DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(2.04m, 8.14m), precision: 2);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_LiveScenario_PlanNotCompleted()
    {
        var progress = DailyTargetProgressCalculator.CalculateFromUsd(2.04m, 8.14m);
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(2.04m, 8.14m);

        Assert.False(progress.IsExceeded);
        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.InRange(progress.PlanCompletionPercent, 25.0, 25.1);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_PlanExceeded_IsAheadAndCompleted()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(10m, 8m);
        var progress = DailyTargetProgressCalculator.CalculateFromUsd(10m, 8m);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
        Assert.Equal(25, delta.RelativeDeltaPercent, precision: 6);
        Assert.Equal(2m, DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(10m, 8m));
        Assert.True(progress.IsExceeded);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_ExactPlan_IsOnPlan()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(8m, 8m);
        var progress = DailyTargetProgressCalculator.CalculateFromUsd(8m, 8m);

        Assert.Equal(DailyPlanDeltaKind.OnPlan, delta.Kind);
        Assert.Equal(0m, DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(8m, 8m));
        Assert.True(progress.PlanCompletionPercent >= 99.99);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_MixedPools_BehindByUsd()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(6m, 8m);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.Equal(25, delta.RelativeDeltaPercent, precision: 6);
        Assert.Equal(-2m, DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(6m, 8m));
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_ModelsExhausted_ApiOnlyPlan()
    {
        var today = 2.04m;
        var plan = 8.14m;
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(today, plan);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.InRange(delta.RelativeDeltaPercent, 74.9, 75.0);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_ApiExhausted_ModelsOnlyPlan()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(3m, 5m);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.Equal(40, delta.RelativeDeltaPercent, precision: 6);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_ZeroPlanZeroToday_IsNoPlan()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(0m, 0m);

        Assert.Equal(DailyPlanDeltaKind.NoPlan, delta.Kind);
    }

    [Fact]
    public void CalculatePlanDeltaFromUsd_ZeroPlanWithUsage_IsAhead()
    {
        var delta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(1.50m, 0m);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_LiveScenario_IsFalse()
    {
        Assert.False(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(2.04m, 8.14m));
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_PlanExceeded_IsTrue()
    {
        Assert.True(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(10m, 8m));
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_ExactPlan_IsTrue()
    {
        Assert.True(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(8m, 8m));
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_MixedPools_Behind_IsFalse()
    {
        Assert.False(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(6m, 8m));
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_ZeroPlanZeroToday_IsFalse()
    {
        Assert.False(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(0m, 0m));
    }

    [Fact]
    public void IsDailyPlanCompletedFromUsd_ZeroPlanWithUsage_IsTrue()
    {
        Assert.True(DailyTargetProgressCalculator.IsDailyPlanCompletedFromUsd(1.50m, 0m));
    }

    [Fact]
    public void PercentBasedDelta_WouldWronglyShowAhead_ForLiveScenario()
    {
        const double todayPercent = 3.88;
        const double planPercent = 1.70;
        var wrongDelta = DailyTargetProgressCalculator.CalculatePlanDelta(todayPercent, planPercent);

        Assert.Equal(DailyPlanDeltaKind.Ahead, wrongDelta.Kind);
        Assert.True(wrongDelta.RelativeDeltaPercent > 100);

        var correctDelta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(2.04m, 8.14m);
        Assert.Equal(DailyPlanDeltaKind.Behind, correctDelta.Kind);
    }
}
