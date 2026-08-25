namespace Quota.Models;

public sealed class UsageHistoryRangeOption
{
    public UsageHistoryRange Range { get; init; }

    public string DisplayName { get; init; } = string.Empty;
}
