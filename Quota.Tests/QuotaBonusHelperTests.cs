using Quota.Helpers;

using Quota.Models;

using Quota.Services;

using Quota.Services.CursorApi;

using Microsoft.Data.Sqlite;

using Xunit;



namespace Quota.Tests;



public class QuotaBonusHelperTests

{

    [Fact]

    public void ResolveModelsBonusUsed_Before100Percent_IsZero()

    {

        var bonus = QuotaBonusHelper.ResolveModelsBonusUsedUsd(200m, 450m);

        Assert.Equal(0m, bonus);

    }



    [Fact]

    public void ResolveModelsBonusUsed_After100Percent_IsExcessOverBase()

    {

        var bonus = QuotaBonusHelper.ResolveModelsBonusUsedUsd(460.56m, 450m);

        Assert.Equal(10.56m, bonus);

    }



    [Fact]

    public void BonusSpend_IsNotBonusAllowance()

    {

        var usage = CreateUsage(

            modelsActualUsd: 460.56m,

            totalSpendUsd: 460.56m,

            modelsBaseLimitUsd: 450m,

            bonusSpendCents: 44056);



        Assert.Equal(10.56m, usage.ModelsBonusUsedUsd);

        Assert.Equal(450m, usage.ModelsBaseLimitUsd);

        Assert.NotEqual(440.56m, usage.ModelsBonusUsedUsd);

    }



    [Fact]

    public void ClassifyBonusSource_FromPlanUsage_IsModels()

    {

        var source = QuotaBonusHelper.ClassifyBonusSourceFromPlanUsage(new PlanUsage

        {

            BonusSpend = 44056,

            AutoPercentUsed = 100,

            AutoBucketModels = true

        });



        Assert.Equal(BonusSource.Models, source);

    }



    [Fact]

    public void ClassifyBonusSource_WhenAutoBucketFalse_IsUnknown()

    {

        var source = QuotaBonusHelper.ClassifyBonusSourceFromPlanUsage(new PlanUsage

        {

            BonusSpend = 1000,

            RemainingBonus = true,

            AutoBucketModels = false

        });



        Assert.Equal(BonusSource.Unknown, source);

    }



    [Fact]

    public void RemainingBonusTrue_MarksBonusAvailable()

    {

        var availability = QuotaBonusHelper.ResolveBonusAvailability(true, 5m);

        Assert.Equal(BonusAvailability.Available, availability);

    }



    [Fact]

    public void RemainingBonusFalse_WithBonusUsage_MarksUnknown()

    {

        var availability = QuotaBonusHelper.ResolveBonusAvailability(false, 10.56m);

        Assert.Equal(BonusAvailability.Unknown, availability);

    }



    [Fact]

    public void RemainingBonusFalse_WithoutBonusUsage_MarksNone()

    {

        var availability = QuotaBonusHelper.ResolveBonusAvailability(false, 0m);

        Assert.Equal(BonusAvailability.None, availability);

    }



    [Fact]

    public void CombinedActual_IsRawTotalSpend_NotModelsPlusApi()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 465m,

            modelsActualUsd: 460m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 5m,

            apiLimitUsd: 20m);



        var combined = QuotaMonetaryHelper.ResolveCombinedUsedUsd(usage);

        Assert.Equal(465m, combined);
        Assert.NotEqual(470m, combined);
    }



    [Fact]

    public void CombinedDisplay_UsesBasePoolsOnly_InMainFraction()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 469.27m,

            modelsActualUsd: 463.35m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 5.92m,

            apiLimitUsd: 20m,

            modelsBonusUsedUsd: 13.35m,

            bonusAvailability: BonusAvailability.Unknown);



        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);



        Assert.Equal(455.92m, combined.UsedUsd);

        Assert.Equal(470m, combined.LimitUsd);

        Assert.Equal(14.08m, combined.RemainingUsd);

        Assert.InRange(combined.UsedPercent, 96.9, 97.1);

        Assert.Equal(13.35m, combined.ModelsBonusUsedUsd);

    }



    [Fact]

    public void CombinedDisplay_BonusDoesNotIncreaseDenominator()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 469.27m,

            modelsActualUsd: 463.35m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 5.92m,

            apiLimitUsd: 20m,

            modelsBonusUsedUsd: 13.35m);



        Assert.Equal(470m, QuotaMonetaryHelper.ResolveCombinedDisplay(usage).LimitUsd);

        Assert.NotEqual(483.35m, QuotaMonetaryHelper.ResolveCombinedDisplay(usage).LimitUsd);

    }



    [Fact]

    public void ApiDelta_DoesNotAffectModelsBonus_WhenTotalUnchanged()

    {

        var before = QuotaBonusHelper.ResolveModelsBonusUsedUsd(455m, 450m);

        var after = QuotaBonusHelper.ResolveModelsBonusUsedUsd(455m, 450m);

        Assert.Equal(before, after);

        Assert.Equal(5m, before);

    }



    [Fact]

    public void CombinedBaseLimit_ExcludesUnknownBonusAllowance()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 465m,

            modelsActualUsd: 460m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 5m,

            apiLimitUsd: 20m,

            modelsBonusUsedUsd: 10m);



        Assert.Equal(470m, QuotaMonetaryHelper.ResolveCombinedBaseLimitUsd(usage));

    }



    [Fact]

    public void CombinedUsedPercent_UsesBasePoolsOnly()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 465.24m,

            modelsActualUsd: 460.56m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 4.68m,

            apiLimitUsd: 20m,

            modelsBonusUsedUsd: 10.56m);



        var percent = QuotaMonetaryHelper.ResolveCombinedUsedPercent(usage);

        Assert.NotNull(percent);

        Assert.InRange(percent!.Value, 96.5, 97.0);

    }



    [Fact]

    public void ApiEffectiveLimit_IncludesKnownApiBonusAllowance()

    {

        var usage = EnrichedUsage(

            totalSpendUsd: 115m,

            modelsActualUsd: 100m,

            modelsBaseLimitUsd: 450m,

            apiUsedUsd: 15m,

            apiLimitUsd: 20m,

            apiKnownBonusAllowanceUsd: 10m,

            apiBonusUsedUsd: 5m);



        Assert.Equal(30m, QuotaBonusHelper.ResolveApiEffectiveLimitUsd(usage));

    }



    [Fact]

    public async Task Baseline_FreezesAt100Percent_DoesNotGrowWithSpend()

    {

        var dbPath = Path.Combine(Path.GetTempPath(), $"quota-bonus-{Guid.NewGuid():N}.db");

        var repository = new QuotaSnapshotRepository(dbPath);

        var periodStart = new DateTime(2026, 8, 6);

        var periodEnd = new DateTime(2026, 9, 6);



        try

        {

            var before100 = CreateRawUsage(

                periodStart,

                periodEnd,

                spendCents: 44550,

                autoPercent: 99.0);

            var enrichedBefore = await repository.EnrichWithBonusBaselineAsync(before100);

            Assert.Equal(450m, enrichedBefore.ModelsBaseLimitUsd);



            var at100 = CreateRawUsage(

                periodStart,

                periodEnd,

                spendCents: 45000,

                autoPercent: 100);

            var enrichedAt100 = await repository.EnrichWithBonusBaselineAsync(at100);

            Assert.Equal(450m, enrichedAt100.ModelsBaseLimitUsd);



            var afterBonus = CreateRawUsage(

                periodStart,

                periodEnd,

                spendCents: 46056,

                autoPercent: 100,

                remainingBonus: false,

                bonusSpendCents: 44056);

            var enrichedAfter = await repository.EnrichWithBonusBaselineAsync(afterBonus);



            Assert.Equal(450m, enrichedAfter.ModelsBaseLimitUsd);

            Assert.Equal(10.56m, enrichedAfter.ModelsBonusUsedUsd);

            Assert.Equal(BonusAvailability.Unknown, enrichedAfter.BonusAvailability);

            Assert.NotEqual(460.56m, enrichedAfter.ModelsBaseLimitUsd);

        }

        finally

        {

            SqliteConnection.ClearAllPools();

            if (File.Exists(dbPath))

                File.Delete(dbPath);

        }

    }



    [Fact]

    public async Task Baseline_RecoversFromHistory_WhenAppStartsAfter100()

    {

        var dbPath = Path.Combine(Path.GetTempPath(), $"quota-bonus-recover-{Guid.NewGuid():N}.db");

        var repository = new QuotaSnapshotRepository(dbPath);

        var periodStart = new DateTime(2026, 8, 6);

        var periodEnd = new DateTime(2026, 9, 6);



        try

        {

            var pre100 = CreateRawUsage(periodStart, periodEnd, 44550, 99.0);

            var enrichedPre = await repository.EnrichWithBonusBaselineAsync(pre100);

            await repository.EnrichWithTodayUsageAsync(enrichedPre);



            var coldStart = CreateRawUsage(

                periodStart,

                periodEnd,

                spendCents: 46056,

                autoPercent: 100,

                remainingBonus: false,

                bonusSpendCents: 44056);



            var recovered = await repository.EnrichWithBonusBaselineAsync(coldStart);

            Assert.Equal(450m, recovered.ModelsBaseLimitUsd);

            Assert.Equal(10.56m, recovered.ModelsBonusUsedUsd);

        }

        finally

        {

            SqliteConnection.ClearAllPools();

            if (File.Exists(dbPath))

                File.Delete(dbPath);

        }

    }



    [Fact]

    public async Task NewBillingCycle_ResetsBaseline()

    {

        var dbPath = Path.Combine(Path.GetTempPath(), $"quota-bonus-cycle-{Guid.NewGuid():N}.db");

        var repository = new QuotaSnapshotRepository(dbPath);



        try

        {

            var oldCycle = CreateRawUsage(

                new DateTime(2026, 8, 6),

                new DateTime(2026, 9, 6),

                46056,

                100,

                remainingBonus: false,

                bonusSpendCents: 44056);

            await repository.EnrichWithBonusBaselineAsync(oldCycle);



            var newCycle = CreateRawUsage(

                new DateTime(2026, 9, 6),

                new DateTime(2026, 10, 6),

                500,

                10);

            var enriched = await repository.EnrichWithBonusBaselineAsync(newCycle);



            Assert.Null(enriched.ModelsBonusUsedUsd);

            Assert.Equal(50m, enriched.ModelsBaseLimitUsd);

        }

        finally

        {

            SqliteConnection.ClearAllPools();

            if (File.Exists(dbPath))

                File.Delete(dbPath);

        }

    }



    [Fact]

    public async Task Migration_PreservesExistingSnapshots()

    {

        var dbPath = Path.Combine(Path.GetTempPath(), $"quota-bonus-migrate-{Guid.NewGuid():N}.db");

        var repository = new QuotaSnapshotRepository(dbPath);

        var periodStart = new DateTime(2026, 8, 6);

        var periodEnd = new DateTime(2026, 9, 6);



        try

        {

            var usage = CreateRawUsage(periodStart, periodEnd, 20000, 44.0);

            await repository.EnrichWithTodayUsageAsync(usage);



            var snapshots = await repository.GetSnapshotsAsync(null, null);

            Assert.Single(snapshots);

            Assert.Equal(20000, snapshots[0].TotalSpendCents);

            Assert.Null(snapshots[0].BonusSpendCents);

        }

        finally

        {

            SqliteConnection.ClearAllPools();

            if (File.Exists(dbPath))

                File.Delete(dbPath);

        }

    }



    private static QuotaUsage CreateUsage(

        decimal modelsActualUsd,

        decimal totalSpendUsd,

        decimal modelsBaseLimitUsd,

        long? bonusSpendCents = null) =>

        QuotaUsageEnricher.ApplyBonusBreakdown(

            new QuotaUsage

            {

                ModelsActualUsedUsd = modelsActualUsd,

                ModelsUsedUsd = modelsActualUsd,

                TotalSpendCents = (long)Math.Round(totalSpendUsd * 100m),

                FirstPartyUsedPercent = 100,

                BonusSpendCents = bonusSpendCents

            },

            (long)Math.Round(modelsBaseLimitUsd * 100m),

            BonusSource.Models);



    private static QuotaUsage EnrichedUsage(

        decimal totalSpendUsd,

        decimal modelsActualUsd,

        decimal modelsBaseLimitUsd,

        decimal apiUsedUsd,

        decimal apiLimitUsd,

        decimal? modelsBonusUsedUsd = null,

        decimal? apiKnownBonusAllowanceUsd = null,

        decimal? apiBonusUsedUsd = null,

        BonusAvailability bonusAvailability = BonusAvailability.None) =>

        new()

        {

            TotalSpendCents = (long)Math.Round(totalSpendUsd * 100m),

            ModelsActualUsedUsd = modelsActualUsd,

            ModelsUsedUsd = modelsActualUsd,

            ModelsBaseLimitUsd = modelsBaseLimitUsd,

            ModelsEstimatedLimitUsd = modelsBaseLimitUsd,

            ModelsBonusUsedUsd = modelsBonusUsedUsd

                ?? QuotaBonusHelper.ResolveModelsBonusUsedUsd(modelsActualUsd, modelsBaseLimitUsd),

            ApiUsedAmountUsd = apiUsedUsd,

            ApiIncludedAmountUsd = apiLimitUsd,

            ApiKnownBonusAllowanceUsd = apiKnownBonusAllowanceUsd,

            ApiBonusUsedUsd = apiBonusUsedUsd,

            BonusAvailability = bonusAvailability,

            FirstPartyUsedPercent = 100,

            ApiUsedPercent = (double)(apiUsedUsd / apiLimitUsd * 100m)

        };



    private static QuotaUsage CreateRawUsage(

        DateTime periodStart,

        DateTime periodEnd,

        long spendCents,

        double autoPercent,

        bool? remainingBonus = null,

        long? bonusSpendCents = null,

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

            LimitCents = 2000,

            RemainingBonus = remainingBonus,

            BonusSpendCents = bonusSpendCents,

            BonusSource = bonusSpendCents is > 0 ? BonusSource.Models : BonusSource.None

        };

}


