using Quota.Models;

namespace Quota.Services;

internal static class QuotaSnapshotAnalytics
{
    /// Суммирует положительные приращения между последовательными снимками дня.
    /// Устойчиво к «скачку» API в начале дня, когда процент падает, а spend растёт.
    public static PoolDaySpent ComputeSummedDayUsage(
        IReadOnlyList<QuotaSnapshot> priorSnapshots,
        QuotaSnapshot current)
    {
        if (priorSnapshots.Count == 0)
            return new PoolDaySpent(0, 0, 0);

        var points = new List<QuotaSnapshot>(priorSnapshots.Count + 1);
        points.AddRange(priorSnapshots);
        points.Add(current);

        if (points.Count < 2)
            return new PoolDaySpent(0, 0, 0);

        double total = 0;
        double firstParty = 0;
        double api = 0;

        for (var i = 1; i < points.Count; i++)
        {
            var delta = ComputeDayDelta(points[i - 1], points[i]);
            total += delta.Total;
            firstParty += delta.FirstParty;
            api += delta.Api;
        }

        return new PoolDaySpent(total, firstParty, api);
    }

    public static PoolDaySpent ComputeDayDelta(QuotaSnapshot first, QuotaSnapshot last)
    {
        var todayApi = PositiveDelta(last.ApiPercent, first.ApiPercent);
        var todayFirstParty = PositiveDelta(last.FirstPartyPercent, first.FirstPartyPercent);

        // Bonus/on-demand: процент моделей может стоять, растёт spend.
        // Spend→% здесь только для пула моделей, не для «общей квоты».
        if (todayFirstParty < 0.001
            && TrySpendDeltaPercent(first, last, out var spendDeltaPercent)
            && spendDeltaPercent > 0.001)
        {
            todayFirstParty = Math.Max(0, spendDeltaPercent - todayApi);
        }

        var poolTotal = todayFirstParty + todayApi;
        return new PoolDaySpent(poolTotal, todayFirstParty, todayApi);
    }

    private static bool TrySpendDeltaPercent(
        QuotaSnapshot first,
        QuotaSnapshot last,
        out double deltaPercent)
    {
        deltaPercent = 0;

        if (first.TotalSpendCents is not long firstSpend || last.TotalSpendCents is not long lastSpend)
            return false;

        var limit = last.LimitCents ?? first.LimitCents;
        if (limit is not > 0)
            return false;

        deltaPercent = Math.Max(0, lastSpend - firstSpend) / (double)limit * 100;
        return deltaPercent > 0 || lastSpend > firstSpend;
    }

  public static long ComputeSummedSpendCentsDelta(
        IReadOnlyList<QuotaSnapshot> priorSnapshots,
        QuotaSnapshot current)
    {
        if (priorSnapshots.Count == 0)
            return 0;

        var points = new List<QuotaSnapshot>(priorSnapshots.Count + 1);
        points.AddRange(priorSnapshots);
        points.Add(current);

        if (points.Count < 2)
            return 0;

        long total = 0;
        for (var i = 1; i < points.Count; i++)
            total += PositiveSpendDelta(points[i - 1].TotalSpendCents, points[i].TotalSpendCents);

        return total;
    }

    private static long PositiveSpendDelta(long? baseline, long? current)
    {
        if (baseline is not long first || current is not long last)
            return 0;

        return Math.Max(0, last - first);
    }

    private static double PositiveDelta(double current, double baseline) =>
        Math.Max(0, current - baseline);
}
