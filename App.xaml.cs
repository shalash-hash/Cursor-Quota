using System.Net.Http;
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
    private HttpClient? _httpClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var diagnosticLogger = new QuotaDiagnosticLogger();
        var authService = new CursorAuthService(_httpClient);
        var quotaUsageProvider = new CursorQuotaUsageProvider(_httpClient, authService, diagnosticLogger);
        var quotaCalculator = new QuotaCalculator();
        var startupService = new StartupService();
        var snapshotRepository = new QuotaSnapshotRepository();
        var uiSettingsService = new UiSettingsService();
        var localizationService = new LocalizationService(uiSettingsService);

        _viewModel = new MainViewModel(
            quotaUsageProvider,
            quotaCalculator,
            startupService,
            snapshotRepository,
            diagnosticLogger,
            uiSettingsService,
            localizationService);

        var mainWindow = new MainWindow(_viewModel);
        _trayIconService = new TrayIconService(mainWindow, _viewModel, localizationService);
        mainWindow.SetTrayService(_trayIconService);

        MainWindow = mainWindow;
        mainWindow.Show();

        await _viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _trayIconService?.Dispose();
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
