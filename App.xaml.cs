using System.Windows;
using Quota.Localization;
using Quota.Services;
using Quota.ViewModels;
using WpfApplication = System.Windows.Application;

namespace Quota;

public partial class App : WpfApplication
{
    private TrayIconService? _trayIconService;
    private MainViewModel? _viewModel;
    private CursorHttpTransport? _httpTransport;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _httpTransport = new CursorHttpTransport();

        var diagnosticLogger = new QuotaDiagnosticLogger();
        var authService = new CursorAuthService(_httpTransport, diagnosticLogger);
        var quotaUsageProvider = new CursorQuotaUsageProvider(_httpTransport, authService, diagnosticLogger);
        var quotaCalculator = new QuotaCalculator();
        var startupService = new StartupService();
        var snapshotRepository = new QuotaSnapshotRepository();
        var usageHistoryService = new UsageHistoryService(snapshotRepository);
        var uiSettingsService = new UiSettingsService();
        var themeService = new ThemeService(uiSettingsService);
        themeService.ApplySavedTheme();
        var localizationService = new LocalizationService(uiSettingsService);

        _viewModel = new MainViewModel(
            quotaUsageProvider,
            quotaCalculator,
            startupService,
            snapshotRepository,
            usageHistoryService,
            diagnosticLogger,
            uiSettingsService,
            themeService,
            localizationService,
            _httpTransport);

        var mainWindow = new MainWindow(_viewModel);
        mainWindow.ApplyWindowSettings(uiSettingsService);
        _trayIconService = new TrayIconService(mainWindow, _viewModel, localizationService);
        mainWindow.SetTrayService(_trayIconService);

        MainWindow = mainWindow;
        mainWindow.PrepareInitialTrayHide();
        mainWindow.Show();
        mainWindow.HideToTray();

        await _viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _trayIconService?.Dispose();
        _httpTransport?.Dispose();
        base.OnExit(e);
    }
}
