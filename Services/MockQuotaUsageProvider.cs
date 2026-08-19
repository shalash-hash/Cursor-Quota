using Quota.Models;

namespace Quota.Services;

public class MockQuotaUsageProvider : IQuotaUsageProvider
{
    private const double TotalUsedPercent = 20.0;
    private const double FirstPartyUsedPercent = 23.0;
    private const double ApiUsedPercent = 0.0;
    private const int RemainingDays = 19;

    public Task<QuotaUsage> GetUsageAsync()
    {
        var now = DateTime.Now;
        var periodEnd = now.Date.AddDays(RemainingDays);
        var periodStart = periodEnd.AddDays(-30);

        var usage = new QuotaUsage
        {
            TotalUsedPercent = TotalUsedPercent,
            FirstPartyUsedPercent = FirstPartyUsedPercent,
            ApiUsedPercent = ApiUsedPercent,
            TodayTotalUsedPercent = 1.2,
            TodayFirstPartyUsedPercent = 1.5,
            TodayApiUsedPercent = 0,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RetrievedAt = now,
            PlanName = "Pro",
            ApiIncludedAmountUsd = 20m,
            ApiUsedAmountUsd = 0m,
            ApiRemainingAmountUsd = 20m
        };

        return Task.FromResult(usage);
    }
}
