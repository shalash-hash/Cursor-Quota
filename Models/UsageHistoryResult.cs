namespace Quota.Models;

public sealed class UsageHistoryResult
{
    public IReadOnlyList<UsageHistoryPoint> Points { get; init; } = [];

    public int SnapshotCount { get; init; }

    public double MaxDailySpentPercent { get; init; }

    public bool HasData => Points.Count > 0;
}
