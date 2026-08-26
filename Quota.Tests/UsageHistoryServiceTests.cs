using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task BuildAsync_Today_UsesBaselineBeforeRange_ForSingleSnapshot()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"quota-history-{Guid.NewGuid():N}.db");
        var repository = new QuotaSnapshotRepository(databasePath);
        var service = new UsageHistoryService(repository);

        try
        {
            var periodStart = new DateTime(2026, 8, 6, 12, 0, 0);
            var periodEnd = new DateTime(2026, 9, 6, 12, 0, 0);
            var reference = new DateTime(2026, 8, 25, 16, 0, 0);

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 24, 22, 0, 0),
                total: 35,
                firstParty: 35,
                api: 0,
                totalSpend: 7000,
                includedSpend: 2000,
                limit: 2000));

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 25, 15, 30, 0),
                total: 39,
                firstParty: 39,
                api: 0,
                totalSpend: 7800,
                includedSpend: 2000,
                limit: 2000));

            var result = await service.BuildAsync(
                UsageHistoryRange.Today,
                reference,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.Single(result.Points);
            Assert.True(result.Points[0].DailySpentPercent > 0.5, $"Daily={result.Points[0].DailySpentPercent}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task BuildAsync_Week_ShowsTodaySpend_WhenOnlyOneSnapshotExistsToday()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"quota-history-{Guid.NewGuid():N}.db");
        var repository = new QuotaSnapshotRepository(databasePath);
        var service = new UsageHistoryService(repository);

        try
        {
            var periodStart = new DateTime(2026, 8, 6, 12, 0, 0);
            var periodEnd = new DateTime(2026, 9, 6, 12, 0, 0);
            var reference = new DateTime(2026, 8, 25, 16, 0, 0);

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 24, 22, 0, 0),
                total: 35,
                firstParty: 35,
                api: 0,
                totalSpend: 7000,
                includedSpend: 2000,
                limit: 2000));

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 25, 15, 30, 0),
                total: 39,
                firstParty: 39,
                api: 0,
                totalSpend: 7800,
                includedSpend: 2000,
                limit: 2000));

            var result = await service.BuildAsync(
                UsageHistoryRange.Week,
                reference,
                System.Globalization.CultureInfo.InvariantCulture);

            var todayPoint = result.Points.Single(point => point.BucketStart.Date == reference.Date);
            Assert.True(todayPoint.DailySpentPercent > 0.5, $"Daily={todayPoint.DailySpentPercent}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static QuotaUsage CreateUsage(
        DateTime periodStart,
        DateTime periodEnd,
        DateTime retrievedAt,
        double total,
        double firstParty,
        double api,
        long? totalSpend,
        long? includedSpend,
        long? limit = null) =>
        new()
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RetrievedAt = retrievedAt,
            TotalUsedPercent = total,
            FirstPartyUsedPercent = firstParty,
            ApiUsedPercent = api,
            TotalSpendCents = totalSpend,
            IncludedSpendCents = includedSpend,
            LimitCents = limit
        };
}
