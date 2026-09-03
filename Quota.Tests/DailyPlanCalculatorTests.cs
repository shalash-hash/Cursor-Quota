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
    public void ThirtyDayCycle_AcceleratedEnd_IsLastDayOf21CalendarDayPhase()
    {
        var acceleratedEnd = DailyPlanCalculator.GetAcceleratedEndInclusive(CycleStart, Reset30Days);

        Assert.Equal(CycleStart.AddDays(20), acceleratedEnd);
        Assert.Equal(new DateTime(2025, 9, 26), acceleratedEnd);
        Assert.Equal(20, (acceleratedEnd - CycleStart).Days);
    }

    [Fact]
    public void ThirtyOneDayCycle_AcceleratedEnd_IsLastDayOf21CalendarDayPhase()
    {
        var cycleStart = new DateTime(2025, 10, 6);
        var acceleratedEnd = DailyPlanCalculator.GetAcceleratedEndInclusive(cycleStart, Reset31Days);

        Assert.Equal(cycleStart.AddDays(20), acceleratedEnd);
        Assert.Equal(new DateTime(2025, 10, 26), acceleratedEnd);
        Assert.Equal(20, (acceleratedEnd - cycleStart).Days);
    }

    [Fact]
    public void AcceleratedPhase_Exactly21CalendarDays_SeptemberCycle()
    {
        var cycleStart = new DateTime(2026, 9, 6);
        var reset = new DateTime(2026, 10, 6);
        var day21 = new DateTime(2026, 9, 26);
        var day22 = new DateTime(2026, 9, 27);

        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPhase(cycleStart, cycleStart, reset));
        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPhase(day21, cycleStart, reset));
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPhase(day22, cycleStart, reset));
        Assert.True(DailyPlanCalculator.IsWithinReservePhase(day22, cycleStart, reset));
        Assert.Equal(day21, DailyPlanCalculator.GetAcceleratedEndInclusive(cycleStart, reset));
        Assert.Equal(day22, DailyPlanCalculator.GetReservePhaseStart(cycleStart, reset));
    }

    [Fact]
    public void AcceleratedPhase_Exactly21CalendarDays_OctoberCycle()
    {
        var cycleStart = new DateTime(2026, 10, 6);
        var reset = new DateTime(2026, 11, 6);
        var day21 = new DateTime(2026, 10, 26);
        var day22 = new DateTime(2026, 10, 27);

        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPhase(cycleStart, cycleStart, reset));
        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPhase(day21, cycleStart, reset));
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPhase(day22, cycleStart, reset));
        Assert.True(DailyPlanCalculator.IsWithinReservePhase(day22, cycleStart, reset));
    }

    [Fact]
    public void FirstDayOfCycle_UsesHillPlanHigherThanLinearAverage()
    {
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(CycleStart, Reset30Days);
        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(100, CycleStart, CycleStart, Reset30Days);
        var linearAverage = 100.0 / DailyPlanCalculator.CalculateRemainingPlanDays(CycleStart, acceleratedEndInclusive, 100);

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, CycleStart, CycleStart, Reset30Days));
        Assert.True(cursorPlan > linearAverage);
        Assert.InRange(cursorPlan, 8.5, 10.5);
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
    public void LastAcceleratedDay_StillUsesModelsHillPlan()
    {
        var lastHillDay = DailyPlanCalculator.GetAcceleratedEndInclusive(CycleStart, Reset30Days);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(25, lastHillDay, CycleStart, Reset30Days);

        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPhase(lastHillDay, CycleStart, Reset30Days));
        Assert.True(plan > 0);
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, lastHillDay, CycleStart, Reset30Days));
    }

    [Fact]
    public void FirstReserveDay_StartsAfterAcceleratedEnd()
    {
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(CycleStart, Reset30Days);
        var reserveStart = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);

        Assert.Equal(acceleratedEndInclusive.AddDays(1), reserveStart);
        Assert.Equal(new DateTime(2025, 9, 27), reserveStart);
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPhase(reserveStart, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.IsWithinReservePhase(reserveStart, CycleStart, Reset30Days));
    }

    [Fact]
    public void ReservePhase_WithRemainingModels_SpreadsModelsUntilReset()
    {
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
        var remaining = 20.0;
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, today, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);

        Assert.True(plan > 0);
        Assert.Equal(remaining / remainingDays, plan, precision: 6);
    }

    [Fact]
    public void ReservePhase_WithApi_SpreadsApiUntilReset()
    {
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
        var remaining = 10.0;
        var plan = DailyPlanCalculator.CalculateApiDailyPlan(remaining, today, CycleStart, Reset30Days);
        var remainingDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);

        Assert.Equal(remaining / remainingDays, plan, precision: 6);
    }

    [Fact]
    public void ReservePhase_WithModelsAndApi_CombinesBothPlans()
    {
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
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
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
        var apiRemaining = 15.0;

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days) > 0);
    }

    [Fact]
    public void ReservePhase_ApiDepleted_ModelsStillSpread()
    {
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
        var modelsRemaining = 9.0;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(modelsRemaining, today, CycleStart, Reset30Days) > 0);
    }

    [Fact]
    public void ReservePhase_BothDepleted_ReturnsZeroPlans()
    {
        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateCombinedDailyPlan(0, 0, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void ThirtyDayCycle_ReserveTailLengthDependsOnRealCycleLength()
    {
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(CycleStart, Reset30Days);
        var reserveStart = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);

        Assert.Equal(30, (Reset30Days - CycleStart).Days);
        Assert.Equal(20, (acceleratedEndInclusive - CycleStart).Days);
        Assert.Equal(9, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, Reset30Days, 10));
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
        var reserveStart = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);

        Assert.Equal(9, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, Reset30Days, 40));
        Assert.Equal(8, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart.AddDays(1), Reset30Days, 40));
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
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(cycleStart, reset);
        var reserveStart = DailyPlanCalculator.GetReservePhaseStart(cycleStart, reset);

        Assert.Equal(cycleStart.AddDays(20), acceleratedEndInclusive);
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(8, reserveStart, cycleStart, reset) > 0);
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(6, reserveStart, cycleStart, reset) > 0);
    }

    [Fact]
    public void LeapYearFebruaryCycle_HandlesFebruary29Correctly()
    {
        var cycleStart = new DateTime(2024, 2, 6);
        var reset = new DateTime(2024, 3, 6);
        var reserveStart = DailyPlanCalculator.GetReservePhaseStart(cycleStart, reset);

        Assert.Equal(29, (reset - cycleStart).Days);
        Assert.True(DailyPlanCalculator.CalculateApiDailyPlan(10, reserveStart, cycleStart, reset) > 0);
    }

    [Fact]
    public void YearBoundaryCycle_UsesRealCalendarDates()
    {
        var cycleStart = new DateTime(2025, 12, 15);
        var reset = new DateTime(2026, 1, 15);
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(cycleStart, reset);
        var today = acceleratedEndInclusive.AddDays(-2);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(18, today, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(20, (acceleratedEndInclusive - cycleStart).Days);
        Assert.True(plan > 0);
    }

    [Fact]
    public void ShortCycle_AcceleratedEndDoesNotExceedRealReset()
    {
        var cycleStart = new DateTime(2025, 9, 1);
        var reset = new DateTime(2025, 9, 12);
        var acceleratedEndInclusive = DailyPlanCalculator.GetAcceleratedEndInclusive(cycleStart, reset);

        Assert.Equal(reset, acceleratedEndInclusive);
        Assert.False(DailyPlanCalculator.IsWithinReservePhase(cycleStart.AddDays(5), cycleStart, reset));
        Assert.True(DailyPlanCalculator.CalculateCursorModelDailyPlan(40, cycleStart.AddDays(5), cycleStart, reset) > 0);
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, cycleStart.AddDays(5), cycleStart, reset));
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
    public void RemainingPlanDays_UsesBillingTimestamp_NotCalendarMidnight()
    {
        var reset = new DateTime(2026, 9, 6, 12, 36, 42);
        var justAfterMidnight = new DateTime(2026, 9, 6, 0, 30, 0);

        var days = DailyPlanCalculator.CalculateRemainingPlanDays(justAfterMidnight, reset, 40);

        Assert.Equal(1, days);
    }
}

public class QuotaCalculatorTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_EarlyPeriod_TotalDailyTarget_MatchesModelsHillPlan()
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

        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
        var result = _calculator.Calculate(usage, today);
        var expectedCombined = DailyPlanCalculator.CalculateCombinedDailyPlan(40, 60, today, CycleStart, Reset30Days);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.True(result.Api.DailyTarget > 0);
        Assert.Equal(expectedCombined, result.Total.DailyTarget, precision: 4);
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

        var today = DailyPlanCalculator.GetReservePhaseStart(CycleStart, Reset30Days);
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
