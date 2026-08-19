using System.Drawing;
using System.Reflection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using Quota.Localization;
using Quota.Models;
using Quota.ViewModels;
using Application = System.Windows.Application;

namespace Quota.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _mainWindow;
    private readonly ILocalizationService _localizationService;
    private bool _isDisposed;

    public TrayIconService(
        Window mainWindow,
        MainViewModel viewModel,
        ILocalizationService localizationService)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;
        _localizationService = localizationService;

        _notifyIcon = new NotifyIcon
        {
            Text = _localizationService["AppTitle"],
            Icon = LoadApplicationIcon(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        _localizationService.PropertyChanged += OnLocalizationChanged;
    }

    public bool IsExiting { get; private set; }

    public void RequestExit()
    {
        IsExiting = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _localizationService.PropertyChanged -= OnLocalizationChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _isDisposed = true;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem(_localizationService["Open"]);
        openItem.Click += (_, _) => ShowMainWindow();

        var refreshItem = new ToolStripMenuItem(_localizationService["RefreshNow"]);
        refreshItem.Click += (_, _) => _ = _viewModel.RefreshAsync(RefreshSource.Manual);

        var exitItem = new ToolStripMenuItem(_localizationService["Exit"]);
        exitItem.Click += (_, _) => RequestExit();

        menu.Items.Add(openItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        _notifyIcon.Text = _localizationService["AppTitle"];
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
    }

    private void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!_mainWindow.IsVisible)
                _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    private static Icon LoadApplicationIcon()
    {
        var processPath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var extracted = Icon.ExtractAssociatedIcon(processPath);
            if (extracted is not null)
                return (Icon)extracted.Clone();
        }

        return SystemIcons.Application;
    }
}
