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
    public void FirstDayOfCycle_UsesHillPlanHigherThanLinearAverage()
    {
        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(100, CycleStart, CycleStart, Reset30Days);
        var linearAverage = 100.0 / 25;

        Assert.Equal(0, DailyPlanCalculator.CalculateApiDailyPlan(10, CycleStart, CycleStart, Reset30Days));
        Assert.True(cursorPlan > linearAverage);
        Assert.InRange(cursorPlan, 7.5, 8.5);
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
    public void LateCycle_WithDeficit_DoesNotProduceUnrealisticDailyPlan()
    {
        var today = CycleStart.AddDays(18);
        var remaining = 61.28;
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(remaining, today, CycleStart, Reset30Days);
        var linearPlan = remaining / 7;

        Assert.True(plan < 10);
        Assert.True(plan < linearPlan * 2);
    }

    [Fact]
    public void LastCursorPlanDay_IsBeforeFiveDayApiReserve()
    {
        var planEnd = DailyPlanCalculator.GetCursorPlanEnd(Reset30Days);

        Assert.Equal(Reset30Days.AddDays(-5), planEnd);
        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(10, planEnd.AddDays(1), CycleStart, Reset30Days));
    }

    [Fact]
    public void ApiReservePeriod_SpreadsApiQuotaUntilReset()
    {
        var today = DailyPlanCalculator.GetApiPlanStart(Reset30Days);
        var remaining = 10.0;
        var plan = DailyPlanCalculator.CalculateApiDailyPlan(remaining, today, CycleStart, Reset30Days);

        Assert.Equal(2.0, plan, precision: 6);
        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(20, today, CycleStart, Reset30Days));
    }

    [Fact]
    public void ApiReservePeriod_TracksCursorAndApiSeparately()
    {
        var today = DailyPlanCalculator.GetApiPlanStart(Reset30Days);
        var cursorRemaining = 5.0;
        var apiRemaining = 15.0;

        var cursorPlan = DailyPlanCalculator.CalculateCursorModelDailyPlan(cursorRemaining, today, CycleStart, Reset30Days);
        var apiPlan = DailyPlanCalculator.CalculateApiDailyPlan(apiRemaining, today, CycleStart, Reset30Days);
        var combinedPlan = DailyPlanCalculator.CalculateCombinedDailyPlan(cursorRemaining, apiRemaining, today, CycleStart, Reset30Days);

        Assert.Equal(0, cursorPlan);
        Assert.Equal(3.0, apiPlan, precision: 6);
        Assert.Equal(apiPlan, combinedPlan, precision: 6);
    }

    [Fact]
    public void ThirtyDayCycle_UsesFiveDayReserveBeforeReset()
    {
        var planEnd = DailyPlanCalculator.GetCursorPlanEnd(Reset30Days);

        Assert.Equal(25, (planEnd - CycleStart).Days);
        Assert.Equal(30, (Reset30Days - CycleStart).Days);
        Assert.False(DailyPlanCalculator.IsWithinApiReservePeriod(CycleStart.AddDays(24), Reset30Days));
        Assert.True(DailyPlanCalculator.IsWithinApiReservePeriod(DailyPlanCalculator.GetApiPlanStart(Reset30Days), Reset30Days));
    }

    [Fact]
    public void ThirtyOneDayCycle_HillPlanEndIsFiveDaysBeforeReset()
    {
        var cycleStart = new DateTime(2025, 10, 6);
        var reset = Reset31Days;
        var planEnd = DailyPlanCalculator.GetCursorPlanEnd(reset);
        var today = planEnd.AddDays(-3);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(25, today, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(26, (planEnd - cycleStart).Days);
        Assert.True(plan > 0);
        Assert.True(plan < 25);
    }

    [Fact]
    public void FebruaryCycle_HandlesShortMonthCorrectly()
    {
        var cycleStart = new DateTime(2025, 1, 30);
        var reset = new DateTime(2025, 2, 28);
        var apiStart = DailyPlanCalculator.GetApiPlanStart(reset);

        Assert.Equal(new DateTime(2025, 2, 23), DailyPlanCalculator.GetCursorPlanEnd(reset));
        Assert.Equal(new DateTime(2025, 2, 24), apiStart);
        Assert.Equal(0, DailyPlanCalculator.CalculateCursorModelDailyPlan(8, apiStart, cycleStart, reset));
    }

    [Fact]
    public void LeapYearFebruaryCycle_HandlesFebruary29Correctly()
    {
        var cycleStart = new DateTime(2024, 2, 6);
        var reset = new DateTime(2024, 3, 6);
        var apiStart = DailyPlanCalculator.GetApiPlanStart(reset);

        Assert.Equal(29, (reset - cycleStart).Days);
        Assert.Equal(2.0, DailyPlanCalculator.CalculateApiDailyPlan(10, apiStart, cycleStart, reset), precision: 6);
    }

    [Fact]
    public void YearBoundaryCycle_UsesRealCalendarDates()
    {
        var cycleStart = new DateTime(2025, 12, 15);
        var reset = new DateTime(2026, 1, 15);
        var planEnd = DailyPlanCalculator.GetCursorPlanEnd(reset);
        var today = planEnd.AddDays(-2);
        var plan = DailyPlanCalculator.CalculateCursorModelDailyPlan(18, today, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(26, (planEnd - cycleStart).Days);
        Assert.True(plan > 0);
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
}

public class QuotaCalculatorTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_EarlyPeriod_UsesOnlyCursorModelForTotalDailyPlan()
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

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(result.FirstParty.DailyTarget, result.Total.DailyTarget);
    }

    [Fact]
    public void Calculate_EarlyPeriod_IgnoresApiQuotaInCombinedPlan()
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

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.Equal(result.FirstParty.DailyTarget, result.Total.DailyTarget);
        Assert.Equal(95, result.Api.RemainingPercent);
    }

    [Fact]
    public void Calculate_ApiReservePeriod_CombinesApiPlanOnly()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 50,
            FirstPartyUsedPercent = 60,
            ApiUsedPercent = 40,
            PeriodStart = CycleStart,
            PeriodEnd = Reset30Days
        };

        var today = DailyPlanCalculator.GetApiPlanStart(Reset30Days);
        var result = _calculator.Calculate(usage, today);

        Assert.Equal(0, result.FirstParty.DailyTarget);
        Assert.True(result.Api.DailyTarget > 0);
        Assert.Equal(result.Api.DailyTarget, result.Total.DailyTarget);
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
