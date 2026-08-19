using System.ComponentModel;
using System.Windows;
using Quota.Services;
using Quota.ViewModels;

namespace Quota;

public partial class MainWindow : Window
{
    private TrayIconService? _trayIconService;
    private bool _isHidingToTray;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        StateChanged += OnStateChanged;
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
            ShowInTaskbar = false;
            WindowState = WindowState.Normal;
            Hide();
        }
        finally
        {
            _isHidingToTray = false;
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

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_isHidingToTray)
            return;

        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_trayIconService is { IsExiting: false })
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }
}
