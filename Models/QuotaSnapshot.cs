namespace Quota.Models;

public sealed class QuotaSnapshot
{
    public DateTime RetrievedAt { get; init; }

    public DateTime PeriodStart { get; init; }

    public DateTime PeriodEnd { get; init; }

    public double TotalPercent { get; init; }

    public double FirstPartyPercent { get; init; }

    public double ApiPercent { get; init; }

    public long? TotalSpendCents { get; init; }

    public long? IncludedSpendCents { get; init; }

    public long? LimitCents { get; init; }
}
