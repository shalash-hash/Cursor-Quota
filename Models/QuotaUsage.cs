namespace Quota.Models;

public class QuotaUsage
{
    public double TotalUsedPercent { get; init; }

    public double FirstPartyUsedPercent { get; init; }

    public double ApiUsedPercent { get; init; }

    public double TodayTotalUsedPercent { get; init; }

    public double TodayFirstPartyUsedPercent { get; init; }

    public double TodayApiUsedPercent { get; init; }

    public double YesterdayTotalUsedPercent { get; init; }

    public double YesterdayFirstPartyUsedPercent { get; init; }

    public double YesterdayApiUsedPercent { get; init; }

    public bool HasYesterdayUsageData { get; init; }

    public DateTime PeriodStart { get; init; }

    public DateTime PeriodEnd { get; init; }

    public DateTime RetrievedAt { get; init; }

    public string? PlanName { get; init; }

    public decimal? ApiIncludedAmountUsd { get; init; }

    public decimal? ApiUsedAmountUsd { get; init; }

    public decimal? ApiRemainingAmountUsd { get; init; }
}
