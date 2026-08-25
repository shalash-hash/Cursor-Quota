using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
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

    private readonly IReadOnlyList<int> _precisionOptions = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
    private readonly UiSettings _uiSettings;

    private QuotaUsage? _lastSuccessfulUsage;
    private DateTime? _lastSuccessfulUpdate;
    private bool _hasCompletedFirstRefreshAttempt;

    private string _totalUsedPercentText = "—";
    private double _totalProgressValue;
    private string _totalProgressLabel = "— / 100";
    private string _totalRemainingText = "—";
    private string _daysUntilResetText = "—";
    private string _totalDailyTargetText = "—";

    private string _totalTodaySpentText = "—";
    private string _totalYesterdaySpentText = "—";
    private string _dailyTodaySpentText = "—";
    private string _dailyYesterdaySpentText = "—";

    private string _todaySpentText = "—";
    private string _todayStatusText = string.Empty;
    private string _todayOverageText = string.Empty;
    private bool _isDailyTargetExceeded;
    private double _dailyProgressFillPercent;
    private double _dailyNormSegmentWeight = 1;
    private double _dailyAheadSegmentWeight;
    private string _dailyProgressNormLabel = string.Empty;
    private string _dailyProgressAheadLabel = string.Empty;

    private string _firstPartyUsedPercentText = "—";
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

    private string? _errorMessage;
    private string? _errorMessageKey;
    private object[] _errorMessageArgs = [];
    private string _lastUpdateText = string.Empty;
    private string _lastUpdateTimeText = string.Empty;
    private string _refreshButtonText = string.Empty;
    private bool _isRefreshing;
    private bool _isStartupEnabled;
    private bool _isDarkMode;
    private int _selectedDecimalPlaces;
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
        _selectedDecimalPlaces = Math.Clamp(_uiSettings.PercentageDecimalPlaces, 0, 7);
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
        get => _selectedDecimalPlaces;
        set
        {
            var sanitized = Math.Clamp(value, 0, 7);
            if (!SetProperty(ref _selectedDecimalPlaces, sanitized))
                return;

            var settings = _uiSettingsService.Load();
            settings.PercentageDecimalPlaces = sanitized;
            _uiSettingsService.Save(settings);

            if (_lastSuccessfulUsage is not null)
                ApplyUsage(_lastSuccessfulUsage);
            else
                NotifyTrayDisplayChanged();
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
            var usage = await _snapshotRepository.EnrichWithTodayUsageAsync(snapshot).ConfigureAwait(false);
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
            SelectedDecimalPlaces,
            _localizationService);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
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
        var digits = SelectedDecimalPlaces;
        var culture = _localizationService.CurrentCulture;

        TotalUsedPercentText = PercentageFormatter.Format(usage.TotalUsedPercent, digits, culture);
        TotalProgressValue = Math.Max(0, usage.TotalUsedPercent);
        TotalProgressLabel = $"{FormatProgressNumber(usage.TotalUsedPercent, digits)} / 100";
        TotalRemainingText = _localizationService.Format(
            "TotalRemainingFormat",
            PercentageFormatter.Format(Math.Max(0, calculation.Total.RemainingPercent), digits, culture));
        DaysUntilResetText = _localizationService.Format(
            "QuotaResetInFormat",
            PercentageFormatter.FormatDays(calculation.RemainingDays, _localizationService));
        TotalDailyTargetText = _localizationService.Format(
            "PerDayFormat",
            PercentageFormatter.Format(calculation.Total.DailyTarget, digits, culture));

        ApplyDailySpentTexts(usage, digits, culture);

        TodaySpentText = DailyTodaySpentText;

        var dailyProgress = DailyTargetProgressCalculator.Calculate(
            usage.TodayTotalUsedPercent,
            calculation.Total.DailyTarget);

        IsDailyTargetExceeded = dailyProgress.IsExceeded;
        DailyProgressFillPercent = dailyProgress.FillPercent;
        DailyNormSegmentWeight = dailyProgress.NormSegmentWeight;
        DailyAheadSegmentWeight = dailyProgress.AheadSegmentWeight;

        if (dailyProgress.IsExceeded)
        {
            DailyProgressNormLabel = PercentageFormatter.Format(100, digits, culture);
            DailyProgressAheadLabel = calculation.Total.TodayOverage > 0
                ? _localizationService.Format(
                    "AheadOfScheduleFormat",
                    PercentageFormatter.Format(calculation.Total.TodayOverage, digits, culture))
                : string.Empty;
        }
        else
        {
            DailyProgressNormLabel = PercentageFormatter.Format(dailyProgress.PlanCompletionPercent, digits, culture);
            DailyProgressAheadLabel = string.Empty;
        }

        if (calculation.Total.IsTodayPlanCompleted)
        {
            TodayStatusText = _localizationService["TodayPlanCompleted"];
            TodayOverageText = string.Empty;
        }
        else
        {
            TodayStatusText = _localizationService.Format(
                "TodayRemainingFormat",
                PercentageFormatter.Format(calculation.Total.TodayRemaining, digits, culture));
            TodayOverageText = string.Empty;
        }

        FirstPartyUsedPercentText = PercentageFormatter.Format(usage.FirstPartyUsedPercent, digits, culture);
        FirstPartyProgressValue = Math.Max(0, usage.FirstPartyUsedPercent);
        FirstPartyPaceText = _localizationService.Format(
            "PaceFormat",
            PercentageFormatter.Format(calculation.FirstParty.DailyTarget, digits, culture));

        ApiUsedPercentText = PercentageFormatter.Format(usage.ApiUsedPercent, digits, culture);
        ApiProgressValue = Math.Max(0, usage.ApiUsedPercent);
        ApiPaceText = _localizationService.Format(
            "PaceFormat",
            PercentageFormatter.Format(calculation.Api.DailyTarget, digits, culture));

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

        TodayPoolsDetailText = _localizationService.Format(
            "TodayPoolsDetailFormat",
            PercentageFormatter.Format(usage.TodayFirstPartyUsedPercent, digits, culture),
            PercentageFormatter.Format(usage.TodayApiUsedPercent, digits, culture));

        NotifyTrayDisplayChanged();
    }

    private void ApplyDailySpentTexts(QuotaUsage usage, int digits, CultureInfo culture)
    {
        TotalTodaySpentText = FormatDailySpentLine(
            "DailySpentTodayFormat",
            usage.TodayTotalUsedPercent,
            digits,
            culture);
        TotalYesterdaySpentText = FormatDailySpentLine(
            "DailySpentYesterdayFormat",
            usage.YesterdayTotalUsedPercent,
            usage.HasYesterdayUsageData,
            digits,
            culture);

        DailyTodaySpentText = TotalTodaySpentText;
        DailyYesterdaySpentText = TotalYesterdaySpentText;

        FirstPartyTodaySpentText = FormatDailySpentLine(
            "DailySpentTodayFormat",
            usage.TodayFirstPartyUsedPercent,
            digits,
            culture);
        FirstPartyYesterdaySpentText = FormatDailySpentLine(
            "DailySpentYesterdayFormat",
            usage.YesterdayFirstPartyUsedPercent,
            usage.HasYesterdayUsageData,
            digits,
            culture);

        ApiTodaySpentText = FormatDailySpentLine(
            "DailySpentTodayFormat",
            usage.TodayApiUsedPercent,
            digits,
            culture);
        ApiYesterdaySpentText = FormatDailySpentLine(
            "DailySpentYesterdayFormat",
            usage.YesterdayApiUsedPercent,
            usage.HasYesterdayUsageData,
            digits,
            culture);
    }

    private string FormatDailySpentLine(
        string resourceKey,
        double value,
        int digits,
        CultureInfo culture)
        => FormatDailySpentLine(resourceKey, value, true, digits, culture);

    private string FormatDailySpentLine(
        string resourceKey,
        double value,
        bool hasData,
        int digits,
        CultureInfo culture)
    {
        var formattedValue = hasData
            ? PercentageFormatter.Format(value, digits, culture)
            : "—";

        return _localizationService.Format(resourceKey, formattedValue);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedLanguage));
        RebuildHistoryRangeOptions();
        OnPropertyChanged(nameof(ViewToggleTooltip));
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
                _localizationService.CurrentCulture).ConfigureAwait(false);

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
        TotalRemainingText = _localizationService.Format("TotalRemainingFormat", dash);
        DaysUntilResetText = _localizationService.Format("QuotaResetInFormat", dash);
        TotalDailyTargetText = _localizationService.Format("PerDayFormat", dashPercent);
        TotalTodaySpentText = _localizationService.Format("DailySpentTodayFormat", dash);
        TotalYesterdaySpentText = _localizationService.Format("DailySpentYesterdayFormat", dash);
        DailyTodaySpentText = TotalTodaySpentText;
        DailyYesterdaySpentText = TotalYesterdaySpentText;
        TodaySpentText = TotalTodaySpentText;
        TodayStatusText = string.Empty;
        TodayOverageText = string.Empty;
        IsDailyTargetExceeded = false;
        DailyProgressFillPercent = 0;
        DailyNormSegmentWeight = 1;
        DailyAheadSegmentWeight = 0;
        DailyProgressNormLabel = string.Empty;
        DailyProgressAheadLabel = string.Empty;
        FirstPartyUsedPercentText = dash;
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
        UpdateLastUpdateText();
        SetRefreshingState(IsRefreshing);
        RebuildErrorMessage();

        if (_lastSuccessfulUsage is not null)
            ApplyUsage(_lastSuccessfulUsage);
        else
            NotifyTrayDisplayChanged();
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
