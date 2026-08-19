using System.ComponentModel;
using System.Windows;
using Quota.Services;
using Quota.ViewModels;

namespace Quota;

public partial class MainWindow : Window
{
    private TrayIconService? _trayIconService;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void SetTrayService(TrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_trayIconService is { IsExiting: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
