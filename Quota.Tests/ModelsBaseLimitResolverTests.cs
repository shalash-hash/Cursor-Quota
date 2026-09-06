using Microsoft.Data.Sqlite;
using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class ModelsBaseLimitResolverTests
{
    [Fact]
    public async Task FreshCycle_ZeroUsage_InheritsPreviousFrozenModelsBaseLimit()
    {
        var dbPath = CreateTempDbPath();
        var repository = new QuotaSnapshotRepository(dbPath);

        try
        {
            var previousCycle = CreateRawUsage(
                new DateTime(2026, 8, 6, 12, 33, 42),
                new DateTime(2026, 9, 6, 12, 33, 42),
                spendCents: 44550,
                autoPercent: 99.0);

            await repository.EnrichWithBonusBaselineAsync(previousCycle);

            var freshCycle = CreateRawUsage(
                new DateTime(2026, 9, 6, 12, 33, 42),
                new DateTime(2026, 10, 6, 12, 33, 42),
                spendCents: 0,
                autoPercent: 0);

            var enriched = await repository.EnrichWithBonusBaselineAsync(freshCycle);

            Assert.Equal(450m, enriched.ModelsBaseLimitUsd);
            Assert.Equal(20m, enriched.ApiIncludedAmountUsd);
            Assert.Equal(470m, QuotaMonetaryHelper.ResolveCombinedBaseLimitUsd(enriched));
            Assert.Equal(0m, QuotaMonetaryHelper.ResolveCombinedBaseUsedUsd(enriched));
            Assert.Equal(450m, QuotaMonetaryHelper.ResolveModelsRemainingUsd(enriched));

            var currentFrozen = await repository.GetFrozenModelsBaseLimitCentsAsync(
                freshCycle.PeriodStart,
                freshCycle.PeriodEnd);
            Assert.Null(currentFrozen);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task FirstSpend_UsesLiveEstimate_OverInheritedSeed()
    {
        var dbPath = CreateTempDbPath();
        var repository = new QuotaSnapshotRepository(dbPath);

        try
        {
            var previousCycle = CreateRawUsage(
                new DateTime(2026, 8, 6, 12, 33, 42),
                new DateTime(2026, 9, 6, 12, 33, 42),
                spendCents: 44550,
                autoPercent: 99.0);
            await repository.EnrichWithBonusBaselineAsync(previousCycle);

            var newCycle = CreateRawUsage(
                new DateTime(2026, 9, 6, 12, 33, 42),
                new DateTime(2026, 10, 6, 12, 33, 42),
                spendCents: 500,
                autoPercent: 10);

            var enriched = await repository.EnrichWithBonusBaselineAsync(newCycle);

            Assert.Equal(50m, enriched.ModelsBaseLimitUsd);
            Assert.Equal(5000, await repository.GetFrozenModelsBaseLimitCentsAsync(
                newCycle.PeriodStart,
                newCycle.PeriodEnd));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task QuotaChangedBetweenCycles_LiveEstimateReplacesInheritedSeed()
    {
        var dbPath = CreateTempDbPath();
        var repository = new QuotaSnapshotRepository(dbPath);

        try
        {
            var previousCycle = CreateRawUsage(
                new DateTime(2026, 8, 6, 12, 33, 42),
                new DateTime(2026, 9, 6, 12, 33, 42),
                spendCents: 44550,
                autoPercent: 99.0);
            await repository.EnrichWithBonusBaselineAsync(previousCycle);

            var newCycle = CreateRawUsage(
                new DateTime(2026, 9, 6, 12, 33, 42),
                new DateTime(2026, 10, 6, 12, 33, 42),
                spendCents: 25000,
                autoPercent: 50);

            var enriched = await repository.EnrichWithBonusBaselineAsync(newCycle);

            Assert.Equal(500m, enriched.ModelsBaseLimitUsd);
            Assert.Equal(50000, await repository.GetFrozenModelsBaseLimitCentsAsync(
                newCycle.PeriodStart,
                newCycle.PeriodEnd));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void CombinedBaseLimit_ReturnsNull_WhenModelsUnknownAndApiKnown()
    {
        var usage = new QuotaUsage
        {
            ApiIncludedAmountUsd = 20m,
            LimitCents = 2000
        };

        Assert.Null(QuotaMonetaryHelper.ResolveCombinedBaseLimitUsd(usage));
    }

    [Fact]
    public async Task FreshCycle_DirectAutoLimit_HasPriorityOverPreviousSeed()
    {
        var dbPath = CreateTempDbPath();
        var repository = new QuotaSnapshotRepository(dbPath);

        try
        {
            var previousCycle = CreateRawUsage(
                new DateTime(2026, 8, 6, 12, 33, 42),
                new DateTime(2026, 9, 6, 12, 33, 42),
                spendCents: 44550,
                autoPercent: 99.0);
            await repository.EnrichWithBonusBaselineAsync(previousCycle);

            var freshCycle = new QuotaUsage
            {
                PeriodStart = new DateTime(2026, 9, 6, 12, 33, 42),
                PeriodEnd = new DateTime(2026, 10, 6, 12, 33, 42),
                RetrievedAt = DateTime.Now,
                TotalSpendCents = 0,
                FirstPartyUsedPercent = 0,
                ApiUsedPercent = 0,
                ApiIncludedAmountUsd = 20m,
                LimitCents = 2000,
                AutoLimitCents = 50000
            };

            var enriched = await repository.EnrichWithBonusBaselineAsync(freshCycle);

            Assert.Equal(500m, enriched.ModelsBaseLimitUsd);
            Assert.Equal(50000, await repository.GetFrozenModelsBaseLimitCentsAsync(
                freshCycle.PeriodStart,
                freshCycle.PeriodEnd));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void FreshCycle_DailyPlan_ModelsPositive_ApiZero()
    {
        var usage = new QuotaUsage
        {
            PeriodStart = new DateTime(2026, 9, 6, 12, 33, 42),
            PeriodEnd = new DateTime(2026, 10, 6, 12, 33, 42),
            RetrievedAt = new DateTime(2026, 9, 6, 14, 0, 0),
            FirstPartyUsedPercent = 0,
            ApiUsedPercent = 0,
            ModelsBaseLimitUsd = 450m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            TotalUsedPercent = 0
        };

        var result = new QuotaCalculator().Calculate(usage);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(0, result.Api.DailyTarget);
        Assert.True(result.Total.DailyTarget > 0);
    }

    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"quota-models-base-{Guid.NewGuid():N}.db");

    private static void CleanupDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    private static QuotaUsage CreateRawUsage(
        DateTime periodStart,
        DateTime periodEnd,
        long spendCents,
        double autoPercent,
        double apiPercent = 0) =>
        new()
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RetrievedAt = DateTime.Now,
            TotalSpendCents = spendCents,
            FirstPartyUsedPercent = autoPercent,
            ApiUsedPercent = apiPercent,
            ApiIncludedAmountUsd = 20m,
            LimitCents = 2000
        };
}
