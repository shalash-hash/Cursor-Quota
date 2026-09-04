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

    public long? TotalSpendCents { get; init; }

    public long? IncludedSpendCents { get; init; }

    public long? LimitCents { get; init; }

    /// <summary>Raw combined period spend (Cursor totalSpend). Не прибавлять API повторно.</summary>
    public decimal? TotalPeriodSpendUsd =>
        TotalSpendCents is long cents ? cents / 100m : null;

    /// <summary>Фактический расход Models (не raw totalSpend).</summary>
    public decimal? ModelsActualUsedUsd { get; init; }

    /// <summary>Фактический расход Models. Синоним ModelsActualUsedUsd для обратной совместимости.</summary>
    public decimal? ModelsUsedUsd { get; init; }

    public long? AutoSpendCents { get; init; }

    public long? ApiSpendCents { get; init; }

    public long? AutoLimitCents { get; init; }

    public long? ApiLimitCents { get; init; }

    /// <summary>Raw totalPercentUsed из Cursor. Не использовать для bonus allowance.</summary>
    public double? RawTotalPercentUsed { get; init; }

    /// Оценка месячного лимита моделей: spend × 100 / percent.
    public decimal? ModelsEstimatedLimitUsd { get; init; }

    public decimal? ModelsEstimatedRemainingUsd { get; init; }

    public long? TodayTotalSpendCents { get; init; }

    public long? TodayModelsSpendCents { get; init; }

    public long? TodayApiSpendCents { get; init; }

    public long? YesterdayTotalSpendCents { get; init; }

    public long? YesterdayModelsSpendCents { get; init; }

    public long? YesterdayApiSpendCents { get; init; }

    public bool HasYesterdaySpendData { get; init; }

    /// <summary>Raw bonusSpend из Cursor API (накопительный provider-subsidized spend, НЕ allowance).</summary>
    public long? BonusSpendCents { get; init; }

    public bool? RemainingBonus { get; init; }

    public string? BonusTooltip { get; init; }

    public BonusSource BonusSource { get; init; }

    public BonusAvailability BonusAvailability { get; init; }

    /// <summary>Зафиксированный base Models allowance (~$450), не растёт после 100%.</summary>
    public decimal? ModelsBaseLimitUsd { get; init; }

    /// <summary>Фактически использованный Models bonus сверх base limit.</summary>
    public decimal? ModelsBonusUsedUsd { get; init; }

    public long? ModelsBaseLimitCents { get; init; }

    public BonusSource ApiBonusSource { get; init; }

    public decimal? ApiBonusUsedUsd { get; init; }

    /// <summary>Известный API bonus allowance (если Cursor когда-либо отдаст total).</summary>
    public decimal? ApiKnownBonusAllowanceUsd { get; init; }
}
