namespace Quota.Helpers;

public enum DailyPlanDeltaKind
{
    NoPlan,
    OnPlan,
    Ahead,
    Behind
}

public readonly struct DailyPlanDelta
{
    public DailyPlanDeltaKind Kind { get; init; }

    /// <summary>Относительное отклонение от дневного плана, % (всегда ≥ 0 для Ahead/Behind).</summary>
    public double RelativeDeltaPercent { get; init; }

    /// <summary>Абсолютная разница в процентных пунктах общей квоты: today − plan.</summary>
    public double AbsoluteDeltaPercent { get; init; }
}

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
    private const decimal UsdEqualityEpsilon = 0.01m;

    /// <summary>Каноническое сравнение факта и плана в USD (не смешивать pool-проценты).</summary>
    public static DailyPlanDelta CalculatePlanDeltaFromUsd(decimal todayUsd, decimal dailyPlanUsd)
    {
        if (dailyPlanUsd <= 0)
        {
            if (todayUsd <= 0)
                return new DailyPlanDelta { Kind = DailyPlanDeltaKind.NoPlan };

            return new DailyPlanDelta
            {
                Kind = DailyPlanDeltaKind.Ahead,
                RelativeDeltaPercent = 100,
                AbsoluteDeltaPercent = (double)todayUsd
            };
        }

        var absoluteDelta = todayUsd - dailyPlanUsd;
        if (Math.Abs(absoluteDelta) < UsdEqualityEpsilon)
            return new DailyPlanDelta { Kind = DailyPlanDeltaKind.OnPlan };

        var relativeDelta = (double)(Math.Abs(absoluteDelta) / dailyPlanUsd * 100m);
        if (absoluteDelta > 0)
        {
            return new DailyPlanDelta
            {
                Kind = DailyPlanDeltaKind.Ahead,
                RelativeDeltaPercent = relativeDelta,
                AbsoluteDeltaPercent = (double)absoluteDelta
            };
        }

        return new DailyPlanDelta
        {
            Kind = DailyPlanDeltaKind.Behind,
            RelativeDeltaPercent = relativeDelta,
            AbsoluteDeltaPercent = (double)absoluteDelta
        };
    }

    public static decimal CalculateDeltaUsdFromValues(decimal todayUsd, decimal dailyPlanUsd) =>
        todayUsd - dailyPlanUsd;

    public static DailyTargetProgressState CalculateFromUsd(decimal todayUsd, decimal dailyPlanUsd) =>
        Calculate((double)todayUsd, (double)dailyPlanUsd);

    /// <summary>План выполнен, если факт ≥ плана в USD (канон для combined Models + API).</summary>
    public static bool IsDailyPlanCompletedFromUsd(decimal todayUsd, decimal dailyPlanUsd) =>
        CalculatePlanDeltaFromUsd(todayUsd, dailyPlanUsd).Kind
            is DailyPlanDeltaKind.Ahead or DailyPlanDeltaKind.OnPlan;

    public static DailyPlanDelta CalculatePlanDelta(double todayUsed, double dailyTarget)
    {
        if (dailyTarget <= 0)
        {
            if (todayUsed <= 0)
            {
                return new DailyPlanDelta { Kind = DailyPlanDeltaKind.NoPlan };
            }

            return new DailyPlanDelta
            {
                Kind = DailyPlanDeltaKind.Ahead,
                RelativeDeltaPercent = 100,
                AbsoluteDeltaPercent = todayUsed
            };
        }

        var absoluteDelta = todayUsed - dailyTarget;
        if (Math.Abs(absoluteDelta) < 1e-12)
        {
            return new DailyPlanDelta { Kind = DailyPlanDeltaKind.OnPlan };
        }

        var relativeDelta = absoluteDelta / dailyTarget * 100;
        if (absoluteDelta > 0)
        {
            return new DailyPlanDelta
            {
                Kind = DailyPlanDeltaKind.Ahead,
                RelativeDeltaPercent = relativeDelta,
                AbsoluteDeltaPercent = absoluteDelta
            };
        }

        return new DailyPlanDelta
        {
            Kind = DailyPlanDeltaKind.Behind,
            RelativeDeltaPercent = -relativeDelta,
            AbsoluteDeltaPercent = absoluteDelta
        };
    }

    public static decimal? CalculateDeltaUsd(double todayPercent, double planPercent, decimal? limitUsd)
    {
        if (limitUsd is null or <= 0m)
            return null;

        return QuotaMonetaryHelper.PercentToUsd(todayPercent, limitUsd.Value)
            - QuotaMonetaryHelper.PercentToUsd(planPercent, limitUsd.Value);
    }

    public static string FormatRelativeDeltaWithUsd(
        double relativeDeltaPercent,
        decimal? deltaUsd,
        int decimalPlaces,
        System.Globalization.CultureInfo culture)
    {
        var percentText = PercentageFormatter.Format(relativeDeltaPercent, decimalPlaces, culture);
        if (deltaUsd is null)
            return percentText;

        return string.Format(
            culture,
            "{0} ({1})",
            percentText,
            QuotaMonetaryHelper.FormatUsd(Math.Abs(deltaUsd.Value), culture));
    }

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
