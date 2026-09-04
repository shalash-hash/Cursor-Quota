using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

/// <summary>Обогащает QuotaUsage bonus-полями и фиксированным base Models limit.</summary>
public sealed class QuotaUsageEnricher
{
    private readonly QuotaSnapshotRepository _repository;

    public QuotaUsageEnricher(QuotaSnapshotRepository repository)
    {
        _repository = repository;
    }

    public Task<QuotaUsage> EnrichAsync(QuotaUsage raw) =>
        _repository.EnrichWithBonusBaselineAsync(raw);

    internal static QuotaUsage ApplyBonusBreakdown(
        QuotaUsage source,
        long? modelsBaseLimitCents,
        BonusSource bonusSource)
    {
        var modelsBaseLimitUsd = modelsBaseLimitCents is long cents && cents > 0
            ? QuotaMonetaryHelper.CentsToUsd(cents)
            : QuotaBonusHelper.EstimateLiveBaseLimitUsd(
                source.TotalSpendCents,
                source.FirstPartyUsedPercent);

        var modelsActualUsd = QuotaSpendResolver.ResolveModelsActualUsedUsd(source);
        var modelsBonusUsedUsd = QuotaBonusHelper.ResolveModelsBonusUsedUsd(
            modelsActualUsd,
            modelsBaseLimitUsd);

        var bonusAvailability = QuotaBonusHelper.ResolveBonusAvailability(
            source.RemainingBonus,
            modelsBonusUsedUsd);

        var modelsBaseRemainingUsd = modelsBaseLimitUsd is decimal baseLimit && modelsActualUsd is decimal actual
            ? Math.Max(0m, baseLimit - QuotaSpendResolver.ResolveModelsBaseUsedUsd(actual, baseLimit))
            : source.ModelsEstimatedRemainingUsd;

        var enriched = CopyUsage(source, usage =>
        {
            usage.ModelsBaseLimitUsd = modelsBaseLimitUsd;
            usage.ModelsBaseLimitCents = modelsBaseLimitCents;
            usage.ModelsEstimatedLimitUsd = modelsBaseLimitUsd ?? source.ModelsEstimatedLimitUsd;
            usage.ModelsActualUsedUsd = modelsActualUsd;
            usage.ModelsUsedUsd = modelsActualUsd;
            usage.ModelsBonusUsedUsd = modelsBonusUsedUsd > 0m ? modelsBonusUsedUsd : null;
            usage.ModelsEstimatedRemainingUsd = modelsBaseRemainingUsd;
            usage.BonusSource = bonusSource;
            usage.BonusAvailability = bonusAvailability;
        });

        var totalPercent = QuotaMonetaryHelper.ResolveCombinedUsedPercent(enriched)
            ?? enriched.TotalUsedPercent;

        return CopyUsage(enriched, usage => usage.TotalUsedPercent = totalPercent);
    }

    internal static QuotaUsage CopyUsage(QuotaUsage source, Action<MutableQuotaUsage> configure)
    {
        var mutable = MutableQuotaUsage.From(source);
        configure(mutable);
        return mutable.ToQuotaUsage();
    }

    internal sealed class MutableQuotaUsage
    {
        public double TotalUsedPercent { get; set; }
        public double FirstPartyUsedPercent { get; set; }
        public double ApiUsedPercent { get; set; }
        public double TodayTotalUsedPercent { get; set; }
        public double TodayFirstPartyUsedPercent { get; set; }
        public double TodayApiUsedPercent { get; set; }
        public double YesterdayTotalUsedPercent { get; set; }
        public double YesterdayFirstPartyUsedPercent { get; set; }
        public double YesterdayApiUsedPercent { get; set; }
        public bool HasYesterdayUsageData { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime RetrievedAt { get; set; }
        public string? PlanName { get; set; }
        public decimal? ApiIncludedAmountUsd { get; set; }
        public decimal? ApiUsedAmountUsd { get; set; }
        public decimal? ApiRemainingAmountUsd { get; set; }
        public long? TotalSpendCents { get; set; }
        public long? IncludedSpendCents { get; set; }
        public long? LimitCents { get; set; }
        public long? AutoSpendCents { get; set; }
        public long? ApiSpendCents { get; set; }
        public long? AutoLimitCents { get; set; }
        public long? ApiLimitCents { get; set; }
        public decimal? ModelsActualUsedUsd { get; set; }
        public decimal? ModelsUsedUsd { get; set; }
        public decimal? ModelsEstimatedLimitUsd { get; set; }
        public decimal? ModelsEstimatedRemainingUsd { get; set; }
        public long? TodayTotalSpendCents { get; set; }
        public long? TodayModelsSpendCents { get; set; }
        public long? TodayApiSpendCents { get; set; }
        public long? YesterdayTotalSpendCents { get; set; }
        public long? YesterdayModelsSpendCents { get; set; }
        public long? YesterdayApiSpendCents { get; set; }
        public bool HasYesterdaySpendData { get; set; }
        public long? BonusSpendCents { get; set; }
        public bool? RemainingBonus { get; set; }
        public string? BonusTooltip { get; set; }
        public BonusSource BonusSource { get; set; }
        public BonusAvailability BonusAvailability { get; set; }
        public decimal? ModelsBaseLimitUsd { get; set; }
        public decimal? ModelsBonusUsedUsd { get; set; }
        public BonusSource ApiBonusSource { get; set; }
        public decimal? ApiBonusUsedUsd { get; set; }
        public decimal? ApiKnownBonusAllowanceUsd { get; set; }
        public long? ModelsBaseLimitCents { get; set; }
        public double? RawTotalPercentUsed { get; set; }

        public static MutableQuotaUsage From(QuotaUsage source) =>
            new()
            {
                TotalUsedPercent = source.TotalUsedPercent,
                FirstPartyUsedPercent = source.FirstPartyUsedPercent,
                ApiUsedPercent = source.ApiUsedPercent,
                TodayTotalUsedPercent = source.TodayTotalUsedPercent,
                TodayFirstPartyUsedPercent = source.TodayFirstPartyUsedPercent,
                TodayApiUsedPercent = source.TodayApiUsedPercent,
                YesterdayTotalUsedPercent = source.YesterdayTotalUsedPercent,
                YesterdayFirstPartyUsedPercent = source.YesterdayFirstPartyUsedPercent,
                YesterdayApiUsedPercent = source.YesterdayApiUsedPercent,
                HasYesterdayUsageData = source.HasYesterdayUsageData,
                PeriodStart = source.PeriodStart,
                PeriodEnd = source.PeriodEnd,
                RetrievedAt = source.RetrievedAt,
                PlanName = source.PlanName,
                ApiIncludedAmountUsd = source.ApiIncludedAmountUsd,
                ApiUsedAmountUsd = source.ApiUsedAmountUsd,
                ApiRemainingAmountUsd = source.ApiRemainingAmountUsd,
                TotalSpendCents = source.TotalSpendCents,
                IncludedSpendCents = source.IncludedSpendCents,
                LimitCents = source.LimitCents,
                AutoSpendCents = source.AutoSpendCents,
                ApiSpendCents = source.ApiSpendCents,
                AutoLimitCents = source.AutoLimitCents,
                ApiLimitCents = source.ApiLimitCents,
                ModelsActualUsedUsd = source.ModelsActualUsedUsd,
                ModelsUsedUsd = source.ModelsUsedUsd,
                ModelsEstimatedLimitUsd = source.ModelsEstimatedLimitUsd,
                ModelsEstimatedRemainingUsd = source.ModelsEstimatedRemainingUsd,
                TodayTotalSpendCents = source.TodayTotalSpendCents,
                TodayModelsSpendCents = source.TodayModelsSpendCents,
                TodayApiSpendCents = source.TodayApiSpendCents,
                YesterdayTotalSpendCents = source.YesterdayTotalSpendCents,
                YesterdayModelsSpendCents = source.YesterdayModelsSpendCents,
                YesterdayApiSpendCents = source.YesterdayApiSpendCents,
                HasYesterdaySpendData = source.HasYesterdaySpendData,
                BonusSpendCents = source.BonusSpendCents,
                RemainingBonus = source.RemainingBonus,
                BonusTooltip = source.BonusTooltip,
                BonusSource = source.BonusSource,
                BonusAvailability = source.BonusAvailability,
                ModelsBaseLimitUsd = source.ModelsBaseLimitUsd,
                ModelsBonusUsedUsd = source.ModelsBonusUsedUsd,
                ApiBonusSource = source.ApiBonusSource,
                ApiBonusUsedUsd = source.ApiBonusUsedUsd,
                ApiKnownBonusAllowanceUsd = source.ApiKnownBonusAllowanceUsd,
                ModelsBaseLimitCents = source.ModelsBaseLimitCents,
                RawTotalPercentUsed = source.RawTotalPercentUsed
            };

        public QuotaUsage ToQuotaUsage() =>
            new()
            {
                TotalUsedPercent = TotalUsedPercent,
                FirstPartyUsedPercent = FirstPartyUsedPercent,
                ApiUsedPercent = ApiUsedPercent,
                TodayTotalUsedPercent = TodayTotalUsedPercent,
                TodayFirstPartyUsedPercent = TodayFirstPartyUsedPercent,
                TodayApiUsedPercent = TodayApiUsedPercent,
                YesterdayTotalUsedPercent = YesterdayTotalUsedPercent,
                YesterdayFirstPartyUsedPercent = YesterdayFirstPartyUsedPercent,
                YesterdayApiUsedPercent = YesterdayApiUsedPercent,
                HasYesterdayUsageData = HasYesterdayUsageData,
                PeriodStart = PeriodStart,
                PeriodEnd = PeriodEnd,
                RetrievedAt = RetrievedAt,
                PlanName = PlanName,
                ApiIncludedAmountUsd = ApiIncludedAmountUsd,
                ApiUsedAmountUsd = ApiUsedAmountUsd,
                ApiRemainingAmountUsd = ApiRemainingAmountUsd,
                TotalSpendCents = TotalSpendCents,
                IncludedSpendCents = IncludedSpendCents,
                LimitCents = LimitCents,
                AutoSpendCents = AutoSpendCents,
                ApiSpendCents = ApiSpendCents,
                AutoLimitCents = AutoLimitCents,
                ApiLimitCents = ApiLimitCents,
                ModelsActualUsedUsd = ModelsActualUsedUsd,
                ModelsUsedUsd = ModelsUsedUsd,
                ModelsEstimatedLimitUsd = ModelsEstimatedLimitUsd,
                ModelsEstimatedRemainingUsd = ModelsEstimatedRemainingUsd,
                TodayTotalSpendCents = TodayTotalSpendCents,
                TodayModelsSpendCents = TodayModelsSpendCents,
                TodayApiSpendCents = TodayApiSpendCents,
                YesterdayTotalSpendCents = YesterdayTotalSpendCents,
                YesterdayModelsSpendCents = YesterdayModelsSpendCents,
                YesterdayApiSpendCents = YesterdayApiSpendCents,
                HasYesterdaySpendData = HasYesterdaySpendData,
                BonusSpendCents = BonusSpendCents,
                RemainingBonus = RemainingBonus,
                BonusTooltip = BonusTooltip,
                BonusSource = BonusSource,
                BonusAvailability = BonusAvailability,
                ModelsBaseLimitUsd = ModelsBaseLimitUsd,
                ModelsBonusUsedUsd = ModelsBonusUsedUsd,
                ApiBonusSource = ApiBonusSource,
                ApiBonusUsedUsd = ApiBonusUsedUsd,
                ApiKnownBonusAllowanceUsd = ApiKnownBonusAllowanceUsd,
                ModelsBaseLimitCents = ModelsBaseLimitCents,
                RawTotalPercentUsed = RawTotalPercentUsed
            };
    }
}
