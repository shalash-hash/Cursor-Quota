namespace Quota.Models;

public class QuotaCalculationResult
{
    public int RemainingDays { get; init; }

    public PoolCalculation Total { get; init; } = new();

    public PoolCalculation FirstParty { get; init; } = new();

    public PoolCalculation Api { get; init; } = new();
}
