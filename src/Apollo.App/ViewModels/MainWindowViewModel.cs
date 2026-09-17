using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Apollo.Commands;
using Apollo.Core.Configuration;
using Apollo.Core.Monitoring;
using Apollo.Core.Recorder;
using Apollo.Services;

namespace Apollo.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IConfigurationDialogService _dialogs;
    private readonly ProcessMonitorService _processMonitor;
    private readonly RecorderLifecycleCoordinator _recorderLifecycle;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly JsonConfigurationStore _store;
    private ApolloConfiguration _configuration = new();
    private RecorderConfiguration? _recorder;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isMonitoringEnabled;
    private bool _launchRecorderWithGames = true;
    private bool _closeRecorderWhenGamesExit = true;
    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _appliedStartWithWindows;
    private int _activeGameCount;
    private RecorderLifecycleStatus _recorderStatus;
    private ImageSource? _recorderIcon;
    private int _disposeStarted;

    public MainWindowViewModel(
        JsonConfigurationStore store,
        IConfigurationDialogService dialogs,
        IStartupRegistrationService startupRegistration,
        ProcessMonitorService processMonitor,
        RecorderLifecycleCoordinator recorderLifecycle)
    {
        _store = store;
        _dialogs = dialogs;
        _startupRegistration = startupRegistration;
        _processMonitor = processMonitor;
        _recorderLifecycle = recorderLifecycle;
        _processMonitor.ActiveGamesChanged += OnActiveGamesChanged;
        _processMonitor.SnapshotTaken += OnProcessSnapshotTaken;
        _recorderLifecycle.StatusChanged += OnRecorderStatusChanged;
        _recorderLifecycle.ActionFailed += OnRecorderActionFailed;

        ChooseRecorderCommand = new AsyncRelayCommand(ChooseRecorderAsync, CanConfigure, ShowOperationError);
        AddGameCommand = new AsyncRelayCommand(AddGameAsync, CanConfigure, ShowOperationError);
        RemoveGameCommand = new AsyncRelayCommand<GameItemViewModel>(RemoveGameAsync, _ => CanConfigure(), ShowOperationError);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanConfigure, ShowOperationError);
        SaveStartupSettingCommand = new AsyncRelayCommand(
            SaveStartupSettingAsync,
            CanConfigureStartup,
            ShowStartupError);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, CanToggleMonitoring, ShowOperationError);
    }

    public ObservableCollection<GameItemViewModel> Games { get; } = [];

    public AsyncRelayCommand ChooseRecorderCommand { get; }

    public AsyncRelayCommand AddGameCommand { get; }

    public AsyncRelayCommand<GameItemViewModel> RemoveGameCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand SaveStartupSettingCommand { get; }

    public AsyncRelayCommand ToggleMonitoringCommand { get; }

    public RecorderConfiguration? Recorder
    {
        get => _recorder;
        private set
        {
            if (!SetProperty(ref _recorder, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasRecorder));
            OnPropertyChanged(nameof(RecorderDisplayName));
            OnPropertyChanged(nameof(RecorderProcessName));
            _recorderIcon = ApplicationIconService.TryGetIcon(value?.ExecutablePath);
            OnPropertyChanged(nameof(RecorderIcon));
            NotifyMonitoringStateChanged();
        }
    }

    public bool HasRecorder => Recorder is not null;

    public bool HasGames => Games.Count > 0;

    public bool HasEnabledGames => Games.Any(game => game.IsEnabled);

    public string RecorderDisplayName => Recorder?.DisplayName ?? string.Empty;

    public string RecorderProcessName => Recorder is null ? string.Empty : $"{Recorder.ProcessName}.exe";

    public ImageSource? RecorderIcon => _recorderIcon;

    public string RecorderStatusText => _recorderStatus switch
    {
        RecorderLifecycleStatus.Running => "Running",
        RecorderLifecycleStatus.Suppressed => "Paused for this session",
        _ => "Idle",
    };

    public bool IsConfigurationAvailable => _isInitialized && !_isBusy;

    public bool IsMonitoringAvailable => IsConfigurationAvailable && HasRecorder && HasEnabledGames;

    public bool IsBehaviorAvailable => IsConfigurationAvailable;

    public bool IsStartupAvailable => IsConfigurationAvailable && _startupRegistration.IsSupported;

    public bool IsStartMinimizedAvailable => IsConfigurationAvailable;

    public string MonitoringStatusText
    {
        get
        {
            if (!_isInitialized)
            {
                return "Loading";
            }

            if (!HasRecorder || !HasEnabledGames)
            {
                return "Configure a recorder and game";
            }

            if (!IsMonitoringEnabled)
            {
                return "Off";
            }

            return _activeGameCount switch
            {
                0 => "Watching for games",
                1 => "1 game active",
                _ => $"{_activeGameCount} games active",
            };
        }
    }

    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set
        {
            if (SetProperty(ref _isMonitoringEnabled, value))
            {
                _configuration.Settings.MonitoringEnabled = value;
                OnPropertyChanged(nameof(MonitoringStatusText));
            }
        }
    }

    public bool LaunchRecorderWithGames
    {
        get => _launchRecorderWithGames;
        set
        {
            if (SetProperty(ref _launchRecorderWithGames, value))
            {
                _configuration.Settings.LaunchRecorderWithGames = value;
            }
        }
    }

    public bool CloseRecorderWhenGamesExit
    {
        get => _closeRecorderWhenGamesExit;
        set
        {
            if (SetProperty(ref _closeRecorderWhenGamesExit, value))
            {
                _configuration.Settings.CloseRecorderWhenGamesExit = value;
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _configuration.Settings.StartWithWindows = value;
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                _configuration.Settings.StartMinimized = value;
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _configuration = await _store.LoadAsync();
            ApplyConfiguration();
            bool startupStateCorrected = SynchronizeStartupRegistration();
            _isInitialized = true;
            NotifyCapabilitiesChanged();

            bool monitoringStateCorrected = await SynchronizeMonitoringAsync();
            if (startupStateCorrected || monitoringStateCorrected)
            {
                await SaveConfigurationAsync();
            }
        }
        catch (Exception exception)
        {
            _isInitialized = true;
            NotifyCapabilitiesChanged();
            ShowOperationError(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _processMonitor.ActiveGamesChanged -= OnActiveGamesChanged;
        _processMonitor.SnapshotTaken -= OnProcessSnapshotTaken;
        _recorderLifecycle.StatusChanged -= OnRecorderStatusChanged;
        _recorderLifecycle.ActionFailed -= OnRecorderActionFailed;
        await _processMonitor.DisposeAsync().ConfigureAwait(false);
        await _recorderLifecycle.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ChooseRecorderAsync()
    {
        RecorderConfiguration? recorder = _dialogs.ShowRecorderDialog(Recorder);
        if (recorder is null)
        {
            return;
        }

        _configuration.Recorder = recorder;
        Recorder = recorder;
        await SynchronizeMonitoringAsync();
        await SaveConfigurationAsync();
    }

    private async Task AddGameAsync()
    {
        GameConfiguration? game = _dialogs.ShowGameDialog();
        if (game is null)
        {
            return;
        }

        if (_configuration.Games.Any(existing => string.Equals(
                existing.ProcessName,
                game.ProcessName,
                StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.ShowError("Game already added", $"{game.DisplayName} is already monitored by process name.");
            return;
        }

        _configuration.Games.Add(game);
        Games.Add(new GameItemViewModel(game));
        OnPropertyChanged(nameof(HasGames));
        NotifyMonitoringStateChanged();
        await SynchronizeMonitoringAsync();
        await SaveConfigurationAsync();
    }

    private async Task RemoveGameAsync(GameItemViewModel game)
    {
        if (!_dialogs.ConfirmGameRemoval(game.DisplayName))
        {
            return;
        }

        _configuration.Games.RemoveAll(candidate => candidate.Id == game.Id);
        Games.Remove(game);
        OnPropertyChanged(nameof(HasGames));
        NotifyMonitoringStateChanged();
        await SynchronizeMonitoringAsync();
        await SaveConfigurationAsync();
    }

    private async Task SaveSettingsAsync()
    {
        NotifyMonitoringStateChanged();
        await SynchronizeMonitoringAsync();
        await SaveConfigurationAsync();
    }

    private async Task SaveStartupSettingAsync()
    {
        bool previousValue = _appliedStartWithWindows;
        bool desiredValue = StartWithWindows;

        try
        {
            _startupRegistration.SetEnabled(desiredValue);
            await SaveConfigurationAsync();
            _appliedStartWithWindows = desiredValue;
        }
        catch
        {
            try
            {
                _startupRegistration.SetEnabled(previousValue);
            }
            catch
            {
                // Preserve the original error. Startup state is reconciled on the next launch.
            }

            StartWithWindows = previousValue;
            throw;
        }
    }

    private async Task ToggleMonitoringAsync()
    {
        await SynchronizeMonitoringAsync();
        await SaveConfigurationAsync();
    }

    private async Task<bool> SynchronizeMonitoringAsync()
    {
        await _recorderLifecycle.UpdateConfigurationAsync(
            Recorder,
            LaunchRecorderWithGames,
            CloseRecorderWhenGamesExit);
        _processMonitor.UpdateGames(_configuration.Games);
        bool monitoringStateCorrected = false;

        if (!HasRecorder || !HasEnabledGames)
        {
            if (IsMonitoringEnabled)
            {
                IsMonitoringEnabled = false;
                monitoringStateCorrected = true;
            }

            await _processMonitor.StopAsync();
            await _recorderLifecycle.ResetAsync();
        }
        else if (IsMonitoringEnabled)
        {
            if (_processMonitor.IsRunning)
            {
                await _processMonitor.RefreshAsync();
            }
            else
            {
                await _processMonitor.StartAsync();
            }
        }
        else
        {
            await _processMonitor.StopAsync();
            await _recorderLifecycle.ResetAsync();
        }

        NotifyMonitoringStateChanged();
        return monitoringStateCorrected;
    }

    private async Task SaveConfigurationAsync()
    {
        SetBusy(true);
        try
        {
            await _store.SaveAsync(_configuration);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyConfiguration()
    {
        Recorder = _configuration.Recorder;

        Games.Clear();
        foreach (GameConfiguration game in _configuration.Games)
        {
            Games.Add(new GameItemViewModel(game));
        }

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(HasEnabledGames));
        IsMonitoringEnabled = _configuration.Settings.MonitoringEnabled;
        LaunchRecorderWithGames = _configuration.Settings.LaunchRecorderWithGames;
        CloseRecorderWhenGamesExit = _configuration.Settings.CloseRecorderWhenGamesExit;
        StartWithWindows = _configuration.Settings.StartWithWindows;
        StartMinimized = _configuration.Settings.StartMinimized;
    }

    private bool SynchronizeStartupRegistration()
    {
        bool desiredValue = StartWithWindows;
        try
        {
            _startupRegistration.SetEnabled(desiredValue);
            _appliedStartWithWindows = desiredValue;
            return false;
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException
                                          or UnauthorizedAccessException)
        {
            bool actualValue = false;
            try
            {
                actualValue = _startupRegistration.IsEnabled();
            }
            catch (Exception stateException) when (stateException is IOException
                                                   or System.Security.SecurityException
                                                   or UnauthorizedAccessException)
            {
            }

            _appliedStartWithWindows = actualValue;
            StartWithWindows = actualValue;
            ShowStartupError(exception);
            return actualValue != desiredValue;
        }
    }

    private void OnActiveGamesChanged(object? sender, ActiveGamesChangedEventArgs e)
    {
        void ApplyActiveGames()
        {
            foreach (GameItemViewModel game in Games)
            {
                game.IsRunning = e.ActiveGameIds.Contains(game.Id);
            }

            _activeGameCount = e.ActiveGameIds.Count;
            OnPropertyChanged(nameof(MonitoringStatusText));
        }

        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ApplyActiveGames();
        }
        else
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ApplyActiveGames);
        }
    }

    private void OnProcessSnapshotTaken(object? sender, ProcessSnapshotEventArgs e)
    {
        _ = HandleRecorderSnapshotAsync(e);
    }

    private async Task HandleRecorderSnapshotAsync(ProcessSnapshotEventArgs snapshot)
    {
        if (!IsMonitoringEnabled)
        {
            return;
        }

        try
        {
            await _recorderLifecycle.HandleSnapshotAsync(
                snapshot.ActiveGameIds,
                snapshot.RunningProcessNames);
        }
        catch (Exception exception)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ShowOperationError(exception));
        }
    }

    private void OnRecorderStatusChanged(object? sender, RecorderStatusChangedEventArgs e)
    {
        void ApplyStatus()
        {
            _recorderStatus = e.Status;
            OnPropertyChanged(nameof(RecorderStatusText));
        }

        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ApplyStatus();
        }
        else
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ApplyStatus);
        }
    }

    private void OnRecorderActionFailed(object? sender, RecorderActionFailedEventArgs e)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            _dialogs.ShowError(
                e.Action == "start" ? "Recorder could not start" : "Recorder could not close",
                e.Exception.Message));
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value)
        {
            return;
        }

        _isBusy = value;
        NotifyCapabilitiesChanged();
    }

    private bool CanConfigure() => IsConfigurationAvailable;

    private bool CanConfigureStartup() => IsStartupAvailable;

    private bool CanToggleMonitoring() => IsMonitoringAvailable;

    private void NotifyCapabilitiesChanged()
    {
        OnPropertyChanged(nameof(IsConfigurationAvailable));
        OnPropertyChanged(nameof(IsBehaviorAvailable));
        OnPropertyChanged(nameof(IsStartupAvailable));
        OnPropertyChanged(nameof(IsStartMinimizedAvailable));
        NotifyMonitoringStateChanged();
        ChooseRecorderCommand.RaiseCanExecuteChanged();
        AddGameCommand.RaiseCanExecuteChanged();
        RemoveGameCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        SaveStartupSettingCommand.RaiseCanExecuteChanged();
    }

    private void NotifyMonitoringStateChanged()
    {
        OnPropertyChanged(nameof(HasEnabledGames));
        OnPropertyChanged(nameof(IsMonitoringAvailable));
        OnPropertyChanged(nameof(MonitoringStatusText));
        ToggleMonitoringCommand.RaiseCanExecuteChanged();
    }

    private void ShowOperationError(Exception exception)
    {
        _dialogs.ShowError("Apollo could not update its settings", exception.Message);
    }

    private void ShowStartupError(Exception exception)
    {
        _dialogs.ShowError("Windows startup could not be updated", exception.Message);
    }
}
