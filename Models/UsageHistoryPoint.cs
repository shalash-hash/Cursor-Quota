namespace Quota.Models;

public sealed class UsageHistoryPoint
{
    public DateTime BucketStart { get; init; }

    public string Label { get; init; } = string.Empty;

    public string TooltipLabel { get; init; } = string.Empty;

    public double DailySpentPercent { get; init; }

    public double DailyModelsPercent { get; init; }

    public double DailyApiPercent { get; init; }

    public double CumulativeUsedPercent { get; init; }
}
