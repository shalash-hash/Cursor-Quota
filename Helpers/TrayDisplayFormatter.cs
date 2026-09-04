using Quota.Helpers;
using Quota.Localization;
using Quota.Models;

namespace Quota.Helpers;

public static class TrayDisplayFormatter
{
    public const int MaxTooltipLength = 127;

    public static TrayDisplayState Create(
        TrayDataState dataState,
        QuotaUsage? usage,
        DateTime? lastSuccessfulUpdate,
        int decimalPlaces,
        ILocalizationService localization)
    {
        if (dataState is TrayDataState.Loading or TrayDataState.NoData)
        {
            var statusText = dataState == TrayDataState.Loading
                ? localization.GetString("TrayLoading")
                : localization.GetString("TrayNoData");

            return new TrayDisplayState
            {
                DataState = dataState,
                TooltipText = TruncateTooltip(statusText),
                InfoMenuLines = [statusText]
            };
        }

        if (usage is null)
        {
            var noDataText = localization.GetString("TrayNoData");
            return new TrayDisplayState
            {
                DataState = TrayDataState.NoData,
                TooltipText = TruncateTooltip(noDataText),
                InfoMenuLines = [noDataText]
            };
        }

        var totalPercent = QuotaMonetaryHelper.ResolveCombinedUsedPercent(usage)
            ?? usage.TotalUsedPercent;
        var culture = localization.CurrentCulture;
        var digits = decimalPlaces;

        var totalPercentText = PercentageFormatter.Format(totalPercent, digits, culture);
        var modelsPercent = PercentageFormatter.Format(usage.FirstPartyUsedPercent, digits, culture);
        var apiPercent = PercentageFormatter.Format(usage.ApiUsedPercent, digits, culture);

        var tooltip = string.Format(
            culture,
            localization.GetString("TrayTooltipFormat"),
            localization.GetString("TrayShortTotal"),
            totalPercentText,
            localization.GetString("TrayShortModels"),
            modelsPercent,
            localization.GetString("TrayShortApi"),
            apiPercent);

        var menuLines = new List<string>
        {
            localization.Format("TrayMenuTotalFormat", totalPercentText),
            localization.Format("TrayMenuModelsFormat", modelsPercent),
            localization.Format("TrayMenuApiFormat", apiPercent)
        };

        if (QuotaSpendResolver.ResolveModelsActualUsedUsd(usage) is decimal modelsActual)
        {
            menuLines.Add(QuotaMonetaryHelper.FormatSpendRange(
                modelsActual,
                usage.ModelsBaseLimitUsd ?? usage.ModelsEstimatedLimitUsd,
                culture));
        }

        if (usage.ApiUsedAmountUsd is not null && usage.ApiIncludedAmountUsd is not null)
        {
            menuLines.Add(localization.Format(
                "ApiSpendFormat",
                QuotaMonetaryHelper.FormatUsd(usage.ApiUsedAmountUsd.Value, culture),
                QuotaMonetaryHelper.FormatUsd(usage.ApiIncludedAmountUsd.Value, culture)));
        }

        if (lastSuccessfulUpdate is not null)
        {
            var updatedAt = lastSuccessfulUpdate.Value.ToString("T", culture);
            menuLines.Add(localization.Format("TrayUpdatedFormat", updatedAt));
        }

        return new TrayDisplayState
        {
            DataState = TrayDataState.Ready,
            TooltipText = TruncateTooltip(tooltip),
            InfoMenuLines = menuLines
        };
    }

    public static string TruncateTooltip(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxTooltipLength)
            return text;

        return text[..(MaxTooltipLength - 1)] + "…";
    }
}
