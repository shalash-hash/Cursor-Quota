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
            Icon = LoadApplicationIcon(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _viewModel.TrayDisplayChanged += OnTrayDisplayChanged;
        _localizationService.PropertyChanged += OnLocalizationChanged;

        UpdateTrayDisplay();
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
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

        _viewModel.TrayDisplayChanged -= OnTrayDisplayChanged;
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _isDisposed = true;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        var displayState = _viewModel.GetTrayDisplayState();

        foreach (var line in displayState.InfoMenuLines)
        {
            menu.Items.Add(CreateInfoItem(line));
        }

        if (displayState.InfoMenuLines.Count > 0)
            menu.Items.Add(new ToolStripSeparator());

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

    private static ToolStripMenuItem CreateInfoItem(string text)
    {
        return new ToolStripMenuItem(text)
        {
            Enabled = false
        };
    }

    private void OnTrayDisplayChanged(object? sender, EventArgs e)
    {
        if (_isDisposed)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_isDisposed)
                return;

            UpdateTrayDisplay();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.ContextMenuStrip = BuildContextMenu();
        });
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnTrayDisplayChanged(sender, e);
    }

    private void UpdateTrayDisplay()
    {
        var displayState = _viewModel.GetTrayDisplayState();
        _notifyIcon.Text = displayState.TooltipText;
    }

    private void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_mainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowFromTray();
                return;
            }

            _mainWindow.ShowInTaskbar = true;

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
