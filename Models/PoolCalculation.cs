namespace Quota.Models;

public class PoolCalculation
{
    public double UsedPercent { get; init; }

    public double RemainingPercent { get; init; }

    public double DailyTarget { get; init; }

    public double TodayUsed { get; init; }

    public double TodayRemaining { get; init; }

    public bool IsTodayPlanCompleted { get; init; }

    public double TodayOverage { get; init; }

    public PaceStatus PaceStatus { get; init; }
}
