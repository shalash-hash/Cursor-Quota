using System.Globalization;
using Quota.Helpers;
using Quota.Localization;
using Quota.Models;
using Xunit;

namespace Quota.Tests;

public sealed class TrayDisplayFormatterTests
{
    private readonly TrayFakeLocalization _localization = new();

    [Fact]
    public void Create_CombinedLimit_IncludesApiPool_NotModelsOnly()
    {
        var usage = CreateUsageWithBonus();
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);
        var culture = _localization.CurrentCulture;

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            null,
            2,
            _localization);

        var expectedCombinedLine = QuotaMonetaryHelper.FormatSpendRange(
            combined.UsedUsd!.Value,
            combined.LimitUsd,
            culture);

        Assert.Equal(470m, combined.LimitUsd);
        Assert.NotEqual(450m, combined.LimitUsd);
        Assert.Contains(state.InfoMenuLines, line => line == expectedCombinedLine);
        Assert.DoesNotContain(
            state.InfoMenuLines,
            line => line.Contains(QuotaMonetaryHelper.FormatUsd(450m, culture), StringComparison.Ordinal)
                && line.Contains(QuotaMonetaryHelper.FormatUsd(466.50m, culture), StringComparison.Ordinal));
    }

    [Fact]
    public void Create_CombinedUsedUsd_MatchesResolveCombinedDisplay()
    {
        var usage = CreateUsageWithBonus();
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            null,
            2,
            _localization);

        var combinedLine = QuotaMonetaryHelper.FormatSpendRange(
            combined.UsedUsd!.Value,
            combined.LimitUsd,
            _localization.CurrentCulture);

        Assert.Equal(463.18m, combined.UsedUsd);
        Assert.Contains(state.InfoMenuLines, line => line == combinedLine);
    }

    [Fact]
    public void Create_WhenBonusUsedUsdPositive_IncludesBonusLineWithoutIncreasingCombinedLimit()
    {
        var usage = CreateUsageWithBonus();
        var breakdown = QuotaBonusHelper.ResolveModelsBreakdown(usage);
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            new DateTime(2026, 9, 5, 22, 1, 45),
            2,
            _localization);

        Assert.Contains(
            state.InfoMenuLines,
            line => line.Contains("Models bonus:", StringComparison.Ordinal)
                && line.Contains(QuotaMonetaryHelper.FormatUsd(breakdown.BonusUsedUsd, _localization.CurrentCulture), StringComparison.Ordinal));
        Assert.Equal(470m, combined.LimitUsd);
        Assert.Equal(16.50m, breakdown.BonusUsedUsd);
    }

    [Fact]
    public void Create_WhenBonusUsedUsdZero_OmitsBonusLine()
    {
        var usage = CreateUsageWithBonus(modelsActualUsd: 400m, modelsBonusUsedUsd: 0m);

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            new DateTime(2026, 9, 5, 22, 1, 45),
            2,
            _localization);

        Assert.DoesNotContain(
            state.InfoMenuLines,
            line => line.Contains("Models bonus:", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_ApiLine_CombinesPercentAndSpend()
    {
        var usage = CreateUsageWithBonus();
        var culture = _localization.CurrentCulture;
        var apiPercent = PercentageFormatter.Format(usage.ApiUsedPercent, 2, culture);
        var expectedApiLine = _localization.Format(
            "TrayMenuApiWithSpendFormat",
            QuotaMonetaryHelper.FormatUsd(usage.ApiUsedAmountUsd!.Value, culture),
            QuotaMonetaryHelper.FormatUsd(usage.ApiIncludedAmountUsd!.Value, culture),
            apiPercent);

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            null,
            2,
            _localization);

        Assert.Single(state.InfoMenuLines, line => line == expectedApiLine);
        Assert.Equal(1, state.InfoMenuLines.Count(line => line.StartsWith("API:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Create_LineOrder_PlacesCombinedSpendBeforeBonusAndApiLine()
    {
        var usage = CreateUsageWithBonus();
        var culture = _localization.CurrentCulture;
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);
        var combinedLine = QuotaMonetaryHelper.FormatSpendRange(
            combined.UsedUsd!.Value,
            combined.LimitUsd,
            culture);

        var state = TrayDisplayFormatter.Create(
            TrayDataState.Ready,
            usage,
            new DateTime(2026, 9, 5, 22, 1, 45),
            2,
            _localization);

        var lines = state.InfoMenuLines;
        var totalPercentIndex = IndexOfContaining(lines, "Total:");
        var modelsPercentIndex = IndexOfContaining(lines, "Cursor Models:");
        var combinedSpendIndex = IndexOfExact(lines, combinedLine);
        var bonusIndex = IndexOfContaining(lines, "Models bonus:");
        var apiLineIndex = IndexOfContaining(lines, "API:");
        var updatedIndex = IndexOfContaining(lines, "Updated:");

        Assert.True(totalPercentIndex < modelsPercentIndex);
        Assert.True(modelsPercentIndex < combinedSpendIndex);
        Assert.True(combinedSpendIndex < bonusIndex);
        Assert.True(bonusIndex < apiLineIndex);
        Assert.True(apiLineIndex < updatedIndex);
    }

    private static int IndexOfExact(IReadOnlyList<string> lines, string value)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i] == value)
                return i;
        }

        return -1;
    }

    private static int IndexOfContaining(IReadOnlyList<string> lines, string value)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(value, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static QuotaUsage CreateUsageWithBonus(
        decimal modelsActualUsd = 466.50m,
        decimal modelsBaseLimitUsd = 450m,
        decimal modelsBonusUsedUsd = 16.50m,
        decimal apiUsedUsd = 13.18m,
        decimal apiLimitUsd = 20m) =>
        new()
        {
            TotalUsedPercent = 98.55,
            FirstPartyUsedPercent = 100,
            ApiUsedPercent = 65.89,
            ModelsActualUsedUsd = modelsActualUsd,
            ModelsUsedUsd = modelsActualUsd,
            ModelsBaseLimitUsd = modelsBaseLimitUsd,
            ModelsEstimatedLimitUsd = modelsBaseLimitUsd,
            ModelsBonusUsedUsd = modelsBonusUsedUsd,
            AutoSpendCents = (long)Math.Round(modelsActualUsd * 100m),
            TotalSpendCents = (long)Math.Round((modelsActualUsd + apiUsedUsd) * 100m),
            ApiUsedAmountUsd = apiUsedUsd,
            ApiIncludedAmountUsd = apiLimitUsd
        };

    private sealed class TrayFakeLocalization : ILocalizationService
    {
        private readonly LanguageOption _language = new("en", "English");

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LanguageOption> SupportedLanguages => [_language];

        public LanguageOption SelectedLanguage
        {
            get => _language;
            set { }
        }

        public CultureInfo CurrentCulture => _language.Culture;

        public System.Windows.FlowDirection CurrentFlowDirection => _language.FlowDirection;

        public string this[string key] => GetString(key);

        public string GetString(string key) => key switch
        {
            "TrayMenuTotalFormat" => "Total: {0}",
            "TrayMenuModelsFormat" => "Cursor Models: {0}",
            "TrayMenuApiFormat" => "API: {0}",
            "TrayMenuApiWithSpendFormat" => "API: {0} of {1} — {2}",
            "TrayModelsBonusUsedFormat" => "Models bonus: {0} used",
            "TrayUpdatedFormat" => "Updated: {0}",
            _ => key
        };

        public string Format(string key, params object[] args) =>
            string.Format(CurrentCulture, GetString(key), args);
    }
}
