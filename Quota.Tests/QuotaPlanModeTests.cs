using System.Text.Json;
using Quota.Helpers;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public class QuotaPlanModeTests
{
    private readonly QuotaCalculator _calculator = new();

    [Fact]
    public void DefaultSettings_WithoutPlanMode_UseOptimal()
    {
        var json = """
            {
              "percentageDecimalPlaces": 2,
              "isDarkMode": false
            }
            """;

        var settings = JsonSerializer.Deserialize<UiSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(QuotaPlanMode.Optimal, settings!.QuotaPlanMode);
    }

    [Fact]
    public void OptimalEarlyCycle_IncludesApiInDailyPlan()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var usage = CreateUsage(cycleStart, periodEnd, 0, 0);

        var result = _calculator.Calculate(usage, QuotaPlanMode.Optimal, cycleStart);

        Assert.True(result.Api.DailyTarget > 0);
        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.Equal(
            QuotaMonetaryHelper.ResolveCombinedDayPercent(
                result.FirstParty.DailyTarget,
                result.Api.DailyTarget,
                usage.ModelsEstimatedLimitUsd,
                usage.ApiIncludedAmountUsd),
            result.Total.DailyTarget,
            precision: 4);
    }

    [Fact]
    public void UniformEarlyCycle_DistributesModelsAndApi()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var usage = CreateUsage(cycleStart, periodEnd, 0, 0);

        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, cycleStart);

        Assert.True(result.FirstParty.DailyTarget > 0);
        Assert.True(result.Api.DailyTarget > 0);
        var combinedUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);
        Assert.InRange(combinedUsd, 15.60m, 15.75m);
        Assert.Equal(
            QuotaMonetaryHelper.ResolveCombinedDayPercent(
                result.FirstParty.DailyTarget,
                result.Api.DailyTarget,
                usage.ModelsEstimatedLimitUsd,
                usage.ApiIncludedAmountUsd),
            result.Total.DailyTarget,
            precision: 4);
    }

    [Fact]
    public void UniformCombinedTarget_MatchesRemainingUsdOverDays()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var usage = CreateUsage(cycleStart, periodEnd, 0, 0);

        var result = _calculator.Calculate(usage, QuotaPlanMode.Uniform, cycleStart);
        var dailyUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            result.FirstParty.DailyTarget,
            result.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);

        Assert.InRange(dailyUsd, 15.60m, 15.75m);
    }

    [Fact]
    public void UniformAfterUnderspend_IncreasesNextTarget()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var dayOne = CreateUsage(cycleStart, periodEnd, 0, 0);
        var dayTwo = CreateUsage(cycleStart, periodEnd, 1, 0.5);

        var firstTarget = _calculator.Calculate(dayOne, QuotaPlanMode.Uniform, cycleStart).Total.DailyTarget;
        var secondTarget = _calculator.Calculate(dayTwo, QuotaPlanMode.Uniform, cycleStart.AddDays(1)).Total.DailyTarget;

        Assert.True(secondTarget > firstTarget);
    }

    [Fact]
    public void UniformAfterOverspend_DecreasesNextTarget()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var dayOne = CreateUsage(cycleStart, periodEnd, 0, 0);
        var dayTwo = CreateUsage(cycleStart, periodEnd, 8, 4);

        var firstTarget = _calculator.Calculate(dayOne, QuotaPlanMode.Uniform, cycleStart).Total.DailyTarget;
        var secondTarget = _calculator.Calculate(dayTwo, QuotaPlanMode.Uniform, cycleStart.AddDays(1)).Total.DailyTarget;

        Assert.True(secondTarget < firstTarget);
    }

    [Fact]
    public void SameUsage_OptimalAndUniformProduceDifferentTargets()
    {
        var cycleStart = new DateTime(2026, 9, 6, 12, 33, 42);
        var periodEnd = new DateTime(2026, 10, 6, 12, 33, 42);
        var usage = CreateUsage(cycleStart, periodEnd, 0, 0);

        var optimal = _calculator.Calculate(usage, QuotaPlanMode.Optimal, cycleStart);
        var uniform = _calculator.Calculate(usage, QuotaPlanMode.Uniform, cycleStart);

        Assert.NotEqual(optimal.Total.DailyTarget, uniform.Total.DailyTarget);
    }

    [Fact]
    public void SettingsPersistence_StoresSelectedMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quota-plan-mode-{Guid.NewGuid():N}.json");
        var service = new UiSettingsService();

        try
        {
            File.WriteAllText(path, """{"percentageDecimalPlaces":2,"quotaPlanMode":"Uniform"}""");
            var loaded = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path));

            Assert.NotNull(loaded);
            Assert.Equal(QuotaPlanMode.Uniform, loaded!.QuotaPlanMode);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static QuotaUsage CreateUsage(
        DateTime periodStart,
        DateTime periodEnd,
        double modelsUsedPercent,
        double apiUsedPercent) =>
        new()
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RetrievedAt = periodStart,
            FirstPartyUsedPercent = modelsUsedPercent,
            ApiUsedPercent = apiUsedPercent,
            TotalUsedPercent = modelsUsedPercent + apiUsedPercent,
            ModelsBaseLimitUsd = 450m,
            ModelsEstimatedLimitUsd = 450m,
            ApiIncludedAmountUsd = 20m,
            LimitCents = 2000
        };
}
