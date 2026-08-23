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
    public void FirstDayOfCycle_PlanTargetsFullQuotaOver21Days()
    {
        var plan = DailyPlanCalculator.CalculateDailyPlan(100, CycleStart, CycleStart, Reset30Days);

        Assert.InRange(plan, 4.7618, 4.7620);
    }

    [Fact]
    public void Day8WithUnderSpending_IncreasesDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var plan = DailyPlanCalculator.CalculateDailyPlan(80, today, CycleStart, Reset30Days);

        Assert.InRange(plan, 5.7142, 5.7144);
    }

    [Fact]
    public void Day8WithOverSpending_DecreasesDailyPlan()
    {
        var today = CycleStart.AddDays(7);
        var plan = DailyPlanCalculator.CalculateDailyPlan(60, today, CycleStart, Reset30Days);

        Assert.InRange(plan, 4.2856, 4.2858);
    }

    [Fact]
    public void LastDayOfAcceleratedPeriod_UsesSingleRemainingDay()
    {
        var today = CycleStart.AddDays(20);
        var plan = DailyPlanCalculator.CalculateDailyPlan(12.5, today, CycleStart, Reset30Days);

        Assert.Equal(12.5, plan, precision: 6);
        Assert.Equal(1, DailyPlanCalculator.CalculateRemainingPlanDays(today, CycleStart.AddDays(21), 12.5));
    }

    [Fact]
    public void FirstDayAfterAcceleratedPeriod_SwitchesToRealResetDate()
    {
        var today = CycleStart.AddDays(21);
        var remaining = 30.0;
        var expectedDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);
        var plan = DailyPlanCalculator.CalculateDailyPlan(remaining, today, CycleStart, Reset30Days);

        Assert.Equal(9, expectedDays);
        Assert.InRange(plan, 3.3333, 3.3334);
    }

    [Fact]
    public void AfterAcceleratedPeriodWithSmallRemainder_SpreadsEvenlyUntilReset()
    {
        var today = CycleStart.AddDays(24);
        var remaining = 4.0;
        var expectedDays = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remaining);
        var plan = DailyPlanCalculator.CalculateDailyPlan(remaining, today, CycleStart, Reset30Days);

        Assert.Equal(6, expectedDays);
        Assert.InRange(plan, 0.6666, 0.6667);
    }

    [Fact]
    public void AfterAcceleratedPeriodWithNoRemainder_ReturnsZeroPlan()
    {
        var today = CycleStart.AddDays(21);

        var plan = DailyPlanCalculator.CalculateDailyPlan(0, today, CycleStart, Reset30Days);

        Assert.Equal(0, plan);
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
        var remaining = 25.0;
        var reserveDays = DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, reset, remaining);
        var plan = DailyPlanCalculator.CalculateDailyPlan(remaining, reserveStart, cycleStart, reset);

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
        var plan = DailyPlanCalculator.CalculateDailyPlan(8, dayBeforeReset, cycleStart, reset);

        Assert.Equal(new DateTime(2025, 2, 20), acceleratedEnd);
        Assert.False(DailyPlanCalculator.IsWithinAcceleratedPeriod(dayBeforeReset, cycleStart));
        Assert.Equal(8, plan, precision: 6);
    }

    [Fact]
    public void YearBoundaryCycle_UsesRealCalendarDates()
    {
        var cycleStart = new DateTime(2025, 12, 15);
        var reset = new DateTime(2026, 1, 15);
        var reserveStart = cycleStart.AddDays(21);
        var remaining = 18.0;
        var reserveDays = DailyPlanCalculator.CalculateRemainingPlanDays(reserveStart, reset, remaining);
        var plan = DailyPlanCalculator.CalculateDailyPlan(remaining, reserveStart, cycleStart, reset);

        Assert.Equal(31, (reset - cycleStart).Days);
        Assert.Equal(10, reserveDays);
        Assert.Equal(1.8, plan, precision: 6);
    }

    [Fact]
    public void RemainingQuotaNeverProducesNegativeDailyPlan()
    {
        var today = CycleStart.AddDays(3);

        Assert.Equal(0, DailyPlanCalculator.CalculateDailyPlan(0, today, CycleStart, Reset30Days));
        Assert.Equal(0, DailyPlanCalculator.CalculateDailyPlan(-5, today, CycleStart, Reset30Days));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 1)]
    public void RemainingPlanDays_PreventsDivisionByZero(double remainingPercent, int expectedDays)
    {
        var today = Reset30Days;

        var days = DailyPlanCalculator.CalculateRemainingPlanDays(today, Reset30Days, remainingPercent);

        Assert.Equal(expectedDays, days);
        Assert.True(double.IsFinite(DailyPlanCalculator.CalculateDailyPlan(remainingPercent, today, CycleStart, Reset30Days)));
    }
}

public class QuotaCalculatorTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void Calculate_PreservesRemainingDaysUntilRealReset()
    {
        var usage = new QuotaUsage
        {
            TotalUsedPercent = 20,
            FirstPartyUsedPercent = 20,
            ApiUsedPercent = 0,
            PeriodStart = new DateTime(2025, 9, 6),
            PeriodEnd = new DateTime(2025, 10, 6)
        };

        var result = _calculator.Calculate(usage, new DateTime(2025, 9, 13));

        Assert.Equal(23, result.RemainingDays);
        Assert.InRange(result.Total.DailyTarget, 5.7142, 5.7144);
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
            PeriodStart = new DateTime(2025, 9, 6),
            PeriodEnd = new DateTime(2025, 10, 6)
        };

        var result = _calculator.Calculate(usage, new DateTime(2025, 9, 20));

        Assert.Equal(42.5, result.Total.UsedPercent);
        Assert.Equal(57.5, result.Total.RemainingPercent);
        Assert.Equal(30, result.FirstParty.UsedPercent);
        Assert.Equal(12.5, result.Api.UsedPercent);
    }
}
