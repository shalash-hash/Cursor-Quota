namespace Quota.Helpers;

public readonly struct DailyTargetProgressState
{
    public bool IsExceeded { get; init; }

    public double FillPercent { get; init; }

    public double NormSegmentWeight { get; init; }

    public double AheadSegmentWeight { get; init; }

    public double PlanCompletionPercent { get; init; }
}

public static class DailyTargetProgressCalculator
{
    public static DailyTargetProgressState Calculate(double todayUsed, double dailyTarget)
    {
        if (dailyTarget <= 0)
        {
            if (todayUsed <= 0)
            {
                return new DailyTargetProgressState
                {
                    FillPercent = 0,
                    PlanCompletionPercent = 0
                };
            }

            return new DailyTargetProgressState
            {
                IsExceeded = true,
                NormSegmentWeight = 0,
                AheadSegmentWeight = 1,
                PlanCompletionPercent = 100
            };
        }

        if (todayUsed <= dailyTarget)
        {
            var completion = Math.Min(100, todayUsed / dailyTarget * 100);
            return new DailyTargetProgressState
            {
                FillPercent = completion,
                PlanCompletionPercent = completion
            };
        }

        var overage = todayUsed - dailyTarget;
        return new DailyTargetProgressState
        {
            IsExceeded = true,
            NormSegmentWeight = dailyTarget / todayUsed,
            AheadSegmentWeight = overage / todayUsed,
            PlanCompletionPercent = 100
        };
    }
}
