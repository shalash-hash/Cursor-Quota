using Quota.Models;

namespace Quota.Services;

public interface IQuotaUsageProvider
{
    Task<QuotaUsage> GetUsageAsync();
}
