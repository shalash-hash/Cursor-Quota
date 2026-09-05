namespace Quota.Helpers;

public static class BillingCycleTimestamp
{
    public static long ParseUnixMilliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, out var milliseconds))
            throw new FormatException("Не удалось разобрать Unix timestamp billing cycle.");

        return milliseconds;
    }

    public static DateTimeOffset ToDateTimeOffset(long unixMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);

    public static DateTime ToLocalDateTime(long unixMilliseconds) =>
        ToDateTimeOffset(unixMilliseconds).LocalDateTime;

    /// <summary>
    /// Оставшееся время до reset — тот же расчёт, что в Cursor UI: billingCycleEndMs - UtcNowMs.
    /// </summary>
    public static TimeSpan ComputeRemaining(long periodEndUnixMilliseconds)
    {
        if (periodEndUnixMilliseconds <= 0)
            return TimeSpan.Zero;

        var remainingMs = periodEndUnixMilliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromMilliseconds(remainingMs);
    }

    public static TimeSpan ComputeRemaining(DateTime periodEndLocal)
    {
        if (periodEndLocal == default)
            return TimeSpan.Zero;

        var normalized = periodEndLocal.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(periodEndLocal, DateTimeKind.Local),
            _ => periodEndLocal
        };

        var remaining = normalized - DateTime.Now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public static TimeSpan ComputeRemaining(long periodEndUnixMilliseconds, DateTime periodEndLocal)
    {
        if (periodEndUnixMilliseconds > 0)
            return ComputeRemaining(periodEndUnixMilliseconds);

        return ComputeRemaining(periodEndLocal);
    }
}
