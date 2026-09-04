using System.Globalization;
using Quota.Helpers;
using Quota.Models;
using Xunit;

namespace Quota.Tests;

public class UsageHistoryChartValuesTests
{
    [Fact]
    public void ResolveTotalDayUsd_HigherSpendDay_IsLarger_ThanLowerSpendDay()
    {
        var august27 = new UsageHistoryPoint
        {
            DailySpentPercent = 15.22,
            DailyModelsSpentUsd = 60.61m,
            DailyApiSpentUsd = 7.89m,
            DailyTotalSpentUsd = 68.50m
        };

        var september4 = new UsageHistoryPoint
        {
            DailySpentPercent = 63.45,
            DailyModelsSpentUsd = 4.80m,
            DailyApiSpentUsd = 7.89m,
            DailyTotalSpentUsd = 12.69m
        };

        var augustTotal = UsageHistoryChartValues.ResolveTotalDayUsd(august27);
        var septemberTotal = UsageHistoryChartValues.ResolveTotalDayUsd(september4);

        Assert.Equal(68.50m, augustTotal);
        Assert.Equal(12.69m, septemberTotal);
        Assert.True(augustTotal > septemberTotal);
    }

    [Fact]
    public void ResolveAxisMaxUsd_UsesHighestDailyTotal()
    {
        var points = new[]
        {
            new UsageHistoryPoint { DailyTotalSpentUsd = 68.50m },
            new UsageHistoryPoint { DailyTotalSpentUsd = 12.69m }
        };

        Assert.Equal(68.50, UsageHistoryChartValues.ResolveAxisMaxUsd(points), 3);
    }

    [Fact]
    public void BarHeightFractions_StackedModelsAndApi_EqualTotal()
    {
        const double axisMax = 68.50;
        var point = new UsageHistoryPoint
        {
            DailyModelsSpentUsd = 60.61m,
            DailyApiSpentUsd = 7.89m,
            DailyTotalSpentUsd = 68.50m
        };

        var modelsFraction = UsageHistoryChartValues.ResolveBarHeightFraction(
            UsageHistoryChartValues.ResolveModelsDayUsd(point),
            axisMax);
        var apiFraction = UsageHistoryChartValues.ResolveBarHeightFraction(
            UsageHistoryChartValues.ResolveApiDayUsd(point),
            axisMax);
        var totalFraction = UsageHistoryChartValues.ResolveBarHeightFraction(
            UsageHistoryChartValues.ResolveTotalDayUsd(point),
            axisMax);

        Assert.Equal(totalFraction, modelsFraction + apiFraction, 6);
        Assert.True(totalFraction > UsageHistoryChartValues.ResolveBarHeightFraction(12.69m, axisMax));
    }

    [Fact]
    public void ResolveTotalDayUsd_FallsBackToModelsPlusApi_WhenTotalMissing()
    {
        var point = new UsageHistoryPoint
        {
            DailyModelsSpentUsd = 10m,
            DailyApiSpentUsd = 2.5m
        };

        Assert.Equal(12.5m, UsageHistoryChartValues.ResolveTotalDayUsd(point));
    }

    [Fact]
    public void FormatUsdAxisTick_FormatsCurrency()
    {
        var formatted = UsageHistoryChartValues.FormatUsdAxisTick(68.5, CultureInfo.InvariantCulture);

        Assert.Contains("68", formatted);
        Assert.Contains("$", formatted);
    }
}
