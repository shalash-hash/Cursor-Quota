using System.Globalization;
using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class QuotaMonetaryHelperTests
{
    [Fact]
    public void EstimateLimitCents_MatchesObservedCursorRatio()
    {
        var limit = QuotaMonetaryHelper.EstimateLimitCents(18108, 40.24);
        Assert.Equal(45000, limit);
        Assert.Equal(450m, QuotaMonetaryHelper.EstimateLimitUsd(18108, 40.24));
    }

    [Fact]
    public void CentsToUsd_RoundsToTwoDecimals()
    {
        Assert.Equal(181.08m, QuotaMonetaryHelper.CentsToUsd(18108));
    }

    [Fact]
    public void FormatSpendRange_ShowsEstimatedLimit()
    {
        var text = QuotaMonetaryHelper.FormatSpendRange(
            181.08m,
            450m,
            CultureInfo.InvariantCulture);

        Assert.Equal("$181.08 из ~$450.00", text);
    }
}
