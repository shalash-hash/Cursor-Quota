using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Quota.Services;
using Quota.ViewModels;

namespace Quota;

public partial class MainWindow : Window
{
    private TrayIconService? _trayIconService;
    private UiSettingsService? _uiSettingsService;
    private UiSettings? _uiSettings;
    private bool _isHidingToTray;
    private bool _isStartupTrayHide;
    private DispatcherTimer? _saveBoundsTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void ApplyWindowSettings(UiSettingsService uiSettingsService)
    {
        _uiSettingsService = uiSettingsService;
        _uiSettings = uiSettingsService.Load();

        Width = _uiSettings.WindowWidth ?? UiSettingsService.DefaultWindowWidth;
        Height = _uiSettings.WindowHeight ?? UiSettingsService.DefaultWindowHeight;
        SizeChanged += OnWindowSizeChanged;
    }

    public void PrepareInitialTrayHide()
    {
        _isStartupTrayHide = true;
    }

    public void SetTrayService(TrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
    }

    public void HideToTray()
    {
        if (_isHidingToTray)
            return;

        _isHidingToTray = true;
        try
        {
            if (!_isStartupTrayHide)
                SaveWindowBounds();

            ShowInTaskbar = false;
            Hide();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }
        finally
        {
            _isHidingToTray = false;
            _isStartupTrayHide = false;
        }
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;

        if (!IsVisible)
            Show();

        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (_isHidingToTray || WindowState != WindowState.Minimized)
            return;

        Dispatcher.BeginInvoke(HideToTray, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_trayIconService is { IsExiting: false })
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        FlushPendingBoundsSave();
        SaveWindowBounds();
        base.OnClosing(e);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isStartupTrayHide || !IsLoaded || WindowState != WindowState.Normal)
            return;

        ScheduleBoundsSave();
    }

    private void ScheduleBoundsSave()
    {
        _saveBoundsTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };

        _saveBoundsTimer.Stop();
        _saveBoundsTimer.Tick -= OnSaveBoundsTimerTick;
        _saveBoundsTimer.Tick += OnSaveBoundsTimerTick;
        _saveBoundsTimer.Start();
    }

    private void OnSaveBoundsTimerTick(object? sender, EventArgs e)
    {
        _saveBoundsTimer?.Stop();
        _saveBoundsTimer!.Tick -= OnSaveBoundsTimerTick;
        SaveWindowBounds();
    }

    private void FlushPendingBoundsSave()
    {
        if (_saveBoundsTimer is null)
            return;

        _saveBoundsTimer.Stop();
        _saveBoundsTimer.Tick -= OnSaveBoundsTimerTick;
    }

    private void SaveWindowBounds()
    {
        if (_uiSettingsService is null)
            return;

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var width = UiSettingsService.SanitizeWindowSize(bounds.Width, UiSettingsService.MinWindowWidth);
        var height = UiSettingsService.SanitizeWindowSize(bounds.Height, UiSettingsService.MinWindowHeight);
        if (width is null || height is null)
            return;

        var settings = _uiSettingsService.Load();
        if (settings.WindowWidth == width && settings.WindowHeight == height)
            return;

        settings.WindowWidth = width;
        settings.WindowHeight = height;
        _uiSettingsService.Save(settings);
        _uiSettings = settings;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmSysCommand = 0x0112;
        const int scMinimize = 0xF020;

        if (msg == wmSysCommand && (wParam.ToInt32() & 0xFFF0) == scMinimize)
        {
            HideToTray();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
