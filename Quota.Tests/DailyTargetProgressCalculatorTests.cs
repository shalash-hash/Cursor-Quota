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
}
