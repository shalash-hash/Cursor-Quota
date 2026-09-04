using System.Globalization;
using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

public sealed class UsageHistoryService
{
    private readonly QuotaSnapshotRepository _snapshotRepository;

    public UsageHistoryService(QuotaSnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository;
    }

    public async Task<UsageHistoryResult> BuildAsync(
        UsageHistoryRange range,
        DateTime referenceTime,
        CultureInfo culture,
        DateTime? billingCycleStart = null)
    {
        var rangeStart = GetRangeStart(range, referenceTime, billingCycleStart);
        var snapshots = await _snapshotRepository.GetSnapshotsAsync(rangeStart, null);

        if (snapshots.Count == 0)
        {
            return new UsageHistoryResult
            {
                SnapshotCount = 0
            };
        }

        var snapshotsForAggregation = await IncludeRangeBaselineAsync(rangeStart, snapshots);

        var useHourlyBuckets = range == UsageHistoryRange.Today;
        var points = new List<UsageHistoryPoint>();

        var bucketGroups = snapshots
            .GroupBy(snapshot => GetBucketStart(snapshot.RetrievedAt, useHourlyBuckets, billingCycleStart))
            .OrderBy(group => group.Key);

        foreach (var bucketGroup in bucketGroups)
        {
            var bucketSnapshots = bucketGroup
                .OrderBy(snapshot => snapshot.RetrievedAt)
                .ToList();

            var dailySpent = AggregateBucketSpent(snapshotsForAggregation, bucketSnapshots);
            var cumulative = ResolveCumulativePercent(bucketSnapshots[^1]);
            var lastSnapshot = bucketSnapshots[^1];
            var daySpendUsd = AggregateBucketSpendUsd(snapshotsForAggregation, bucketSnapshots);
            var (modelsUsd, apiUsd, totalUsd) = UsageHistoryDayUsdResolver.Resolve(
                dailySpent,
                daySpendUsd,
                lastSnapshot);

            decimal? cumulativeUsd = lastSnapshot.TotalSpendCents is long cumulativeCents
                ? QuotaMonetaryHelper.CentsToUsd(cumulativeCents)
                : null;

            points.Add(new UsageHistoryPoint
            {
                BucketStart = bucketGroup.Key,
                Label = FormatBucketLabel(bucketGroup.Key, range, culture),
                TooltipLabel = FormatTooltipLabel(bucketGroup.Key, range, culture),
                DailySpentPercent = dailySpent.Total,
                DailyModelsPercent = dailySpent.FirstParty,
                DailyApiPercent = dailySpent.Api,
                CumulativeUsedPercent = cumulative,
                DailyModelsSpentUsd = modelsUsd,
                DailyApiSpentUsd = apiUsd > 0 ? apiUsd : null,
                DailyTotalSpentUsd = totalUsd > 0 ? totalUsd : null,
                CumulativeSpentUsd = cumulativeUsd
            });
        }

        return new UsageHistoryResult
        {
            Points = points,
            SnapshotCount = snapshots.Count,
            MaxDailySpentPercent = points.Count == 0
                ? 0
                : points.Max(point => point.DailySpentPercent)
        };
    }

    internal static DateTime GetRangeStart(
        UsageHistoryRange range,
        DateTime referenceTime,
        DateTime? billingCycleStart = null)
    {
        var today = billingCycleStart is DateTime cycleStart
            ? BillingCycleCalendar.GetDayStart(referenceTime, cycleStart)
            : referenceTime.Date;

        return range switch
        {
            UsageHistoryRange.Today => today,
            UsageHistoryRange.Week => today.AddDays(-6),
            UsageHistoryRange.Month => today.AddDays(-29),
            UsageHistoryRange.Year => today.AddDays(-364),
            UsageHistoryRange.AllTime => DateTime.MinValue,
            _ => today
        };
    }

    private static DateTime GetBucketStart(DateTime timestamp, bool hourly, DateTime? billingCycleStart)
    {
        if (hourly)
        {
            return new DateTime(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour,
                0,
                0,
                timestamp.Kind);
        }

        if (billingCycleStart is DateTime cycleStart)
            return BillingCycleCalendar.GetDayStart(timestamp, cycleStart);

        return timestamp.Date;
    }

    private async Task<IReadOnlyList<QuotaSnapshot>> IncludeRangeBaselineAsync(
        DateTime rangeStart,
        IReadOnlyList<QuotaSnapshot> snapshots)
    {
        if (rangeStart <= DateTime.MinValue)
            return snapshots;

        var priorSnapshots = await _snapshotRepository.GetSnapshotsAsync(null, rangeStart);
        if (priorSnapshots.Count == 0)
            return snapshots;

        return [priorSnapshots[^1], .. snapshots];
    }

    private static PoolDaySpent AggregateBucketSpent(
        IReadOnlyList<QuotaSnapshot> snapshotsForAggregation,
        IReadOnlyList<QuotaSnapshot> bucketSnapshots)
    {
        var orderedBucket = bucketSnapshots
            .OrderBy(snapshot => snapshot.RetrievedAt)
            .ToList();

        if (orderedBucket.Count == 0)
            return new PoolDaySpent(0, 0, 0);

        var prior = BuildBucketPriorSnapshots(snapshotsForAggregation, orderedBucket);
        return QuotaSnapshotAnalytics.ComputeSummedDayUsage(prior, orderedBucket[^1]);
    }

    private static DaySpendUsd AggregateBucketSpendUsd(
        IReadOnlyList<QuotaSnapshot> snapshotsForAggregation,
        IReadOnlyList<QuotaSnapshot> bucketSnapshots)
    {
        var orderedBucket = bucketSnapshots
            .OrderBy(snapshot => snapshot.RetrievedAt)
            .ToList();

        if (orderedBucket.Count == 0)
            return default;

        var prior = BuildBucketPriorSnapshots(snapshotsForAggregation, orderedBucket);
        return QuotaSpendResolver.ComputeSummedDaySpendUsd(prior, orderedBucket[^1]);
    }

    private static List<QuotaSnapshot> BuildBucketPriorSnapshots(
        IReadOnlyList<QuotaSnapshot> snapshotsForAggregation,
        IReadOnlyList<QuotaSnapshot> orderedBucket)
    {
        var prior = new List<QuotaSnapshot>();
        var baseline = FindBaselineSnapshot(snapshotsForAggregation, orderedBucket[0].RetrievedAt);
        if (baseline is not null)
            prior.Add(baseline);

        if (orderedBucket.Count > 1)
            prior.AddRange(orderedBucket.Take(orderedBucket.Count - 1));

        return prior;
    }

    private static QuotaSnapshot? FindBaselineSnapshot(
        IReadOnlyList<QuotaSnapshot> snapshotsForAggregation,
        DateTime bucketFirstRetrievedAt)
    {
        QuotaSnapshot? baseline = null;

        foreach (var snapshot in snapshotsForAggregation)
        {
            if (snapshot.RetrievedAt >= bucketFirstRetrievedAt)
                break;

            baseline = snapshot;
        }

        return baseline;
    }

    private static double ResolveCumulativePercent(QuotaSnapshot snapshot)
    {
        decimal? modelsLimit = null;
        if (snapshot.TotalSpendCents is long spendCents)
            modelsLimit = QuotaMonetaryHelper.EstimateLimitUsd(spendCents, snapshot.FirstPartyPercent);

        decimal? apiLimit = snapshot.LimitCents is long limitCents
            ? QuotaMonetaryHelper.CentsToUsd(limitCents)
            : null;

        var usage = new QuotaUsage
        {
            FirstPartyUsedPercent = snapshot.FirstPartyPercent,
            ApiUsedPercent = snapshot.ApiPercent,
            ModelsActualUsedUsd = QuotaSpendResolver.ResolveModelsActualUsedUsdFromSnapshot(snapshot),
            ModelsUsedUsd = QuotaSpendResolver.ResolveModelsActualUsedUsdFromSnapshot(snapshot),
            ApiUsedAmountUsd = QuotaSpendResolver.ResolveApiUsedUsdFromSnapshot(snapshot),
            ModelsEstimatedLimitUsd = modelsLimit,
            ApiIncludedAmountUsd = apiLimit,
            ModelsBaseLimitUsd = snapshot.ModelsBaseLimitCents is long baseCents
                ? QuotaMonetaryHelper.CentsToUsd(baseCents)
                : modelsLimit,
            TotalSpendCents = snapshot.TotalSpendCents
        };

        return QuotaMonetaryHelper.ResolveCombinedUsedPercent(usage) ?? snapshot.TotalPercent;
    }

    private static string FormatBucketLabel(
        DateTime bucketStart,
        UsageHistoryRange range,
        CultureInfo culture)
    {
        return range switch
        {
            UsageHistoryRange.Today => bucketStart.ToString("HH:mm", culture),
            UsageHistoryRange.Week => bucketStart.ToString("ddd", culture),
            UsageHistoryRange.Year or UsageHistoryRange.AllTime => bucketStart.ToString("dd.MM", culture),
            _ => bucketStart.ToString("dd.MM", culture)
        };
    }

    private static string FormatTooltipLabel(
        DateTime bucketStart,
        UsageHistoryRange range,
        CultureInfo culture)
    {
        return range switch
        {
            UsageHistoryRange.Today => bucketStart.ToString("HH:mm", culture),
            UsageHistoryRange.Week => bucketStart.ToString("dddd", culture),
            UsageHistoryRange.Year or UsageHistoryRange.AllTime => bucketStart.ToString("d MMMM yyyy", culture),
            _ => bucketStart.ToString("d MMMM yyyy", culture)
        };
    }
}
