using System.Globalization;
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
        CultureInfo culture)
    {
        var rangeStart = GetRangeStart(range, referenceTime);
        var snapshots = await _snapshotRepository.GetSnapshotsAsync(rangeStart, null);

        if (snapshots.Count == 0)
        {
            return new UsageHistoryResult
            {
                SnapshotCount = 0
            };
        }

        var useHourlyBuckets = range == UsageHistoryRange.Today;
        var points = new List<UsageHistoryPoint>();

        var bucketGroups = snapshots
            .GroupBy(snapshot => GetBucketStart(snapshot.RetrievedAt, useHourlyBuckets))
            .OrderBy(group => group.Key);

        foreach (var bucketGroup in bucketGroups)
        {
            var bucketSnapshots = bucketGroup
                .OrderBy(snapshot => snapshot.RetrievedAt)
                .ToList();

            var dailySpent = AggregateBucketSpent(bucketSnapshots);
            var cumulative = bucketSnapshots[^1].TotalPercent;

            points.Add(new UsageHistoryPoint
            {
                BucketStart = bucketGroup.Key,
                Label = FormatBucketLabel(bucketGroup.Key, range, culture),
                TooltipLabel = FormatTooltipLabel(bucketGroup.Key, range, culture),
                DailySpentPercent = dailySpent.Total,
                DailyModelsPercent = dailySpent.FirstParty,
                DailyApiPercent = dailySpent.Api,
                CumulativeUsedPercent = cumulative
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

    internal static DateTime GetRangeStart(UsageHistoryRange range, DateTime referenceTime)
    {
        var today = referenceTime.Date;

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

    private static DateTime GetBucketStart(DateTime timestamp, bool hourly)
    {
        if (!hourly)
            return timestamp.Date;

        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            timestamp.Hour,
            0,
            0,
            timestamp.Kind);
    }

    private static PoolDaySpent AggregateBucketSpent(IReadOnlyList<QuotaSnapshot> bucketSnapshots)
    {
        var total = 0d;
        var models = 0d;
        var api = 0d;

        foreach (var periodGroup in bucketSnapshots.GroupBy(snapshot => (snapshot.PeriodStart, snapshot.PeriodEnd)))
        {
            var ordered = periodGroup.OrderBy(snapshot => snapshot.RetrievedAt).ToList();
            var delta = QuotaSnapshotAnalytics.ComputeDayDelta(ordered[0], ordered[^1]);
            total += delta.Total;
            models += delta.FirstParty;
            api += delta.Api;
        }

        return new PoolDaySpent(total, models, api);
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
