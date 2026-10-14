namespace Quota.Models;

public class QuotaCalculationResult
{
    public int RemainingDays { get; init; }

    public PoolCalculation Total { get; init; } = new();

    public PoolCalculation FirstParty { get; init; } = new();

    public PoolCalculation Api { get; init; } = new();

    /// <summary>Плановый cumulative combined used (% шкалы 0–100) на текущую billing-дату.</summary>
    public double PlannedCumulativeUsedPercent { get; init; }

    public decimal? PlannedCumulativeUsedUsd { get; init; }
}
