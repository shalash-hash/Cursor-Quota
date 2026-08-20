using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
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
            Hide();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
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

        base.OnClosing(e);
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
