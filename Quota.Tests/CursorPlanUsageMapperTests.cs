using Quota.Services;
using Quota.Services.CursorApi;
using Xunit;

namespace Quota.Tests;

public class CursorPlanUsageMapperTests
{
    [Fact]
    public void Map_UsesCombinedDollarLimit_WhenIncludedSpendIsCapped()
    {
        var usage = CursorPlanUsageMapper.Map(
            new PlanUsage
            {
                TotalSpend = 17675,
                IncludedSpend = 2000,
                Limit = 2000,
                TotalPercentUsed = 35.7,
                AutoPercentUsed = 39.2,
                ApiPercentUsed = 0
            },
            new DateTime(2026, 8, 1),
            new DateTime(2026, 9, 1),
            "Pro",
            2000);

        Assert.InRange(usage.TotalUsedPercent, 37.5, 37.65);
        Assert.Equal(39.2, usage.FirstPartyUsedPercent, precision: 3);
        Assert.Equal(0, usage.ApiUsedPercent, precision: 3);
        Assert.Equal(17675, usage.TotalSpendCents);
    }

    [Fact]
    public void Map_FallsBackToApiTotalPercent_WhenCombinedLimitUnknown()
    {
        var usage = CursorPlanUsageMapper.Map(
            new PlanUsage
            {
                TotalPercentUsed = 42.5,
                AutoPercentUsed = 30,
                ApiPercentUsed = 12.5
            },
            new DateTime(2026, 8, 1),
            new DateTime(2026, 9, 1),
            null,
            null);

        Assert.Equal(42.5, usage.TotalUsedPercent, precision: 3);
        Assert.Equal(30, usage.FirstPartyUsedPercent, precision: 3);
        Assert.Equal(12.5, usage.ApiUsedPercent, precision: 3);
    }

    [Fact]
    public void Map_TotalPercent_UsesCombinedDollarLimit_ForModelsAndApi()
    {
        var usage = CursorPlanUsageMapper.Map(
            new PlanUsage
            {
                TotalSpend = 17675,
                IncludedSpend = 2000,
                Limit = 2000,
                AutoPercentUsed = 39.2,
                ApiPercentUsed = 5.3
            },
            new DateTime(2026, 8, 1),
            new DateTime(2026, 9, 1),
            "Pro",
            2000);

        Assert.InRange(usage.TotalUsedPercent, 37.5, 37.65);
        Assert.Equal(39.2, usage.FirstPartyUsedPercent, precision: 3);
        Assert.Equal(5.3, usage.ApiUsedPercent, precision: 3);
        Assert.NotEqual(usage.TotalUsedPercent, usage.FirstPartyUsedPercent);
    }
}
