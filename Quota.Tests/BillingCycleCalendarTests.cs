using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class BillingCycleCalendarTests
{
    [Fact]
    public void GetDayStart_BeforeRollover_StaysOnPreviousBillingDay()
    {
        var periodStart = new DateTime(2026, 8, 6, 12, 36, 42);
        var now = new DateTime(2026, 8, 27, 0, 30, 0);

        var dayStart = BillingCycleCalendar.GetDayStart(now, periodStart);

        Assert.Equal(new DateTime(2026, 8, 26, 12, 36, 42), dayStart);
        Assert.Equal(new DateTime(2026, 8, 27, 12, 36, 42), BillingCycleCalendar.GetDayEnd(now, periodStart));
    }

    [Fact]
    public void GetDayStart_AfterRollover_StartsNewBillingDay()
    {
        var periodStart = new DateTime(2026, 8, 6, 12, 36, 42);
        var now = new DateTime(2026, 8, 27, 12, 36, 42);

        var dayStart = BillingCycleCalendar.GetDayStart(now, periodStart);

        Assert.Equal(now, dayStart);
    }

    [Fact]
    public void CountRemainingDays_BeforeRolloverOnResetCalendarDate_StillCountsTheDay()
    {
        var periodEnd = new DateTime(2026, 9, 6, 12, 36, 42);
        var now = new DateTime(2026, 9, 6, 0, 30, 0);

        var days = BillingCycleCalendar.CountRemainingDays(now, periodEnd, remainingPercent: 50);

        Assert.Equal(1, days);
    }
}
