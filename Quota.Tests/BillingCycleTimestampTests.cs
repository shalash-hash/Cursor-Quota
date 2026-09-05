using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class BillingCycleTimestampTests
{
  private const long KnownEndUnixMs = 1788676422000L;

    [Fact]
    public void ToDateTimeOffset_KnownUnixMs_MatchesUtcInstant()
    {
        var offset = BillingCycleTimestamp.ToDateTimeOffset(KnownEndUnixMs);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 6, 33, 42, TimeSpan.Zero), offset);
    }

    [Fact]
    public void ToLocalDateTime_KnownUnixMs_MatchesSystemLocalConversion()
    {
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(KnownEndUnixMs).LocalDateTime;
        var actual = BillingCycleTimestamp.ToLocalDateTime(KnownEndUnixMs);

        Assert.Equal(expected, actual);
        Assert.Equal(DateTimeKind.Local, actual.Kind);
    }

    [Fact]
    public void ComputeRemaining_FromUnixMs_MatchesUtcNowDifference()
    {
        var remaining = BillingCycleTimestamp.ComputeRemaining(KnownEndUnixMs);
        var expectedMs = KnownEndUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expected = expectedMs <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(expectedMs);

        Assert.Equal(expected, remaining);
    }

    [Fact]
    public void ComputeRemaining_FromUnixMs_NeverNegative()
    {
        var remaining = BillingCycleTimestamp.ComputeRemaining(1);

        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void ComputeRemaining_PrefersUnixMillisecondsOverLocalDateTime()
    {
        var futureMs = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        var staleLocal = DateTime.Now.AddHours(5);

        var remaining = BillingCycleTimestamp.ComputeRemaining(futureMs, staleLocal);

        Assert.InRange(remaining.TotalHours, 1.9, 2.1);
    }

    [Fact]
    public void ParseUnixMilliseconds_InvalidValue_Throws()
    {
        Assert.Throws<FormatException>(() => BillingCycleTimestamp.ParseUnixMilliseconds("not-a-number"));
    }
}
