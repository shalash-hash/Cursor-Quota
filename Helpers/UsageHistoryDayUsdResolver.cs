using Quota.Models;

namespace Quota.Helpers;

/// <summary>
/// Resolves per-day USD for history chart/tooltip from spend cents when available,
/// otherwise from pool percents and known limits (legacy percent-only snapshots).
/// </summary>
public static class UsageHistoryDayUsdResolver
{
  internal const decimal LegacyModelsPoolLimitUsd = 450m;

  internal const decimal DefaultApiPoolLimitUsd = 20m;

  public static (decimal? ModelsUsd, decimal? ApiUsd, decimal? TotalUsd) Resolve(
      PoolDaySpent dailySpent,
      DaySpendUsd daySpendUsd,
      QuotaSnapshot anchorSnapshot)
  {
    var modelsLimit = ResolveHistoryModelsLimitUsd(anchorSnapshot);
    var apiLimit = ResolveHistoryApiLimitUsd(anchorSnapshot);

    decimal? modelsUsd = daySpendUsd.ModelsUsd > 0m ? daySpendUsd.ModelsUsd : null;
    decimal? apiUsd = daySpendUsd.ApiUsd > 0m ? daySpendUsd.ApiUsd : null;
    decimal? totalUsd = daySpendUsd.CombinedUsd > 0m ? daySpendUsd.CombinedUsd : null;

    if (modelsUsd is null && dailySpent.FirstParty > 0.001 && modelsLimit is decimal modelsLimitUsd)
      modelsUsd = QuotaMonetaryHelper.PercentToUsd(dailySpent.FirstParty, modelsLimitUsd);

    if (apiUsd is null && dailySpent.Api > 0.001)
      apiUsd = QuotaMonetaryHelper.PercentToUsd(dailySpent.Api, apiLimit);

    if (totalUsd is null || totalUsd <= 0m)
    {
      if (modelsUsd is not null || apiUsd is not null)
        totalUsd = (modelsUsd ?? 0m) + (apiUsd ?? 0m);
      else if (dailySpent.Total > 0.001)
      {
        totalUsd = QuotaMonetaryHelper.ResolveCombinedDailyTargetUsd(
            dailySpent.FirstParty,
            dailySpent.Api,
            modelsLimit,
            apiLimit);
      }
    }

    return (
        PositiveOrNull(modelsUsd),
        PositiveOrNull(apiUsd),
        PositiveOrNull(totalUsd));
  }

  public static decimal? ResolveHistoryModelsLimitUsd(QuotaSnapshot snapshot)
  {
    if (snapshot.ModelsBaseLimitCents is long baseCents)
      return QuotaMonetaryHelper.CentsToUsd(baseCents);

    if (snapshot.TotalSpendCents is long spendCents && snapshot.FirstPartyPercent > 0.001)
      return QuotaMonetaryHelper.EstimateLimitUsd(spendCents, snapshot.FirstPartyPercent);

    if (snapshot.FirstPartyPercent > 0.001 || snapshot.TotalPercent > 0.001)
      return LegacyModelsPoolLimitUsd;

    return null;
  }

  public static decimal ResolveHistoryApiLimitUsd(QuotaSnapshot snapshot) =>
      snapshot.LimitCents is long limitCents
          ? QuotaMonetaryHelper.CentsToUsd(limitCents)
          : DefaultApiPoolLimitUsd;

  private static decimal? PositiveOrNull(decimal? value) =>
      value is > 0m ? value : null;
}
