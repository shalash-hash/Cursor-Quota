using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Quota.Helpers;
using Quota.Localization;
using Quota.Models;
using Quota.Services;

namespace Quota.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IQuotaUsageProvider _quotaUsageProvider;
    private readonly QuotaCalculator _quotaCalculator;
    private readonly StartupService _startupService;
    private readonly QuotaSnapshotRepository _snapshotRepository;
    private readonly UsageHistoryService _usageHistoryService;
    private readonly QuotaDiagnosticLogger _logger;
    private readonly QuotaRefreshScheduler _refreshScheduler;
    private readonly UiSettingsService _uiSettingsService;
    private readonly ThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DispatcherTimer? _resetCountdownTimer;

    private readonly IReadOnlyList<int> _precisionOptions = new[] { 2 };

    private const int DisplayDigits = QuotaMonetaryHelper.DisplayDecimalPlaces;
    private readonly UiSettings _uiSettings;

    private QuotaUsage? _lastSuccessfulUsage;
    private DateTime? _lastSuccessfulUpdate;
    private bool _hasCompletedFirstRefreshAttempt;

    private string _totalUsedPercentText = "—";
    private double _totalProgressValue;
    private string _totalProgressLabel = "— / 100";
    private string _totalRemainingText = "—";
    private string _totalSpendText = "—";
    private string _totalRemainingAmountText = string.Empty;
    private string _daysUntilResetText = "—";
    private string _totalDailyTargetText = "—";

    private string _totalTodaySpentText = "—";
    private string _totalYesterdaySpentText = "—";
    private string _dailyTodaySpentText = "—";
    private string _dailyTodayBreakdownText = string.Empty;
    private string _dailyYesterdaySpentText = "—";

    private string _todaySpentText = "—";
    private string _todayStatusText = string.Empty;
    private string _todayOverageText = string.Empty;
    private bool _isDailyTargetExceeded;
    private double _dailyProgressFillPercent;
    private double _dailyPrimaryFillPercent;
    private double _dailySecondaryFillPercent;
    private double _dailyNormSegmentWeight = 1;
    private double _dailyAheadSegmentWeight;
    private string _dailyProgressNormLabel = string.Empty;
    private string _dailyProgressAheadLabel = string.Empty;

    private string _firstPartyUsedPercentText = "—";
    private string _firstPartySpendText = string.Empty;
    private string _firstPartyRemainingText = string.Empty;
    private string _firstPartyBonusText = string.Empty;
    private string _firstPartyBonusStatusText = string.Empty;
    private double _firstPartyProgressValue;
    private string _firstPartyPaceText = "—";
    private string _firstPartyTodaySpentText = "—";
    private string _firstPartyYesterdaySpentText = "—";

    private string _apiUsedPercentText = "—";
    private double _apiProgressValue;
    private string _apiSpendText = string.Empty;
    private string _apiPaceText = "—";
    private string _apiTodaySpentText = "—";
    private string _apiYesterdaySpentText = "—";

    private string _paceStatusText = string.Empty;
    private string _todayPoolsDetailText = string.Empty;
    private string _totalPoolsDetailText = string.Empty;
    private string _totalBonusDetailText = string.Empty;

    private string? _errorMessage;
    private string? _errorMessageKey;
    private object[] _errorMessageArgs = [];
    private string _lastUpdateText = string.Empty;
    private string _lastUpdateTimeText = string.Empty;
    private string _refreshButtonText = string.Empty;
    private bool _isRefreshing;
    private bool _isStartupEnabled;
    private bool _isDarkMode;
    private bool _isStatisticsView;
    private UsageHistoryRange _selectedHistoryRange = UsageHistoryRange.Week;
    private IReadOnlyList<UsageHistoryPoint> _usageHistoryPoints = [];
    private IReadOnlyList<UsageHistoryRangeOption> _historyRangeOptions = [];
    private string _statisticsSummaryText = string.Empty;
    private bool _hasStatisticsData;

    public MainViewModel(
        IQuotaUsageProvider quotaUsageProvider,
        QuotaCalculator quotaCalculator,
        StartupService startupService,
        QuotaSnapshotRepository snapshotRepository,
        UsageHistoryService usageHistoryService,
        QuotaDiagnosticLogger logger,
        UiSettingsService uiSettingsService,
        ThemeService themeService,
        ILocalizationService localizationService)
    {
        _quotaUsageProvider = quotaUsageProvider;
        _quotaCalculator = quotaCalculator;
        _startupService = startupService;
        _snapshotRepository = snapshotRepository;
        _usageHistoryService = usageHistoryService;
        _logger = logger;
        _uiSettingsService = uiSettingsService;
        _themeService = themeService;
        _localizationService = localizationService;
        _uiSettings = _uiSettingsService.Load();
        _isDarkMode = _themeService.IsDarkMode;
        _localizationService.PropertyChanged += OnLocalizationChanged;

        RefreshCommand = new RelayCommand(
            () => RefreshAsync(RefreshSource.Manual),
            () => !IsRefreshing);

        _isStartupEnabled = _startupService.IsEnabled();
        _refreshScheduler = new QuotaRefreshScheduler(RefreshAsync, TimeSpan.FromMinutes(1));
        RebuildHistoryRangeOptions();
        ResetDisplayTexts();
        NotifyTrayDisplayChanged();
    }

    public event EventHandler? TrayDisplayChanged;

    public ICommand RefreshCommand { get; }

    public IReadOnlyList<int> PrecisionOptions => _precisionOptions;

    public IReadOnlyList<LanguageOption> SupportedLanguages => _localizationService.SupportedLanguages;

    public LanguageOption SelectedLanguage
    {
        get => _localizationService.SelectedLanguage;
        set
        {
            if (value is null || ReferenceEquals(_localizationService.SelectedLanguage, value))
                return;

            _localizationService.SelectedLanguage = value;
        }
    }

    public int SelectedDecimalPlaces
    {
        get => DisplayDigits;
        set
        {
            // Фиксированная точность: 2 знака после запятой для процентов.
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (!SetProperty(ref _isDarkMode, value))
                return;

            _themeService.SetDarkMode(value);
        }
    }

    public bool IsStatisticsView
    {
        get => _isStatisticsView;
        set
        {
            if (!SetProperty(ref _isStatisticsView, value))
                return;

            OnPropertyChanged(nameof(ViewToggleTooltip));
            if (value)
                _ = LoadStatisticsAsync();
        }
    }

    public string ViewToggleTooltip =>
        _localizationService[IsStatisticsView ? "ShowDashboardTooltip" : "ShowStatisticsTooltip"];

    public UsageHistoryRange SelectedHistoryRange
    {
        get => _selectedHistoryRange;
        set
        {
            if (!SetProperty(ref _selectedHistoryRange, value))
                return;

            if (IsStatisticsView)
                _ = LoadStatisticsAsync();
        }
    }

    public IReadOnlyList<UsageHistoryRangeOption> HistoryRangeOptions => _historyRangeOptions;

    public IReadOnlyList<UsageHistoryPoint> UsageHistoryPoints
    {
        get => _usageHistoryPoints;
        private set => SetProperty(ref _usageHistoryPoints, value);
    }

    public string StatisticsSummaryText
    {
        get => _statisticsSummaryText;
        private set => SetProperty(ref _statisticsSummaryText, value);
    }

    public bool HasStatisticsData
    {
        get => _hasStatisticsData;
        private set => SetProperty(ref _hasStatisticsData, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string RefreshButtonText
    {
        get => _refreshButtonText;
        private set => SetProperty(ref _refreshButtonText, value);
    }

    public string TotalUsedPercentText
    {
        get => _totalUsedPercentText;
        private set => SetProperty(ref _totalUsedPercentText, value);
    }

    public double TotalProgressValue
    {
        get => _totalProgressValue;
        private set => SetProperty(ref _totalProgressValue, value);
    }

    public string TotalProgressLabel
    {
        get => _totalProgressLabel;
        private set => SetProperty(ref _totalProgressLabel, value);
    }

    public string TotalRemainingText
    {
        get => _totalRemainingText;
        private set => SetProperty(ref _totalRemainingText, value);
    }

    public string TotalSpendText
    {
        get => _totalSpendText;
        private set => SetProperty(ref _totalSpendText, value);
    }

    public string TotalRemainingAmountText
    {
        get => _totalRemainingAmountText;
        private set => SetProperty(ref _totalRemainingAmountText, value);
    }

    public string EstimatedLimitHintText =>
        _localizationService["EstimatedLimitHint"];

    public string DaysUntilResetText
    {
        get => _daysUntilResetText;
        private set => SetProperty(ref _daysUntilResetText, value);
    }

    public string TotalDailyTargetText
    {
        get => _totalDailyTargetText;
        private set => SetProperty(ref _totalDailyTargetText, value);
    }

    public string TotalTodaySpentText
    {
        get => _totalTodaySpentText;
        private set => SetProperty(ref _totalTodaySpentText, value);
    }

    public string TotalYesterdaySpentText
    {
        get => _totalYesterdaySpentText;
        private set => SetProperty(ref _totalYesterdaySpentText, value);
    }

    public string DailyTodaySpentText
    {
        get => _dailyTodaySpentText;
        private set => SetProperty(ref _dailyTodaySpentText, value);
    }

    public string DailyTodayBreakdownText
    {
        get => _dailyTodayBreakdownText;
        private set => SetProperty(ref _dailyTodayBreakdownText, value);
    }

    public string DailyYesterdaySpentText
    {
        get => _dailyYesterdaySpentText;
        private set => SetProperty(ref _dailyYesterdaySpentText, value);
    }

    public string TodaySpentText
    {
        get => _todaySpentText;
        private set => SetProperty(ref _todaySpentText, value);
    }

    public string TodayStatusText
    {
        get => _todayStatusText;
        private set => SetProperty(ref _todayStatusText, value);
    }

    public string TodayOverageText
    {
        get => _todayOverageText;
        private set => SetProperty(ref _todayOverageText, value);
    }

    public bool IsDailyTargetExceeded
    {
        get => _isDailyTargetExceeded;
        private set => SetProperty(ref _isDailyTargetExceeded, value);
    }

    public double DailyProgressFillPercent
    {
        get => _dailyProgressFillPercent;
        private set => SetProperty(ref _dailyProgressFillPercent, value);
    }

    public double DailyPrimaryFillPercent
    {
        get => _dailyPrimaryFillPercent;
        private set => SetProperty(ref _dailyPrimaryFillPercent, value);
    }

    public double DailySecondaryFillPercent
    {
        get => _dailySecondaryFillPercent;
        private set => SetProperty(ref _dailySecondaryFillPercent, value);
    }

    public double DailyNormSegmentWeight
    {
        get => _dailyNormSegmentWeight;
        private set => SetProperty(ref _dailyNormSegmentWeight, value);
    }

    public double DailyAheadSegmentWeight
    {
        get => _dailyAheadSegmentWeight;
        private set => SetProperty(ref _dailyAheadSegmentWeight, value);
    }

    public string DailyProgressNormLabel
    {
        get => _dailyProgressNormLabel;
        private set => SetProperty(ref _dailyProgressNormLabel, value);
    }

    public string DailyProgressAheadLabel
    {
        get => _dailyProgressAheadLabel;
        private set => SetProperty(ref _dailyProgressAheadLabel, value);
    }

    public string FirstPartyUsedPercentText
    {
        get => _firstPartyUsedPercentText;
        private set => SetProperty(ref _firstPartyUsedPercentText, value);
    }

    public string FirstPartySpendText
    {
        get => _firstPartySpendText;
        private set => SetProperty(ref _firstPartySpendText, value);
    }

    public string FirstPartyRemainingText
    {
        get => _firstPartyRemainingText;
        private set => SetProperty(ref _firstPartyRemainingText, value);
    }

    public string FirstPartyBonusText
    {
        get => _firstPartyBonusText;
        private set => SetProperty(ref _firstPartyBonusText, value);
    }

    public string FirstPartyBonusStatusText
    {
        get => _firstPartyBonusStatusText;
        private set => SetProperty(ref _firstPartyBonusStatusText, value);
    }

    public double FirstPartyProgressValue
    {
        get => _firstPartyProgressValue;
        private set => SetProperty(ref _firstPartyProgressValue, value);
    }

    public string FirstPartyPaceText
    {
        get => _firstPartyPaceText;
        private set => SetProperty(ref _firstPartyPaceText, value);
    }

    public string FirstPartyTodaySpentText
    {
        get => _firstPartyTodaySpentText;
        private set => SetProperty(ref _firstPartyTodaySpentText, value);
    }

    public string FirstPartyYesterdaySpentText
    {
        get => _firstPartyYesterdaySpentText;
        private set => SetProperty(ref _firstPartyYesterdaySpentText, value);
    }

    public string ApiUsedPercentText
    {
        get => _apiUsedPercentText;
        private set => SetProperty(ref _apiUsedPercentText, value);
    }

    public double ApiProgressValue
    {
        get => _apiProgressValue;
        private set => SetProperty(ref _apiProgressValue, value);
    }

    public string ApiSpendText
    {
        get => _apiSpendText;
        private set => SetProperty(ref _apiSpendText, value);
    }

    public string ApiPaceText
    {
        get => _apiPaceText;
        private set => SetProperty(ref _apiPaceText, value);
    }

    public string ApiTodaySpentText
    {
        get => _apiTodaySpentText;
        private set => SetProperty(ref _apiTodaySpentText, value);
    }

    public string ApiYesterdaySpentText
    {
        get => _apiYesterdaySpentText;
        private set => SetProperty(ref _apiYesterdaySpentText, value);
    }

    public string PaceStatusText
    {
        get => _paceStatusText;
        private set => SetProperty(ref _paceStatusText, value);
    }

    public string TodayPoolsDetailText
    {
        get => _todayPoolsDetailText;
        private set => SetProperty(ref _todayPoolsDetailText, value);
    }

    public string TotalPoolsDetailText
    {
        get => _totalPoolsDetailText;
        private set => SetProperty(ref _totalPoolsDetailText, value);
    }

    public string TotalBonusDetailText
    {
        get => _totalBonusDetailText;
        private set => SetProperty(ref _totalBonusDetailText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string LastUpdateText
    {
        get => _lastUpdateText;
        private set => SetProperty(ref _lastUpdateText, value);
    }

    public string LastUpdateTimeText
    {
        get => _lastUpdateTimeText;
        private set => SetProperty(ref _lastUpdateTimeText, value);
    }

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set
        {
            if (!SetProperty(ref _isStartupEnabled, value))
                return;

            try
            {
                if (value)
                    _startupService.Enable();
                else
                    _startupService.Disable();
            }
            catch (Exception ex)
            {
                _isStartupEnabled = _startupService.IsEnabled();
                OnPropertyChanged(nameof(IsStartupEnabled));
                _ = ex;
                SetError("AutostartChangeFailed");
            }
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync(RefreshSource.Startup);
    }

    public async Task RefreshAsync(RefreshSource source)
    {
        if (!await _refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            if (source == RefreshSource.Manual)
                RunOnUi(() => SetRefreshingState(true));

            return;
        }

        RunOnUi(() => SetRefreshingState(true));
        _logger.LogRefreshStart(source);

        try
        {
            var snapshot = await _quotaUsageProvider.GetUsageAsync().ConfigureAwait(false);
            var withBonus = await _snapshotRepository.EnrichWithBonusBaselineAsync(snapshot).ConfigureAwait(false);
            var usage = await _snapshotRepository.EnrichWithTodayUsageAsync(withBonus).ConfigureAwait(false);
            var successTime = DateTimeOffset.Now;

            RunOnUi(() =>
            {
                _lastSuccessfulUsage = usage;
                _lastSuccessfulUpdate = successTime.LocalDateTime;
                ApplyUsage(usage);
                ErrorMessage = null;
                UpdateLastUpdateText();
            });

            _logger.LogRefreshSuccess(
                source,
                usage.TotalUsedPercent,
                usage.FirstPartyUsedPercent,
                usage.ApiUsedPercent);

            if (IsStatisticsView)
                await LoadStatisticsAsync().ConfigureAwait(false);
        }
        catch (CursorAuthException ex)
        {
            RunOnUi(() => HandleRefreshError("CursorAuthFailed"));
            _logger.LogRefreshFailed(source, ex.Message);
        }
        catch (Exception ex)
        {
            RunOnUi(() => HandleRefreshError("UpdateFailedGeneric"));
            _logger.LogRefreshFailed(source, ex.GetType().Name);
        }
        finally
        {
            _hasCompletedFirstRefreshAttempt = true;
            _refreshLock.Release();
            RunOnUi(() =>
            {
                SetRefreshingState(false);
                NotifyTrayDisplayChanged();
            });
        }
    }

    public TrayDisplayState GetTrayDisplayState()
    {
        var dataState = ResolveTrayDataState();
        return TrayDisplayFormatter.Create(
            dataState,
            _lastSuccessfulUsage,
            _lastSuccessfulUpdate,
            DisplayDigits,
            _localizationService);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        StopResetCountdown();
        _refreshScheduler.Dispose();
        _refreshLock.Dispose();
    }

    private void SetRefreshingState(bool isRefreshing)
    {
        IsRefreshing = isRefreshing;
        RefreshButtonText = _localizationService[isRefreshing ? "Refreshing" : "Refresh"];
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
    }

    private void HandleRefreshError(string key, params object[] args)
    {
        SetError(key, args);

        if (_lastSuccessfulUsage is not null)
            ApplyUsage(_lastSuccessfulUsage);
    }

    private void UpdateLastUpdateText()
    {
        if (_lastSuccessfulUpdate is null)
        {
            LastUpdateText = string.Empty;
            LastUpdateTimeText = string.Empty;
            return;
        }

        var formatted = _lastSuccessfulUpdate.Value.ToString("T", _localizationService.CurrentCulture);
        LastUpdateTimeText = formatted;
        LastUpdateText = _localizationService.Format("LastUpdatedFormat", formatted);
    }

    private void ApplyUsage(QuotaUsage usage)
    {
        var calculation = _quotaCalculator.Calculate(usage);
        var digits = DisplayDigits;
        var culture = _localizationService.CurrentCulture;
        var combined = QuotaMonetaryHelper.ResolveCombinedDisplay(usage);

        TotalUsedPercentText = PercentageFormatter.Format(combined.UsedPercent, digits, culture);
        TotalProgressValue = Math.Max(0, combined.UsedPercent);
        TotalPoolsDetailText = BuildTotalPoolsDetailText(usage, digits, culture);
        TotalProgressLabel = BuildProgressLabel(
            combined.UsedPercent,
            combined.UsedUsd,
            combined.LimitUsd,
            culture,
            digits);

        if (combined.UsedUsd is not null)
        {
            TotalSpendText = QuotaMonetaryHelper.FormatSpendRange(
                combined.UsedUsd.Value,
                combined.LimitUsd,
                culture);
        }
        else
        {
            TotalSpendText = "—";
        }

        TotalBonusDetailText = BuildTotalBonusDetailText(usage, combined, culture);

        TotalRemainingText = _localizationService.Format(
            "CombinedBaseRemainingFormat",
            FormatPercentWithUsd(
                combined.RemainingPercent,
                combined.LimitUsd,
                culture));
        TotalRemainingAmountText = string.Empty;
        UpdateResetCountdownText();
        StartResetCountdown();
        TotalDailyTargetText = _localizationService.Format(
            "PerDayFormat",
            FormatPercentWithUsd(
                calculation.Total.DailyTarget,
                combined.LimitUsd,
                culture));

        ApplyDailySpentTexts(usage, culture);

        TodaySpentText = DailyTodaySpentText;

        var dailyPlanUsd = QuotaMonetaryHelper.ResolveDailyPlanUsd(
            calculation.FirstParty.DailyTarget,
            calculation.Api.DailyTarget,
            usage.ModelsEstimatedLimitUsd,
            usage.ApiIncludedAmountUsd);
        var todayUsageUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

        DailyTargetProgressState dailyProgress;
        DailyPlanDelta planDelta;
        decimal? planDeltaUsd;

        if (todayUsageUsd is not null)
        {
            dailyProgress = DailyTargetProgressCalculator.CalculateFromUsd(
                todayUsageUsd.Value,
                dailyPlanUsd);
            planDelta = DailyTargetProgressCalculator.CalculatePlanDeltaFromUsd(
                todayUsageUsd.Value,
                dailyPlanUsd);
            planDeltaUsd = DailyTargetProgressCalculator.CalculateDeltaUsdFromValues(
                todayUsageUsd.Value,
                dailyPlanUsd);
        }
        else
        {
            dailyProgress = DailyTargetProgressCalculator.Calculate(
                usage.TodayTotalUsedPercent,
                calculation.Total.DailyTarget);
            planDelta = DailyTargetProgressCalculator.CalculatePlanDelta(
                usage.TodayTotalUsedPercent,
                calculation.Total.DailyTarget);
            planDeltaUsd = DailyTargetProgressCalculator.CalculateDeltaUsd(
                usage.TodayTotalUsedPercent,
                calculation.Total.DailyTarget,
                combined.LimitUsd);
        }

        IsDailyTargetExceeded = dailyProgress.IsExceeded;
        DailyProgressFillPercent = dailyProgress.FillPercent;
        DailyNormSegmentWeight = dailyProgress.NormSegmentWeight;
        DailyAheadSegmentWeight = dailyProgress.AheadSegmentWeight;
        DailyPrimaryFillPercent = dailyProgress.IsExceeded
            ? dailyProgress.NormSegmentWeight * 100
            : dailyProgress.FillPercent;
        DailySecondaryFillPercent = dailyProgress.IsExceeded
            ? dailyProgress.AheadSegmentWeight * 100
            : 0;

        var planDeltaText = DailyTargetProgressCalculator.FormatRelativeDeltaWithUsd(
            planDelta.RelativeDeltaPercent,
            planDeltaUsd,
            digits,
            culture);

        if (dailyProgress.IsExceeded)
        {
            DailyProgressNormLabel = PercentageFormatter.Format(100, digits, culture);
            DailyProgressAheadLabel = planDelta.Kind == DailyPlanDeltaKind.Ahead
                ? _localizationService.Format("AheadOfDailyPlanFormat", planDeltaText)
                : string.Empty;
        }
        else
        {
            DailyProgressNormLabel = PercentageFormatter.Format(dailyProgress.PlanCompletionPercent, digits, culture);
            DailyProgressAheadLabel = string.Empty;
        }

        TodayOverageText = string.Empty;
        TodayStatusText = planDelta.Kind switch
        {
            DailyPlanDeltaKind.OnPlan => _localizationService["TodayPlanCompletedExact"],
            DailyPlanDeltaKind.Ahead => _localizationService["TodayPlanCompleted"],
            DailyPlanDeltaKind.Behind => _localizationService.Format("BehindDailyPlanFormat", planDeltaText),
            _ => string.Empty
        };

        FirstPartyUsedPercentText = PercentageFormatter.Format(
            Math.Min(100, usage.FirstPartyUsedPercent),
            digits,
            culture);
        FirstPartyProgressValue = Math.Min(100, Math.Max(0, usage.FirstPartyUsedPercent));

        var modelsBreakdown = QuotaBonusHelper.ResolveModelsBreakdown(usage);
        if (modelsBreakdown.BaseLimitUsd is decimal modelsBaseLimit)
        {
            FirstPartySpendText = QuotaMonetaryHelper.FormatSpendRange(
                modelsBreakdown.BaseUsedUsd,
                modelsBaseLimit,
                culture);
        }
        else if (QuotaSpendResolver.ResolveModelsActualUsedUsd(usage) is decimal modelsActual)
        {
            FirstPartySpendText = QuotaMonetaryHelper.FormatSpendRange(
                modelsActual,
                usage.ModelsEstimatedLimitUsd,
                culture);
        }
        else
        {
            FirstPartySpendText = string.Empty;
        }

        if (modelsBreakdown.BonusUsedUsd > 0m)
        {
            FirstPartyBonusText = _localizationService.Format(
                "ModelsBonusUsedFormat",
                QuotaMonetaryHelper.FormatUsd(modelsBreakdown.BonusUsedUsd, culture));
        }
        else
        {
            FirstPartyBonusText = string.Empty;
        }

        FirstPartyBonusStatusText = modelsBreakdown.BonusAvailability switch
        {
            BonusAvailability.Available => _localizationService["ModelsBonusAvailable"],
            _ => string.Empty
        };

        var modelsRemainingUsd = QuotaMonetaryHelper.ResolveModelsRemainingUsd(usage);
        if (modelsRemainingUsd is not null)
        {
            FirstPartyRemainingText = _localizationService.Format(
                "TotalRemainingFormat",
                QuotaMonetaryHelper.FormatUsd(modelsRemainingUsd.Value, culture));
        }
        else
        {
            FirstPartyRemainingText = string.Empty;
        }

        FirstPartyPaceText = _localizationService.Format(
            "PaceFormat",
            FormatPercentWithUsd(
                calculation.FirstParty.DailyTarget,
                usage.ModelsEstimatedLimitUsd,
                culture));

        ApiUsedPercentText = PercentageFormatter.Format(usage.ApiUsedPercent, digits, culture);
        ApiProgressValue = Math.Max(0, usage.ApiUsedPercent);
        ApiPaceText = _localizationService.Format(
            "PaceFormat",
            FormatPercentWithUsd(
                calculation.Api.DailyTarget,
                usage.ApiIncludedAmountUsd,
                culture));

        if (usage.ApiIncludedAmountUsd is not null && usage.ApiUsedAmountUsd is not null)
        {
            ApiSpendText = _localizationService.Format(
                "ApiSpendFormat",
                PercentageFormatter.FormatUsd(usage.ApiUsedAmountUsd.Value, culture),
                PercentageFormatter.FormatUsd(usage.ApiIncludedAmountUsd.Value, culture));
        }
        else
        {
            ApiSpendText = string.Empty;
        }

        PaceStatusText = calculation.Total.PaceStatus switch
        {
            PaceStatus.BelowPlan => _localizationService["PaceBelowPlan"],
            PaceStatus.OnPlan => _localizationService["PaceOnPlan"],
            PaceStatus.AbovePlan => _localizationService["PaceAbovePlan"],
            _ => string.Empty
        };

        TodayPoolsDetailText = BuildTodayPoolsDetailText(usage, digits, culture);

        NotifyTrayDisplayChanged();
    }

    private string BuildProgressLabel(
        double usedPercent,
        decimal? usedUsd,
        decimal? limitUsd,
        CultureInfo culture,
        int digits)
    {
        var percentPart = $"{FormatProgressNumber(usedPercent, digits)} / 100";
        if (usedUsd is null)
            return percentPart;

        var amountPart = QuotaMonetaryHelper.FormatSpendRange(usedUsd.Value, limitUsd, culture);
        return $"{percentPart} · {amountPart}";
    }

    private string BuildTotalBonusDetailText(
        QuotaUsage usage,
        CombinedQuotaDisplay combined,
        CultureInfo culture)
    {
        if (combined.ModelsBonusUsedUsd is not decimal bonusUsed || bonusUsed <= 0m)
            return string.Empty;

        if (usage.BonusSource != BonusSource.Models)
            return string.Empty;

        return _localizationService.Format(
            "CombinedModelsBonusUsedFormat",
            QuotaMonetaryHelper.FormatUsd(bonusUsed, culture));
    }

    private string BuildTotalPoolsDetailText(QuotaUsage usage, int digits, CultureInfo culture)
    {
        var modelsPercent = PercentageFormatter.Format(usage.FirstPartyUsedPercent, digits, culture);
        var apiPercent = PercentageFormatter.Format(usage.ApiUsedPercent, digits, culture);

        return _localizationService.Format(
            "TotalPoolsDetailFormat",
            modelsPercent,
            apiPercent);
    }

    private string BuildTodayPoolsDetailText(QuotaUsage usage, int digits, CultureInfo culture)
    {
        var modelsPercent = PercentageFormatter.Format(usage.TodayFirstPartyUsedPercent, digits, culture);
        var apiPercent = PercentageFormatter.Format(usage.TodayApiUsedPercent, digits, culture);

        var modelsAmount = FormatOptionalSpendAmount(usage.TodayModelsSpendCents, culture);
        var apiAmount = FormatApiDaySpendAmount(usage.TodayApiUsedPercent, usage.ApiIncludedAmountUsd, culture);

        if (modelsAmount is not null || apiAmount is not null)
        {
            return _localizationService.Format(
                "TodayPoolsDetailWithAmountFormat",
                modelsPercent,
                modelsAmount ?? "—",
                apiPercent,
                apiAmount ?? "—");
        }

        return _localizationService.Format(
            "TodayPoolsDetailFormat",
            modelsPercent,
            apiPercent);
    }

    private static string? FormatOptionalSpendAmount(long? spendCents, CultureInfo culture) =>
        spendCents is long cents
            ? QuotaMonetaryHelper.FormatUsd(QuotaMonetaryHelper.CentsToUsd(cents), culture)
            : null;

    private static string? FormatApiDaySpendAmount(
        double percent,
        decimal? includedUsd,
        CultureInfo culture)
    {
        if (includedUsd is null || percent <= 0)
            return percent <= 0 ? QuotaMonetaryHelper.FormatUsd(0m, culture) : null;

        return QuotaMonetaryHelper.FormatUsd(
            QuotaMonetaryHelper.PercentToUsd(percent, includedUsd.Value),
            culture);
    }

    private void ApplyDailySpentTexts(QuotaUsage usage, CultureInfo culture)
    {
        var combinedTodayUsd = QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);
        var combinedTodayPercent = QuotaMonetaryHelper.ResolveCombinedTodayPercentOrFallback(usage);

        TotalTodaySpentText = FormatCombinedCardDailySpent(
            combinedTodayUsd,
            isYesterday: false,
            culture);
        TotalYesterdaySpentText = FormatCombinedCardDailySpent(
            ComputeYesterdayTotalSpendUsd(usage),
            isYesterday: true,
            culture,
            hasData: usage.HasYesterdayUsageData);

        DailyTodaySpentText = FormatDailySpentToday(combinedTodayPercent, combinedTodayUsd);

        DailyTodayBreakdownText = BuildDailyTodayBreakdownText(usage, culture);
        DailyYesterdaySpentText = FormatDailySpentYesterday(
            usage.YesterdayTotalUsedPercent,
            ComputeYesterdayTotalSpendUsd(usage),
            usage.HasYesterdayUsageData);

        var modelsYesterdayPercent = ResolveModelsYesterdayPercent(usage);

        FirstPartyTodaySpentText = FormatDailySpentToday(
            usage.TodayFirstPartyUsedPercent,
            QuotaMonetaryHelper.ResolveModelsTodayUsd(usage));
        FirstPartyYesterdaySpentText = FormatDailySpentYesterday(
            modelsYesterdayPercent,
            QuotaSpendResolver.ResolveModelsYesterdayUsd(usage),
            usage.HasYesterdayUsageData);

        ApiTodaySpentText = FormatDailySpentToday(
            usage.TodayApiUsedPercent,
            ApiPercentToUsd(usage.TodayApiUsedPercent, usage.ApiIncludedAmountUsd));
        ApiYesterdaySpentText = FormatDailySpentYesterday(
            usage.YesterdayApiUsedPercent,
            ApiPercentToUsd(usage.YesterdayApiUsedPercent, usage.ApiIncludedAmountUsd),
            usage.HasYesterdayUsageData);
    }

    private string BuildDailyTodayBreakdownText(QuotaUsage usage, CultureInfo culture)
    {
        var modelsUsd = QuotaMonetaryHelper.ResolveModelsTodayUsd(usage);
        var apiUsd = QuotaMonetaryHelper.ResolveApiTodayUsd(usage);
        var hasModels = modelsUsd is > 0.005m;
        var hasApi = apiUsd is > 0.005m;

        if (!hasModels && !hasApi)
            return string.Empty;

        if (hasModels && hasApi)
        {
            return _localizationService.Format(
                "DailyTodayBreakdownBothFormat",
                QuotaMonetaryHelper.FormatUsd(modelsUsd!.Value, culture),
                QuotaMonetaryHelper.FormatUsd(apiUsd!.Value, culture));
        }

        if (hasModels)
        {
            return _localizationService.Format(
                "DailyTodayBreakdownModelsOnlyFormat",
                QuotaMonetaryHelper.FormatUsd(modelsUsd!.Value, culture));
        }

        return _localizationService.Format(
            "DailyTodayBreakdownApiOnlyFormat",
            QuotaMonetaryHelper.FormatUsd(apiUsd!.Value, culture));
    }

    private decimal? ComputeTodayTotalSpendUsd(QuotaUsage usage) =>
        QuotaMonetaryHelper.ResolveTodayUsageUsd(usage);

    private decimal? ComputeYesterdayTotalSpendUsd(QuotaUsage usage) =>
        QuotaSpendResolver.ResolveCombinedYesterdayUsd(usage);

    private static double ResolveModelsYesterdayPercent(QuotaUsage usage)
    {
        if (usage.YesterdayFirstPartyUsedPercent > 0.001)
            return usage.YesterdayFirstPartyUsedPercent;

        if (usage.YesterdayTotalUsedPercent > 0.001 && usage.YesterdayApiUsedPercent < 0.001)
            return usage.YesterdayTotalUsedPercent;

        return usage.YesterdayFirstPartyUsedPercent;
    }

    private static decimal? SpendCentsToUsd(long? cents) =>
        cents is long value ? QuotaMonetaryHelper.CentsToUsd(value) : null;

    private static decimal? ApiPercentToUsd(double percent, decimal? includedUsd) =>
        includedUsd is null ? null : QuotaMonetaryHelper.PercentToUsd(percent, includedUsd.Value);

    private string FormatCombinedCardDailySpent(
        decimal? amountUsd,
        bool isYesterday,
        CultureInfo culture,
        bool hasData = true)
    {
        if (isYesterday && !hasData)
            return _localizationService.Format("CombinedYesterdaySpentFormat", "—");

        if (amountUsd is not null && amountUsd > 0m)
        {
            var formatted = QuotaMonetaryHelper.FormatUsd(amountUsd.Value, culture);
            return _localizationService.Format(
                isYesterday ? "CombinedYesterdaySpentFormat" : "CombinedTodaySpentFormat",
                formatted);
        }

        return _localizationService.Format(
            isYesterday ? "CombinedYesterdaySpentFormat" : "CombinedTodaySpentFormat",
            "—");
    }

    private string FormatDailySpentToday(double percent, decimal? amountUsd)
    {
        var culture = _localizationService.CurrentCulture;
        var percentText = PercentageFormatter.Format(percent, DisplayDigits, culture);

        if (amountUsd is not null)
        {
            return _localizationService.Format(
                "DailySpentTodayWithAmountFormat",
                percentText,
                QuotaMonetaryHelper.FormatUsd(amountUsd.Value, culture));
        }

        return _localizationService.Format("DailySpentTodayFormat", percentText);
    }

    private string FormatDailySpentYesterday(double percent, decimal? amountUsd, bool hasData)
    {
        if (!hasData)
            return _localizationService.Format("DailySpentYesterdayFormat", "—");

        var culture = _localizationService.CurrentCulture;
        var percentText = PercentageFormatter.Format(percent, DisplayDigits, culture);

        if (amountUsd is not null)
        {
            return _localizationService.Format(
                "DailySpentYesterdayWithAmountFormat",
                percentText,
                QuotaMonetaryHelper.FormatUsd(amountUsd.Value, culture));
        }

        return _localizationService.Format("DailySpentYesterdayFormat", percentText);
    }

    private string FormatPercentWithUsd(double percent, decimal? limitUsd, CultureInfo culture) =>
        QuotaMonetaryHelper.FormatPercentWithUsd(percent, limitUsd, DisplayDigits, culture);

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedLanguage));
        RebuildHistoryRangeOptions();
        OnPropertyChanged(nameof(ViewToggleTooltip));
        OnPropertyChanged(nameof(EstimatedLimitHintText));
        ResetDisplayTexts();

        if (IsStatisticsView)
            _ = LoadStatisticsAsync();
    }

    private async Task LoadStatisticsAsync()
    {
        try
        {
            var result = await _usageHistoryService.BuildAsync(
                SelectedHistoryRange,
                DateTime.Now,
                _localizationService.CurrentCulture,
                _lastSuccessfulUsage?.PeriodStart).ConfigureAwait(false);

            RunOnUi(() =>
            {
                UsageHistoryPoints = result.Points;
                HasStatisticsData = result.HasData;
                StatisticsSummaryText = _localizationService.Format(
                    "StatisticsSnapshotsFormat",
                    result.SnapshotCount);
            });
        }
        catch
        {
            RunOnUi(() =>
            {
                UsageHistoryPoints = [];
                HasStatisticsData = false;
                StatisticsSummaryText = _localizationService["StatisticsEmptyData"];
            });
        }
    }

    private void RebuildHistoryRangeOptions()
    {
        _historyRangeOptions =
        [
            new UsageHistoryRangeOption
            {
                Range = UsageHistoryRange.Today,
                DisplayName = _localizationService["HistoryRangeToday"]
            },
            new UsageHistoryRangeOption
            {
                Range = UsageHistoryRange.Week,
                DisplayName = _localizationService["HistoryRangeWeek"]
            },
            new UsageHistoryRangeOption
            {
                Range = UsageHistoryRange.Month,
                DisplayName = _localizationService["HistoryRangeMonth"]
            },
            new UsageHistoryRangeOption
            {
                Range = UsageHistoryRange.Year,
                DisplayName = _localizationService["HistoryRangeYear"]
            },
            new UsageHistoryRangeOption
            {
                Range = UsageHistoryRange.AllTime,
                DisplayName = _localizationService["HistoryRangeAllTime"]
            }
        ];

        OnPropertyChanged(nameof(HistoryRangeOptions));
    }

    private void ResetDisplayTexts()
    {
        var dash = "—";
        var dashPercent = $"{dash}%";

        TotalUsedPercentText = dash;
        TotalProgressLabel = $"{dash} / 100";
        TotalSpendText = dash;
        TotalRemainingText = _localizationService.Format("TotalRemainingFormat", dash);
        TotalRemainingAmountText = string.Empty;
        DaysUntilResetText = _localizationService.Format("QuotaResetInFormat", dash);
        StopResetCountdown();
        TotalDailyTargetText = _localizationService.Format("PerDayFormat", dashPercent);
        TotalTodaySpentText = _localizationService.Format("DailySpentTodayFormat", dash);
        TotalYesterdaySpentText = _localizationService.Format("DailySpentYesterdayFormat", dash);
        DailyTodaySpentText = TotalTodaySpentText;
        DailyTodayBreakdownText = string.Empty;
        DailyYesterdaySpentText = TotalYesterdaySpentText;
        TodaySpentText = TotalTodaySpentText;
        TodayStatusText = string.Empty;
        TodayOverageText = string.Empty;
        IsDailyTargetExceeded = false;
        DailyProgressFillPercent = 0;
        DailyPrimaryFillPercent = 0;
        DailySecondaryFillPercent = 0;
        DailyNormSegmentWeight = 1;
        DailyAheadSegmentWeight = 0;
        DailyProgressNormLabel = string.Empty;
        DailyProgressAheadLabel = string.Empty;
        FirstPartyUsedPercentText = dash;
        FirstPartySpendText = string.Empty;
        FirstPartyRemainingText = string.Empty;
        FirstPartyBonusText = string.Empty;
        FirstPartyBonusStatusText = string.Empty;
        FirstPartyPaceText = _localizationService.Format("PaceFormat", dashPercent);
        FirstPartyTodaySpentText = _localizationService.Format("DailySpentTodayFormat", dash);
        FirstPartyYesterdaySpentText = _localizationService.Format("DailySpentYesterdayFormat", dash);
        ApiUsedPercentText = dash;
        ApiSpendText = string.Empty;
        ApiPaceText = _localizationService.Format("PaceFormat", dashPercent);
        ApiTodaySpentText = _localizationService.Format("DailySpentTodayFormat", dash);
        ApiYesterdaySpentText = _localizationService.Format("DailySpentYesterdayFormat", dash);
        PaceStatusText = string.Empty;
        TodayPoolsDetailText = string.Empty;
        TotalPoolsDetailText = string.Empty;
        TotalBonusDetailText = string.Empty;
        UpdateLastUpdateText();
        SetRefreshingState(IsRefreshing);
        RebuildErrorMessage();

        if (_lastSuccessfulUsage is not null)
            ApplyUsage(_lastSuccessfulUsage);
        else
            NotifyTrayDisplayChanged();
    }

    private void StartResetCountdown()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (_resetCountdownTimer is null)
        {
            _resetCountdownTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _resetCountdownTimer.Tick += (_, _) => UpdateResetCountdownText();
        }

        UpdateResetCountdownInterval();
        if (!_resetCountdownTimer.IsEnabled)
            _resetCountdownTimer.Start();
    }

    private void StopResetCountdown()
    {
        _resetCountdownTimer?.Stop();
    }

    private void UpdateResetCountdownText()
    {
        if (_lastSuccessfulUsage is null)
            return;

        var remaining = _lastSuccessfulUsage.PeriodEnd - DateTime.Now;
        DaysUntilResetText = _localizationService.Format(
            "QuotaResetInFormat",
            RemainingTimeFormatter.Format(remaining, _localizationService));
        UpdateResetCountdownInterval();
    }

    private void UpdateResetCountdownInterval()
    {
        if (_resetCountdownTimer is null || _lastSuccessfulUsage is null)
            return;

        var remaining = _lastSuccessfulUsage.PeriodEnd - DateTime.Now;
        var interval = RemainingTimeFormatter.SuggestedRefreshInterval(remaining);
        if (_resetCountdownTimer.Interval != interval)
            _resetCountdownTimer.Interval = interval;
    }

    private void SetError(string key, params object[] args)
    {
        _errorMessageKey = key;
        _errorMessageArgs = args;
        ErrorMessage = _localizationService.Format(key, args);
    }

    private void RebuildErrorMessage()
    {
        if (string.IsNullOrWhiteSpace(_errorMessageKey))
        {
            ErrorMessage = null;
            return;
        }

        ErrorMessage = _localizationService.Format(_errorMessageKey, _errorMessageArgs);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static string FormatProgressNumber(double value, int decimalPlaces)
    {
        return PercentageFormatter.FormatNumber(
            value,
            decimalPlaces,
            LocalizationService.Instance.CurrentCulture);
    }

    private TrayDataState ResolveTrayDataState()
    {
        if (_lastSuccessfulUsage is not null)
            return TrayDataState.Ready;

        if (!_hasCompletedFirstRefreshAttempt || IsRefreshing)
            return TrayDataState.Loading;

        return TrayDataState.NoData;
    }

    private void NotifyTrayDisplayChanged()
    {
        TrayDisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}
