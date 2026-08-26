using Microsoft.Data.Sqlite;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class QuotaSnapshotAnalyticsTests
{
    [Fact]
    public void ComputeDayDelta_WhenIncludedLimitReached_UsesModelPercentDelta()
    {
        var first = new QuotaSnapshot
        {
            FirstPartyPercent = 38.7044444444444,
            ApiPercent = 0,
            TotalSpendCents = null,
            IncludedSpendCents = null,
            LimitCents = null
        };

        var last = new QuotaSnapshot
        {
            FirstPartyPercent = 39.7955555555556,
            ApiPercent = 0,
            TotalSpendCents = 17908,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var delta = QuotaSnapshotAnalytics.ComputeDayDelta(first, last);

        Assert.True(delta.Total > 0.5, $"Expected meaningful delta, got {delta.Total}");
        Assert.Equal(delta.FirstParty, delta.Total, precision: 3);
    }

    [Fact]
    public void ComputeDayDelta_SpendGrowth_DoesNotInflateTotalQuotaToday()
    {
        var first = new QuotaSnapshot
        {
            FirstPartyPercent = 39.65,
            ApiPercent = 0,
            TotalSpendCents = 17846,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var last = new QuotaSnapshot
        {
            FirstPartyPercent = 39.65,
            ApiPercent = 0,
            TotalSpendCents = 17912,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var delta = QuotaSnapshotAnalytics.ComputeDayDelta(first, last);

        Assert.InRange(delta.FirstParty, 3.0, 3.5);
        Assert.Equal(delta.FirstParty, delta.Total, precision: 3);
        Assert.True(delta.Total < 5, $"Total should stay pool-sized, got {delta.Total}");
    }

    [Fact]
    public void ComputeSummedDayUsage_TotalMatchesModelAndApiPools()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot { FirstPartyPercent = 38.7, ApiPercent = 0 },
            new QuotaSnapshot { FirstPartyPercent = 39.2, ApiPercent = 0 },
            new QuotaSnapshot { FirstPartyPercent = 39.79, ApiPercent = 0 },
        };

        var current = new QuotaSnapshot
        {
            FirstPartyPercent = 40.05,
            ApiPercent = 0,
            TotalSpendCents = 18023,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var summed = QuotaSnapshotAnalytics.ComputeSummedDayUsage(snapshots, current);

        Assert.InRange(summed.FirstParty, 1.0, 2.0);
        Assert.Equal(summed.FirstParty, summed.Total, precision: 3);
        Assert.True(summed.Total < 5, $"Expected ~1-2% total quota today, got {summed.Total}");
    }

    [Fact]
    public void ComputeDayDelta_WhenIncludedLimitReached_UsesBonusSpendDelta()
    {
        var first = new QuotaSnapshot
        {
            FirstPartyPercent = 39.65,
            ApiPercent = 0,
            TotalSpendCents = 17846,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var last = new QuotaSnapshot
        {
            FirstPartyPercent = 39.65,
            ApiPercent = 0,
            TotalSpendCents = 17912,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var delta = QuotaSnapshotAnalytics.ComputeDayDelta(first, last);

        Assert.InRange(delta.Total, 3.0, 3.5);
    }

    [Fact]
    public void ComputeSummedDayUsage_IgnoresMidnightApiDrop_AndSumsAfternoonSpend()
    {
        var midnight = new QuotaSnapshot
        {
            FirstPartyPercent = 57.98,
            ApiPercent = 0,
            TotalSpendCents = null
        };

        var afternoon = new QuotaSnapshot
        {
            FirstPartyPercent = 39.38,
            ApiPercent = 0,
            TotalSpendCents = 17721,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var later = new QuotaSnapshot
        {
            FirstPartyPercent = 39.79,
            ApiPercent = 0,
            TotalSpendCents = 17908,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var current = new QuotaSnapshot
        {
            FirstPartyPercent = 40.05,
            ApiPercent = 0,
            TotalSpendCents = 18023,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

        var summed = QuotaSnapshotAnalytics.ComputeSummedDayUsage(
            [midnight, afternoon, later],
            current);

        Assert.True(summed.Total > 0.5, $"Expected intraday spend sum, got {summed.Total}");
    }

    [Fact]
    public async Task EnrichWithTodayUsage_SumsIntradySnapshots_WhenMidnightBaselineIsStale()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"quota-test-{Guid.NewGuid():N}.db");
        var repository = new QuotaSnapshotRepository(databasePath);

        try
        {
            var periodStart = new DateTime(2026, 8, 6, 12, 33, 42);
            var periodEnd = new DateTime(2026, 9, 6, 12, 33, 42);

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 25, 15, 31, 23),
                total: 38.704,
                firstParty: 38.704,
                api: 0,
                totalSpend: null,
                includedSpend: null));

            var enriched = await repository.EnrichWithTodayUsageAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 25, 16, 44, 34),
                total: 39.795,
                firstParty: 39.795,
                api: 0,
                totalSpend: 17908,
                includedSpend: 2000,
                limit: 2000));

            Assert.True(enriched.TodayTotalUsedPercent > 0.5, $"Today={enriched.TodayTotalUsedPercent}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EnrichWithTodayUsage_KeepsSameBillingDay_AfterCalendarMidnight()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"quota-test-{Guid.NewGuid():N}.db");
        var repository = new QuotaSnapshotRepository(databasePath);

        try
        {
            var periodStart = new DateTime(2026, 8, 6, 12, 36, 42);
            var periodEnd = new DateTime(2026, 9, 6, 12, 36, 42);

            await repository.SaveSnapshotAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 26, 23, 10, 0),
                total: 40,
                firstParty: 40,
                api: 0,
                totalSpend: 18000,
                includedSpend: 2000,
                limit: 2000));

            var enriched = await repository.EnrichWithTodayUsageAsync(CreateUsage(
                periodStart,
                periodEnd,
                new DateTime(2026, 8, 27, 0, 30, 0),
                total: 41.2,
                firstParty: 41.2,
                api: 0,
                totalSpend: 18540,
                includedSpend: 2000,
                limit: 2000));

            Assert.True(enriched.TodayTotalUsedPercent > 0.5, $"Today={enriched.TodayTotalUsedPercent}");
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
