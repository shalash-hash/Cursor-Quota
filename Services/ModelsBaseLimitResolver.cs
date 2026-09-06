using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

internal static class ModelsBaseLimitResolver
{
    private const double ModelsPercentCap = 99.999;

    public static async Task<long?> ResolveBaseLimitCentsAsync(
        QuotaSnapshotRepository repository,
        QuotaUsage usage)
    {
        if (usage.AutoLimitCents is long autoLimitCents && autoLimitCents > 0)
        {
            await repository.UpsertFrozenModelsBaseLimitCentsAsync(
                usage.PeriodStart,
                usage.PeriodEnd,
                autoLimitCents);
            return autoLimitCents;
        }

        if (usage.TotalSpendCents is long spendCents
            && spendCents > 0
            && usage.FirstPartyUsedPercent > 0.0001
            && usage.FirstPartyUsedPercent < ModelsPercentCap)
        {
            var estimate = QuotaMonetaryHelper.EstimateLimitCents(spendCents, usage.FirstPartyUsedPercent);
            if (estimate is long liveEstimate && liveEstimate > 0)
            {
                await repository.UpsertFrozenModelsBaseLimitCentsAsync(
                    usage.PeriodStart,
                    usage.PeriodEnd,
                    liveEstimate);
                return liveEstimate;
            }
        }

        var frozen = await repository.GetFrozenModelsBaseLimitCentsAsync(usage.PeriodStart, usage.PeriodEnd);
        if (frozen is long stored && stored > 0)
            return stored;

        var recovered = await repository.RecoverModelsBaseLimitCentsFromHistoryAsync(
            usage.PeriodStart,
            usage.PeriodEnd);
        if (recovered is long recoveredCents && recoveredCents > 0)
        {
            await repository.UpsertFrozenModelsBaseLimitCentsAsync(
                usage.PeriodStart,
                usage.PeriodEnd,
                recoveredCents);
            return recoveredCents;
        }

        var firstAt100Spend = await repository.GetFirstModels100SpendCentsAsync(
            usage.PeriodStart,
            usage.PeriodEnd);
        if (firstAt100Spend is long at100 && at100 > 0)
        {
            await repository.UpsertFrozenModelsBaseLimitCentsAsync(
                usage.PeriodStart,
                usage.PeriodEnd,
                at100);
            return at100;
        }

        var previousCycle = await repository.GetLatestPreviousModelsBaseLimitCentsAsync(usage.PeriodStart);
        if (previousCycle is long previousCents && previousCents > 0)
            return previousCents;

        return null;
    }
}
