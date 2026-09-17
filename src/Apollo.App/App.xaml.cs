using System.IO;
using System.ComponentModel;
using System.Windows;
using Apollo.Core.Configuration;
using Apollo.Core.Monitoring;
using Apollo.Core.Recorder;
using Apollo.Services;
using Apollo.ViewModels;

namespace Apollo;

public partial class App : System.Windows.Application
{
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIconService? _trayIcon;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configurationPath = Path.Combine(applicationData, "Apollo", "settings.json");
        _viewModel = new MainWindowViewModel(
            new JsonConfigurationStore(configurationPath),
            new ConfigurationDialogService(),
            new WindowsStartupRegistrationService(),
            new ProcessMonitorService(
                new SystemProcessSnapshotProvider(),
                TimeSpan.FromSeconds(2)),
            new RecorderLifecycleCoordinator(
                new RecorderProcessController(),
                TimeSpan.FromSeconds(5)));

        _window = new MainWindow(_viewModel);
        _window.Closing += OnMainWindowClosing;
        MainWindow = _window;

        _trayIcon = new TrayIconService();
        _trayIcon.OpenRequested += OnOpenRequested;
        _trayIcon.MonitoringToggleRequested += OnMonitoringToggleRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        await _viewModel.InitializeAsync();
        UpdateTrayIcon();

        if (!_viewModel.StartMinimized)
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeTrayIcon();
        _viewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _window?.Hide();
    }

    private void OnOpenRequested(object? sender, EventArgs e) => ShowMainWindow();

    private void OnMonitoringToggleRequested(object? sender, EventArgs e)
    {
        if (_viewModel is null || !_viewModel.ToggleMonitoringCommand.CanExecute(null))
        {
            return;
        }

        _viewModel.IsMonitoringEnabled = !_viewModel.IsMonitoringEnabled;
        _viewModel.ToggleMonitoringCommand.Execute(null);
        UpdateTrayIcon();
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        DisposeTrayIcon();

        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.DisposeAsync();
            }
        }
        finally
        {
            _window?.Close();
            Shutdown();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsMonitoringAvailable)
            or nameof(MainWindowViewModel.IsMonitoringEnabled)
            or nameof(MainWindowViewModel.RecorderStatusText))
        {
            UpdateTrayIcon();
        }
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon is null || _viewModel is null)
        {
            return;
        }

        _trayIcon.Update(
            _viewModel.IsMonitoringAvailable,
            _viewModel.IsMonitoringEnabled,
            _viewModel.RecorderStatusText);
    }

    private void DisposeTrayIcon()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.OpenRequested -= OnOpenRequested;
        _trayIcon.MonitoringToggleRequested -= OnMonitoringToggleRequested;
        _trayIcon.ExitRequested -= OnExitRequested;
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
