namespace Quota.Models;

/// <summary>Разложение pool usage на base allowance и bonus (bonus — часть total, не сверху).</summary>
public readonly record struct PoolQuotaBreakdown(
    decimal? BaseLimitUsd,
    decimal BaseUsedUsd,
    decimal BaseRemainingUsd,
    decimal BonusUsedUsd,
    BonusSource BonusSource,
    BonusAvailability BonusAvailability,
    bool HasKnownBonusAllowance,
    decimal? KnownBonusAllowanceUsd);
