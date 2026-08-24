namespace Quota.Models;

public readonly record struct PoolDaySpent(
    double Total,
    double FirstParty,
    double Api);
