using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class UsageHistoryServiceTests
{
    [Theory]
    [InlineData(UsageHistoryRange.Today, 0)]
    [InlineData(UsageHistoryRange.Week, -6)]
    [InlineData(UsageHistoryRange.Month, -29)]
    [InlineData(UsageHistoryRange.Year, -364)]
    public void GetRangeStart_UsesExpectedOffsets(UsageHistoryRange range, int dayOffset)
    {
        var reference = new DateTime(2026, 8, 25, 15, 30, 0);

        var start = UsageHistoryService.GetRangeStart(range, reference);

        Assert.Equal(reference.Date.AddDays(dayOffset), start);
    }

    [Fact]
    public void GetRangeStart_AllTime_ReturnsMinValue()
    {
        var start = UsageHistoryService.GetRangeStart(
            UsageHistoryRange.AllTime,
            new DateTime(2026, 8, 25));

        Assert.Equal(DateTime.MinValue, start);
    }
}
