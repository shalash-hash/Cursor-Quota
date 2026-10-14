using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class CyclePlanPaceTests
{
    [Fact]
    public void CyclePlanPaceDelta_UsesPercentPointsOnTotalScale_NotUsdRatio()
    {
        const double actualPercent = 29.10;
        const double plannedPercent = 37.14;
        const decimal actualUsd = 136.77m;
        const decimal plannedUsd = 174.56m;

        var delta = DailyTargetProgressCalculator.CalculateCyclePlanPaceDelta(
            actualPercent,
            plannedPercent,
            actualUsd,
            plannedUsd);

        Assert.Equal(DailyPlanDeltaKind.Behind, delta.Kind);
        Assert.InRange(delta.RelativeDeltaPercent, 7.9, 8.1);

        var wrongRelativeToPlanned = (double)((plannedUsd - actualUsd) / plannedUsd * 100m);
        Assert.True(delta.RelativeDeltaPercent < 10);
        Assert.True(wrongRelativeToPlanned > 20);
    }

    [Fact]
    public void CyclePlanPaceDelta_ActualAbovePlanned_IsAhead()
    {
        var delta = DailyTargetProgressCalculator.CalculateCyclePlanPaceDelta(
            40,
            35,
            188m,
            164.5m);

        Assert.Equal(DailyPlanDeltaKind.Ahead, delta.Kind);
        Assert.Equal(5, delta.RelativeDeltaPercent, precision: 1);
    }

    [Fact]
    public void CyclePlanPaceDelta_Equal_IsOnPlan()
    {
        var delta = DailyTargetProgressCalculator.CalculateCyclePlanPaceDelta(
            50,
            50,
            235m,
            235m);

        Assert.Equal(DailyPlanDeltaKind.OnPlan, delta.Kind);
    }
}
