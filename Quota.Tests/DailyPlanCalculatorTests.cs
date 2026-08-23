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
    public void AcceleratedPlan_UsesOnlyCursorModelQuota_NotApi()
    {
        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(100, CycleStart, CycleStart, Reset30Days);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(10, CycleStart, CycleStart, Reset30Days);
        var combinedPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 10, CycleStart, CycleStart, Reset30Days);

        Assert.InRange(cursorPlan, 4.7618, 4.7620);
        Assert.Equal(0, apiPlan);
        Assert.Equal(cursorPlan, combinedPlan);
    }

    [Fact]
    public void AcceleratedPeriod_ApiQuotaDoesNotAffectDailyPlan()
    {
        var planWithoutApi = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 0, CycleStart, CycleStart, Reset30Days);
        var planWithApi = DailyPlanCalculator.CalculateCombinedDailyPlan(100, 50, CycleStart, CycleStart, Reset30Days);

        Assert.Equal(planWithoutApi, planWithApi);
    }

    [Fact]
    public void AcceleratedPeriod_UnderSpendingCursor_IncreasesCursorDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(80, today, CycleStart, Reset30Days);

        Assert.InRange(plan, 5.7142, 5.7144);
    }

    [Fact]
    public void AcceleratedPeriod_OverSpendingCursor_DecreasesCursorDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(60, today, CycleStart, Reset30Days);

        Assert.InRange(plan, 4.2856, 4.2858);
    }

    [Fact]
    public void AcceleratedPeriod_PartialApiUsage_DoesNotChangePlannedApiSpend()
    {
        var today = CycleStart.AddDays(5);
        var apiRemaining = 70;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void AcceleratedPeriod_ExhaustedCursorQuota_ReturnsZeroCursorPlan()
    {
        var today = CycleStart.AddDays(10);

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateCombinedDailyPlan(0, 100, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void ReservePeriod_StartsOnDay22()
    {
        var today = CycleStart.AddDays(21);
        var cursorRemaining = 30.0;
        var apiRemaining = 10.0;
        var reserveDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, cursorRemaining);

        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(cursorRemaining, today, CycleStart, Reset30Days);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days);
        var combinedPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(cursorRemaining, apiRemaining, today, CycleStart, Reset30Days);

        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPeriod(today, CycleStart));
        Assert.Equal(9, reserveDays);
        Assert.InRange(cursorPlan, 3.3333, 3.3334);
        Assert.InRange(apiPlan, 1.1111, 1.1112);
        Assert.Equal(cursorPlan + apiPlan, combinedPlan, precision: 6);
    }

    [Fact]
    public void ReservePeriod_SpreadsRemainingCursorQuotaUntilReset()
    {
        var today = CycleStart.AddDays(24);
        var remaining = 4.0;
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, today, CycleStart, Reset30Days);

        Assert.Equal(6, DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining));
        Assert.InRange(plan, 0.6666, 0.6667);
    }

    [Fact]
    public void ReservePeriod_SpreadsRemainingApiQuotaUntilReset()
    {
        var today = CycleStart.AddDays(22);
        var remaining = 8.0;
        var plan = DailyPlanCalculator.CalculateApiDailyPlan(remaining, today, CycleStart, Reset30Days);

        Assert.Equal(8, DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining));
        Assert.Equal(1.0, plan, precision: 6);
    }

    [Fact]
    public void ReservePeriod_TracksBothQuotasSeparatelyWithoutMixingBalances()
    {
        var today = CycleStart.AddDays(21);
        var cursorRemaining = 20.0;
        var apiRemaining = 15.0;

        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(cursorRemaining, today, CycleStart, Reset30Days);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days);
        var combinedPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(cursorRemaining, apiRemaining, today, CycleStart, Reset30Days);

        Assert.Equal(20.0 / 9, cursorPlan, precision: 6);
        Assert.Equal(15.0 / 9, apiPlan, precision: 6);
        Assert.Equal(cursorPlan + apiPlan, combinedPlan, precision: 6);
    }

    [Fact]
    public void LastDayOfAcceleratedPeriod_UsesSingleRemainingDay()
    {
        var today = CycleStart.AddDays(20);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(12.5, today, CycleStart, Reset30Days);

        Assert.Equal(12.5, plan, precision: 6);
        Assert.Equal(1, DailyPlanCalculator.CalculateRemainingPlanDays(today, CycleStart.AddDays(21), 12.5));
    }

    [Fact]
    public void ReservePeriod_WithNoRemainder_ReturnsZeroPlan()
    {
        var today = CycleStart.AddDays(21);

        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(0, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void ThirtyDayCycle_Uses21DayAcceleratedBoundary()
    {
        var acceleratedEnd = DailyPlanCalculator.GetAcceleratedPeriodEnd(CycleStart);

        Assert.Equal(new DateTime(2025, 9, 27), acceleratedEnd);
        Assert.True(DailyPlanCalculator.IsWithinAcceleratedPeriod(CycleStart.AddDays(20), CycleStart));
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPeriod(CycleStart.AddDays(21), CycleStart));
        Assert.Equal(30, (Reset30Days - CycleStart).Days);
    }

    [Fact]
    public void ThirtyOneDayCycle_ReservePeriodStartsAfterDay21()
    {
        var cycleStart = new DateTime(2025, 10, 6);
        var reset = Reset31Days;
        var reserveStart = cycleStart.AddDays(21);
        var cursorRemaining = 25.0;
        var reserveDays = DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, reset, cursorRemaining);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(cursorRemaining, reserveStart, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(10, reserveDays);
        Assert.Equal(2.5, plan, precision: 6);
    }

    [Fact]
    public void FebruaryCycle_HandlesShortMonthCorrectly()
    {
        var cycleStart = new DateTime(2025, 1, 30);
        var reset = new DateTime(2025, 2, 28);
        var acceleratedEnd = DailyPlanCalculator.GetAcceleratedPeriodEnd(cycleStart);
        var dayBeforeReset = reset.AddDays(-1);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(8, dayBeforeReset, cycleStart, reset);

        Assert.Equal(new DateTime(2025, 2, 20), acceleratedEnd);
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPeriod(dayBeforeReset, cycleStart));
        Assert.Equal(8, plan, precision: 6);
    }

    [Fact]
    public void LeapYearFebruaryCycle_HandlesFebruary29Correctly()
    {
        var cycleStart = new DateTime(2024, 2, 6);
        var reset = new DateTime(2024, 3, 6);
        var reserveStart = cycleStart.AddDays(21);

        Assert.Equal(29, (reset - cycleStart).Days);
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPeriod(reserveStart, cycleStart));
        Assert.Equal(8, DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, reset, 16));
        Assert.Equal(2.0, DailyPlanCalculator.CalculateApiDailyPlan(16, reserveStart, cycleStart, reset), precision: 6);
    }

    [Fact]
    public void YearBoundaryCycle_UsesRealCalendarDates()
    {
        var cycleStart = new DateTime(2025, 12, 15);
        var reset = new DateTime(2026, 1, 15);
        var reserveStart = cycleStart.AddDays(21);
        var remaining = 18.0;
        var reserveDays = DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, reset, remaining);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, reserveStart, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(10, reserveDays);
        Assert.Equal(1.8, plan, precision: 6);
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
}

public class QuotaCalculatorTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_AcceleratedPeriod_UsesOnlyCursorModelForTotalDailyPlan()
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

        Assert.InRange(result.FirstParty.DailyTarget, 4.7618, 4.7620);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(result.FirstParty.DailyTarget, result.Total.DailyTarget);
    }

    [Fact]
    public void Calculate_AcceleratedPeriod_IgnoresApiQuotaInCombinedPlan()
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

        Assert.InRange(result.FirstParty.DailyTarget, 4.7618, 4.7620);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(result.FirstParty.DailyTarget, result.Total.DailyTarget);
        Assert.Equal(95, result.Api.RemainingPercent);
    }

    [Fact]
    public void Calculate_ReservePeriod_CombinesCursorAndApiPlans()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 50,
            FirstPartyUsedPercent = 60,
            ApiUsedPercent = 40,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var result = _calculator.Calculate(usage, CycleStart.AddDays(21));

        Assert.Equal(40.0 / 9, result.FirstParty.DailyTarget, precision: 6);
        Assert.Equal(60.0 / 9, result.Api.DailyTarget, precision: 6);
        Assert.Equal(result.FirstParty.DailyTarget + result.Api.DailyTarget, result.Total.DailyTarget, precision: 6);
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
        Assert.InRange(result.FirstParty.DailyTarget, 5.7142, 5.7144);
        Assert.Equal(result.FirstParty.DailyTarget, result.Total.DailyTarget);
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
