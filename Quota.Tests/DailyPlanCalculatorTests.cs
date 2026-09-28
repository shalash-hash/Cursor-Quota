using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class DailyPlanCalculatorTests
{
    private static readonly DateTime CycleStart = new(2025, 9, 6);
    private static readonly DateTime Reset30Days = new(2025, 10, 6);
    private static readonly DateTime Reset31Days = new(2025, 11, 6);

    [Fact]
    public void Standard30DayCycle_TailStartsExactly7DaysBeforeReset()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var reset = new DateTime(2026, 10, 6, 12, 33, 42);
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset);

        Assert.Equal(new DateTime(2026, 9, 29, 12, 33, 42), tailStart);
        Assert.Equal(reset.AddDays(-DailyPlanCalculator.FinalUniformTailDurationDays), tailStart);
    }

    [Fact]
    public void ThirtyDayCycle_FinalUniformTail_IsSevenBillingDays()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);

        Assert.Equal(new DateTime(2025, 9, 29), tailStart);
        Assert.Equal(7, DailyPlanCalculator.CalculateRemainingPlanDays(tailStart, Reset30Days, 10));
    }

    [Fact]
    public void ScenarioA_StartOfCycle_OptimalExceedsUniform()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var optimalModels = DailyPlanCalculator.CalculateCursorModelDailyPlan(100, CycleStart, CycleStart, Reset30Days);
        var uniformModels = DailyPlanCalculator.CalculateLinearDailyPlan(100, CycleStart, Reset30Days);
        var accelDays = DailyPlanCalculator.CalculateRemainingPlanDays(CycleStart, tailStart, 100);
        var expectedOptimal = 100.0 / accelDays;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, CycleStart, CycleStart, Reset30Days));
        Assert.Equal(expectedOptimal, optimalModels, precision: 6);
        Assert.True(optimalModels > uniformModels);
    }

    [Fact]
    public void ScenarioB_OnOptimalTrajectory_DailyPlanTracksRemainingOverAcceleratedDays()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var totalAccelDays = DailyPlanCalculator.CalculateRemainingPlanDays(CycleStart, tailStart, 100);
        var today = CycleStart.AddDays(7);
        var spentFraction = 7.0 / totalAccelDays;
        var remaining = 100 * (1 - spentFraction);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, today, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, tailStart, remaining);

        Assert.Equal(remaining / remainingDays, plan, precision: 6);
    }

    [Fact]
    public void ScenarioC_AfterUnderspend_OptimalDailyPlanIncreasesSharply()
    {
        var today = CycleStart.AddDays(7);
        var onPaceRemaining = 60.0;
        var underspendRemaining = 80.0;
        var onPacePlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(onPaceRemaining, today, CycleStart, Reset30Days);
        var underspendPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(underspendRemaining, today, CycleStart, Reset30Days);

        Assert.True(underspendPlan > onPacePlan);
        Assert.True(underspendPlan - onPacePlan > 1.0);
    }

    [Fact]
    public void ScenarioD_AfterOverspend_OptimalDailyPlanDecreases()
    {
        var today = CycleStart.AddDays(7);
        var underspendPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(80, today, CycleStart, Reset30Days);
        var overspendPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(20, today, CycleStart, Reset30Days);

        Assert.True(overspendPlan < underspendPlan);
        Assert.True(overspendPlan >= 0);
    }

    [Fact]
    public void ScenarioE_MidCycle24PercentSpent_OptimalExceedsUniform()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var today = new DateTime(2026, 9, 21, 13, 51, 14);
        const double modelsRemaining = 100 - 25.6088888888889;
        const double apiRemaining = 100.0;

        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, periodEnd);
        var accelDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, tailStart, modelsRemaining);
        var resetDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, periodEnd, modelsRemaining);

        var optimalModels = DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, cycleStart, periodEnd);
        var optimalApi = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, cycleStart, periodEnd);
        var uniformModels = DailyPlanCalculator.CalculateLinearDailyPlan(modelsRemaining, today, periodEnd);
        var uniformApi = DailyPlanCalculator.CalculateLinearDailyPlan(apiRemaining, today, periodEnd);

        Assert.True(DailyPlanCalculator.IsWithinOptimalAcceleratedPhase(today, cycleStart, periodEnd));
        Assert.Equal(modelsRemaining / accelDays, optimalModels, precision: 4);
        Assert.Equal(0, optimalApi);
        Assert.True(optimalModels > uniformModels);
        Assert.Equal(8, accelDays);
        Assert.True(resetDays >= 14);
    }

    [Fact]
    public void EarlyPeriod_ApiQuotaDoesNotAffectCombinedPlan()
    {
        var planWithoutApi = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 0, CycleStart, CycleStart, Reset30Days);
        var planWithApi = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 50, CycleStart, CycleStart, Reset30Days);

        Assert.Equal(planWithoutApi, planWithApi);
    }

    [Fact]
    public void MidCycle_UnderSpending_IncreasesDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var behindPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(80, today, CycleStart, Reset30Days);
        var onTrackPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(60, today, CycleStart, Reset30Days);

        Assert.True(behindPlan > onTrackPlan);
    }

    [Fact]
    public void MidCycle_OverSpending_DecreasesDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var behindPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(80, today, CycleStart, Reset30Days);
        var aheadPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(20, today, CycleStart, Reset30Days);

        Assert.True(aheadPlan < behindPlan);
        Assert.True(aheadPlan >= 0);
    }

    [Fact]
    public void ScenarioF_LastOptimalAcceleratedInstant_UsesAdaptiveModelsPlan()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var lastAcceleratedInstant = tailStart.AddTicks(-1);
        var remaining = 25.0;
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, lastAcceleratedInstant, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(lastAcceleratedInstant, tailStart, remaining);

        Assert.True(DailyPlanCalculator.IsWithinOptimalAcceleratedPhase(lastAcceleratedInstant, CycleStart, Reset30Days));
        Assert.Equal(remaining / remainingDays, plan, precision: 6);
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, lastAcceleratedInstant, CycleStart, Reset30Days));
    }

    [Fact]
    public void ScenarioG_FirstFinalTailInstant_OptimalEqualsUniform()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);

        Assert.Equal(new DateTime(2025, 9, 29), tailStart);
        Assert.False(DailyPlanCalculator.IsWithinOptimalAcceleratedPhase(tailStart, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.IsWithinFinalUniformTail(tailStart, CycleStart, Reset30Days));
        Assert.Equal(
            DailyPlanCalculator.CalculateLinearDailyPlan(40, tailStart, Reset30Days),
            DailyPlanCalculator.CalculateCursorModelDailyPlan(40, tailStart, CycleStart, Reset30Days),
            precision: 8);
    }

    [Fact]
    public void ReservePhase_WithRemainingModels_SpreadsModelsUntilReset()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var remaining = 20.0;
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, today, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);

        Assert.True(plan > 0);
        Assert.Equal(remaining / remainingDays, plan, precision: 6);
    }

    [Fact]
    public void ReservePhase_WithApi_SpreadsApiUntilReset()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var remaining = 10.0;
        var plan = DailyPlanCalculator.CalculateApiDailyPlan(remaining, today, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);

        Assert.Equal(remaining / remainingDays, plan, precision: 6);
    }

    [Fact]
    public void ReservePhase_WithModelsAndApi_CombinesBothPlans()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var cursorRemaining = 12.0;
        var apiRemaining = 8.0;

        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(cursorRemaining, today, CycleStart, Reset30Days);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days);
        var combinedPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(cursorRemaining, apiRemaining, today, CycleStart, Reset30Days);

        Assert.True(cursorPlan > 0);
        Assert.True(apiPlan > 0);
        Assert.Equal(cursorPlan + apiPlan, combinedPlan, precision: 6);
    }

    [Fact]
    public void ReservePhase_ModelsDepleted_OnlyApiPlanRemains()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var apiRemaining = 15.0;

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days) > 0);
    }

    [Fact]
    public void ReservePhase_ApiDepleted_ModelsStillSpread()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var modelsRemaining = 9.0;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, CycleStart, Reset30Days) > 0);
    }

    [Fact]
    public void ReservePhase_BothDepleted_ReturnsZeroPlans()
    {
        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateCombinedDailyPlan(0, 0, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void ThirtyDayCycle_FinalTailLength_IsAlwaysSevenDaysWhenCycleLongerThanSeven()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);

        Assert.Equal(30, (Reset30Days - CycleStart).Days);
        Assert.Equal(23, (tailStart - CycleStart).Days);
        Assert.Equal(7, DailyPlanCalculator.CalculateRemainingPlanDays(tailStart, Reset30Days, 10));
    }

    [Fact]
    public void ReserveSpreadDays_LastCalendarDayBeforeReset_IsOneDay()
    {
        var lastSpendingDay = new DateTime(2025, 10, 5);

        Assert.Equal(1, DailyPlanCalculator.CalculateRemainingPlanDays(lastSpendingDay, Reset30Days, 40));
    }

    [Fact]
    public void ReserveSpreadDays_FromFirstReserveDayToReset_UsesBillingCalendarSemantics()
    {
        var reserveStart = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);

        Assert.Equal(7, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, Reset30Days, 40));
        Assert.Equal(6, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart.AddDays(1), Reset30Days, 40));
    }

    [Fact]
    public void ReserveSpreadDays_OnResetCalendarDayBeforeRolloverTime_StillCountsOneDay()
    {
        var reset = new DateTime(2026, 10, 6, 12, 36, 42);
        var morningOfResetDay = new DateTime(2026, 10, 6, 0, 30, 0);

        Assert.Equal(1, DailyPlanCalculator.CalculateRemainingPlanDays(morningOfResetDay, reset, 40));
    }

    [Fact]
    public void FebruaryCycle_HandlesShortMonthCorrectly()
    {
        var cycleStart = new DateTime(2025, 1, 30);
        var reset = new DateTime(2025, 2, 28);
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset);

        Assert.Equal(new DateTime(2025, 2, 21), tailStart);
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(8, tailStart, cycleStart, reset) > 0);
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(6, tailStart, cycleStart, reset) > 0);
    }

    [Fact]
    public void LeapYearFebruaryCycle_HandlesFebruary29Correctly()
    {
        var cycleStart = new DateTime(2024, 2, 6);
        var reset = new DateTime(2024, 3, 6);
        var reserveStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset);

        Assert.Equal(29, (reset - cycleStart).Days);
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(10, reserveStart, cycleStart, reset) > 0);
    }

    [Fact]
    public void YearBoundaryCycle_UsesRealCalendarDates()
    {
        var cycleStart = new DateTime(2025, 12, 15);
        var reset = new DateTime(2026, 1, 15);
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset);
        var today = tailStart.AddDays(-2);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(18, today, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(24, (tailStart - cycleStart).Days);
        Assert.True(plan > 0);
    }

    [Fact]
    public void ScenarioH_ShortCycle_ElevenDays_TailStartsAtDayFive()
    {
        var cycleStart = new DateTime(2025, 9, 1);
        var reset = new DateTime(2025, 9, 12);
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset);
        var midCycle = cycleStart.AddDays(5);

        Assert.Equal(new DateTime(2025, 9, 5), tailStart);
        Assert.True(DailyPlanCalculator.IsWithinFinalUniformTail(midCycle, cycleStart, reset));
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(40, midCycle, cycleStart, reset) > 0);
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(10, midCycle, cycleStart, reset) > 0);
    }

    [Fact]
    public void LinearDailyPlan_SpreadsRemainingUntilReset()
    {
        var today = CycleStart.AddDays(18);
        var remaining = 61.28;
        var daysToReset = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);

        var linearPlan = DailyPlanCalculator.CalculateLinearDailyPlan(remaining, today, Reset30Days);

        Assert.Equal(12, daysToReset);
        Assert.Equal(remaining / daysToReset, linearPlan, precision: 3);
    }

    [Fact]
    public void RemainingQuotaNeverProducesNegativeDailyPlan()
    {
        var today = CycleStart.AddDays(3);

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(-5, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(-3, today, CycleStart, Reset30Days));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 1)]
    public void RemainingPlanDays_PreventsDivisionByZero(double remainingPercent, int expectedDays)
    {
        var today = Reset30Days;

        var days = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remainingPercent);

        Assert.Equal(expectedDays, days);
        Assert.True(double.IsFinite(DailyPlanCalculator.CalculateCursorModelDailyPlan(remainingPercent, today, CycleStart, Reset30Days)));
        Assert.True(double.IsFinite(DailyPlanCalculator.CalculateApiDailyPlan(remainingPercent, today, CycleStart, Reset30Days)));
    }

    [Fact]
    public void ScenarioI_RemainingPlanDays_UsesBillingTimestamp_NotCalendarMidnight()
    {
        var reset = new DateTime(2026, 9, 6, 12, 36, 42);
        var justAfterMidnight = new DateTime(2026, 9, 6, 0, 30, 0);

        var days = DailyPlanCalculator.CalculateRemainingPlanDays(justAfterMidnight, reset, 40);

        Assert.Equal(1, days);
    }

    [Fact]
    public void ScenarioJ_Phase1_ApiDailyPlanIsZero_ModelsCarryCombinedTarget()
    {
        var today = CycleStart.AddDays(4);
        var modelsRemaining = 55.0;
        var apiRemaining = 88.0;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, CycleStart, Reset30Days) > 0);
        Assert.Equal(
            DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, CycleStart, Reset30Days),
            DailyPlanCalculator.CalculateCombinedDailyPlan(modelsRemaining, apiRemaining, today, CycleStart, Reset30Days),
            precision: 6);
    }

    private static readonly DateTime CanonCycleStart = new(2026, 9, 6, 12, 33, 42);
    private static readonly DateTime CanonReset = new(2026, 10, 6, 12, 33, 42);

    [Fact]
    public void AtCycleStart_Phase1PlanDays_UntilSevenDayTailBeforeReset()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);
        var days = DailyPlanCalculator.CalculateRemainingPlanDays(CanonCycleStart, tailStart, 100);

        Assert.Equal(23, days);
    }

    [Fact]
    public void AtCycleStart_100PercentRemaining_Yields100DivPhase1Days()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);
        var days = DailyPlanCalculator.CalculateRemainingPlanDays(CanonCycleStart, tailStart, 100);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(100, CanonCycleStart, CanonCycleStart, CanonReset);

        Assert.Equal(100.0 / days, plan, precision: 10);
    }

    [Fact]
    public void BillingTimestampBoundary_NotMidnight()
    {
        var justBeforeTail = new DateTime(2026, 9, 29, 12, 33, 41);
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);

        Assert.Equal(CanonReset.AddDays(-7), tailStart);
        Assert.True(DailyPlanCalculator.IsWithinOptimalAcceleratedPhase(justBeforeTail, CanonCycleStart, CanonReset));
        Assert.True(DailyPlanCalculator.IsWithinFinalUniformTail(tailStart, CanonCycleStart, CanonReset));
    }

    [Fact]
    public void BeforeSevenDayTail_OptimalDiffersFromUniform()
    {
        var today = CanonReset.AddDays(-8);
        const double modelsRemaining = 50;
        const double apiRemaining = 100;

        var optimal = DailyPlanCalculator.CalculateCombinedDailyPlan(modelsRemaining, apiRemaining, today, CanonCycleStart, CanonReset);
        var uniform = DailyPlanCalculator.CalculateLinearDailyPlan(modelsRemaining, today, CanonReset)
                      + DailyPlanCalculator.CalculateLinearDailyPlan(apiRemaining, today, CanonReset);

        Assert.True(DailyPlanCalculator.IsWithinOptimalAcceleratedPhase(today, CanonCycleStart, CanonReset));
        Assert.NotEqual(optimal, uniform);
    }

    [Fact]
    public void ExactlySevenDaysBeforeReset_OptimalEqualsUniform()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);
        const double modelsRemaining = 40;
        const double apiRemaining = 60;

        AssertCombinedOptimalMatchesUniform(CanonCycleStart, CanonReset, tailStart, modelsRemaining, apiRemaining);
    }

    [Fact]
    public void SixDaysBeforeReset_OptimalEqualsUniform()
    {
        var today = CanonReset.AddDays(-6);
        AssertCombinedOptimalMatchesUniform(CanonCycleStart, CanonReset, today, 30, 80);
    }

    [Fact]
    public void OneDayBeforeReset_OptimalEqualsUniform()
    {
        var today = CanonReset.AddDays(-1);
        AssertCombinedOptimalMatchesUniform(CanonCycleStart, CanonReset, today, 5, 10);
    }

    [Fact]
    public void FinalPartialBillingDay_OptimalEqualsUniform()
    {
        var morningOfResetDay = new DateTime(2026, 10, 6, 0, 30, 0);
        AssertCombinedOptimalMatchesUniform(CanonCycleStart, CanonReset, morningOfResetDay, 2, 3);
    }

    [Fact]
    public void ResetBoundary_ReturnsCorrectZeroOrExpiredBehavior()
    {
        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, CanonReset, CanonCycleStart, CanonReset));
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, CanonReset, CanonCycleStart, CanonReset));
        Assert.Equal(50, DailyPlanCalculator.CalculateCursorModelDailyPlan(50, CanonReset, CanonCycleStart, CanonReset));
    }

    [Fact]
    public void Phase1_Underrun_IncreasesOptimalRecommendation()
    {
        var today = CanonCycleStart.AddDays(10);
        var onPace = DailyPlanCalculator.CalculateCursorModelDailyPlan(50, today, CanonCycleStart, CanonReset);
        var underspend = DailyPlanCalculator.CalculateCursorModelDailyPlan(70, today, CanonCycleStart, CanonReset);

        Assert.True(underspend > onPace);
    }

    [Fact]
    public void Phase1_Overrun_DecreasesOptimalRecommendation()
    {
        var today = CanonCycleStart.AddDays(10);
        var underspend = DailyPlanCalculator.CalculateCursorModelDailyPlan(70, today, CanonCycleStart, CanonReset);
        var overspend = DailyPlanCalculator.CalculateCursorModelDailyPlan(20, today, CanonCycleStart, CanonReset);

        Assert.True(overspend < underspend);
    }

    [Fact]
    public void ApiIsReservedBeforeFinalTail()
    {
        var today = CanonCycleStart.AddDays(5);
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(100, today, CanonCycleStart, CanonReset));
    }

    [Fact]
    public void ApiParticipatesDuringFinalTail()
    {
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);
        var optimalApi = DailyPlanCalculator.CalculateApiDailyPlan(50, tailStart, CanonCycleStart, CanonReset);
        var uniformApi = DailyPlanCalculator.CalculateLinearDailyPlan(50, tailStart, CanonReset);

        Assert.Equal(uniformApi, optimalApi, precision: 8);
    }

    [Fact]
    public void CycleExactly7Days_OptimalEqualsUniformThroughout()
    {
        var cycleStart = new DateTime(2026, 3, 1, 12, 0, 0);
        var reset = cycleStart.AddDays(7);
        var today = cycleStart.AddDays(3);

        Assert.Equal(cycleStart, DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset));
        AssertCombinedOptimalMatchesUniform(cycleStart, reset, today, 80, 90);
    }

    [Fact]
    public void CycleShorterThan7Days_OptimalEqualsUniformThroughout()
    {
        var cycleStart = new DateTime(2026, 4, 1, 9, 15, 0);
        var reset = cycleStart.AddDays(4);
        var today = cycleStart.AddDays(2);

        Assert.Equal(cycleStart, DailyPlanCalculator.GetFinalUniformTailStart(cycleStart, reset));
        AssertCombinedOptimalMatchesUniform(cycleStart, reset, today, 55, 66);
    }

    [Fact]
    public void ModeSwitch_RecalculatesDisplayedRecommendation()
    {
        var calculator = new QuotaCalculator();
        var usage = new QuotaUsage
        {
            PeriodStart = CanonCycleStart,
            PeriodEnd = CanonReset,
            FirstPartyUsedPercent = 0,
            ApiUsedPercent = 0,
            TotalUsedPercent = 0,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m
        };
        var beforeTail = CanonCycleStart.AddDays(5);
        var optimalBefore = calculator.Calculate(usage, QuotaPlanMode.Optimal, beforeTail);
        var uniformBefore = calculator.Calculate(usage, QuotaPlanMode.Uniform, beforeTail);
        Assert.NotEqual(optimalBefore.Total.DailyTarget, uniformBefore.Total.DailyTarget);

        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);
        var optimalTail = calculator.Calculate(usage, QuotaPlanMode.Optimal, tailStart);
        var uniformTail = calculator.Calculate(usage, QuotaPlanMode.Uniform, tailStart);
        Assert.Equal(optimalTail.Total.DailyTarget, uniformTail.Total.DailyTarget, precision: 6);
    }

    [Fact]
    public void RealSnapshot_2026_09_21_CorrectRemainingAcceleratedDays()
    {
        var today = new DateTime(2026, 9, 21, 13, 51, 14);
        const double modelsRemaining = 74.3911111111111;
        var tailStart = DailyPlanCalculator.GetFinalUniformTailStart(CanonCycleStart, CanonReset);

        Assert.Equal(8, DailyPlanCalculator.CalculateRemainingPlanDays(today, tailStart, modelsRemaining));
    }

    private static void AssertCombinedOptimalMatchesUniform(
        DateTime cycleStart,
        DateTime reset,
        DateTime today,
        double modelsRemaining,
        double apiRemaining)
    {
        var optimalModels = DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, cycleStart, reset);
        var optimalApi = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, cycleStart, reset);
        var uniformModels = DailyPlanCalculator.CalculateLinearDailyPlan(modelsRemaining, today, reset);
        var uniformApi = DailyPlanCalculator.CalculateLinearDailyPlan(apiRemaining, today, reset);

        Assert.Equal(uniformModels, optimalModels, precision: 8);
        Assert.Equal(uniformApi, optimalApi, precision: 8);
        Assert.Equal(uniformModels + uniformApi, optimalModels + optimalApi, precision: 8);
    }
}

public class QuotaCalculatorTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_EarlyPeriod_TotalDailyTarget_MatchesModelsAdaptivePlan()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 0,
            FirstPartyUsedPercent = 0,
            ApiUsedPercent = 0,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var result = _calculator.Calculate(usage, CycleStart);
        var expectedCombined = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 100, CycleStart, CycleStart, Reset30Days);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
    }

    [Fact]
    public void Calculate_EarlyPeriod_ApiDoesNotIncreaseTotalDailyTarget()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 5,
            FirstPartyUsedPercent = 0,
            ApiUsedPercent = 5,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var result = _calculator.Calculate(usage, CycleStart);
        var expectedCombined = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 95, CycleStart, CycleStart, Reset30Days);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
        Assert.Equal(95, result.Api.RemainingPercent);
    }

    [Fact]
    public void Calculate_ReservePeriod_TotalDailyTarget_CombinesModelsAndApi()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 50,
            FirstPartyUsedPercent = 60,
            ApiUsedPercent = 40,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var result = _calculator.Calculate(usage, today);
        var expectedCombined = DailyPlanCalculator.CalculateCombinedDailyPlan(40, 60, today, CycleStart, Reset30Days);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.True(result.Api.DailyTarget > 0);
        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
    }

    [Fact]
    public void ScenarioE_RealSnapshot_OptimalCombinedUsdExceedsUniform()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var today = new DateTime(2026, 9, 21, 13, 51, 14);
        var usage = new QuotaUsage
        {
            PeriodStart = cycleStart,
            PeriodEnd = periodEnd,
            FirstPartyUsedPercent = 25.6088888888889,
            ApiUsedPercent = 0,
            TotalUsedPercent = 24.5191489361702,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m
        };

        var optimal = _calculator.Calculate(usage, QuotaPlanMode.Optimal, today);
        var uniform = _calculator.Calculate(usage, QuotaPlanMode.Uniform, today);
        var optimalUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            optimal.FirstParty.DailyTarget,
            optimal.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);
        var uniformUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            uniform.FirstParty.DailyTarget,
            uniform.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

        Assert.Equal(0, optimal.Api.DailyTarget);
        Assert.True(optimalUsd > uniformUsd);
        Assert.InRange(uniformUsd, 23m, 24m);
        Assert.InRange(optimalUsd, 40m, 44m);
    }

    [Fact]
    public void Calculate_PreservesRemainingDaysUntilRealReset()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 20,
            FirstPartyUsedPercent = 20,
            ApiUsedPercent = 0,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var result = _calculator.Calculate(usage, new DateTime(2025, 9, 13));

        Assert.Equal(23, result.RemainingDays);
        Assert.True(result.FirstParty.DailyTarget > 0);
    }

    [Fact]
    public void Calculate_TotalDailyTarget_UsesCombinedDailyPlan()
    {
        var periodStart = new DateTime(2025, 8, 26, 12, 36, 0);
        var periodEnd = new DateTime(2025, 9, 7, 12, 36, 0);
        var today = new DateTime(2025, 9, 1, 10, 0, 0);
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 77.45,
            FirstPartyUsedPercent = 75,
            ApiUsedPercent = 10,
            ModelsUsedUsd = 337.5m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            ApiUsedAmountUsd = 2m,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd
        };

        var result = _calculator.Calculate(usage, today);
        var modelsPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(25, today, periodStart, periodEnd);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(90, today, periodStart, periodEnd);
        var expectedCombined = QuotaMonetaryHelper.ResolveCombinedDayPercent(
            modelsPlan,
            apiPlan,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
    }

    [Fact]
    public void Calculate_TotalDailyTarget_UsesCombinedDailyPlanInAcceleratedPhase()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 40,
            FirstPartyUsedPercent = 40,
            ApiUsedPercent = 0,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var today = CycleStart.AddDays(7);
        var result = _calculator.Calculate(usage, today);
        var modelsPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(60, 100, today, CycleStart, Reset30Days);
        var expectedCombined = QuotaMonetaryHelper.ResolveCombinedDayPercent(
            result.FirstParty.DailyTarget,
            0,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
        Assert.True(result.Total.DailyTarget < modelsPlan);
    }

    [Fact]
    public void Calculate_ReservePeriod_ModelsAndApiTargetsAreIndependent()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 50,
            FirstPartyUsedPercent = 60,
            ApiUsedPercent = 40,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var today = DailyPlanCalculator.GetFinalUniformTailStart(CycleStart, Reset30Days);
        var result = _calculator.Calculate(usage, today);

        Assert.Equal(
            DailyPlanCalculator.CalculateCursorModelDailyPlan(40, today, CycleStart, Reset30Days),
            result.FirstParty.DailyTarget,
            precision: 4);
        Assert.Equal(
            DailyPlanCalculator.CalculateApiDailyPlan(60, today, CycleStart, Reset30Days),
            result.Api.DailyTarget,
            precision: 4);
    }

    [Fact]
    public void Calculate_DoesNotChangeUsedOrRemainingPercent()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 42.5,
            FirstPartyUsedPercent = 30,
            ApiUsedPercent = 12.5,
            TodayTotalUsedPercent = 1.1,
            TodayFirstPartyUsedPercent = 0.8,
            TodayApiUsedPercent = 0.3,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var result = _calculator.Calculate(usage, new DateTime(2025, 9, 20));

        Assert.Equal(42.5, result.Total.UsedPercent);
        Assert.Equal(57.5, result.Total.RemainingPercent);
        Assert.Equal(30, result.FirstParty.UsedPercent);
        Assert.Equal(12.5, result.Api.UsedPercent);
    }

    private static readonly DateTime CycleStart = new(2025, 9, 6);
    private static readonly DateTime Reset30Days = new(2025, 10, 6);
}
