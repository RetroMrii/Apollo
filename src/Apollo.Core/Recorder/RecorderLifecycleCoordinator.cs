using Apollo.Core.Configuration;

namespace Apollo.Core.Recorder;

public sealed class RecorderLifecycleCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IRecorderProcessController _processController;
    private readonly TimeSpan _shutdownDelay;
    private RecorderConfiguration? _recorder;
    private bool _launchEnabled = true;
    private bool _closeEnabled = true;
    private bool _sessionActive;
    private bool _launchAttempted;
    private bool _recorderObserved;
    private bool _suppressed;
    private HashSet<Guid> _activeGameIds = [];
    private RecorderLifecycleStatus _status;
    private CancellationTokenSource? _shutdownCancellation;
    private Task? _shutdownTask;

    public RecorderLifecycleCoordinator(
        IRecorderProcessController processController,
        TimeSpan shutdownDelay)
    {
        ArgumentNullException.ThrowIfNull(processController);
        if (shutdownDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shutdownDelay));
        }

        _processController = processController;
        _shutdownDelay = shutdownDelay;
    }

    public event EventHandler<RecorderStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<RecorderActionFailedEventArgs>? ActionFailed;

    public RecorderLifecycleStatus Status => _status;

    public bool IsSuppressedForCurrentSession => _suppressed;

    public async Task UpdateConfigurationAsync(
        RecorderConfiguration? recorder,
        bool launchEnabled,
        bool closeEnabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool recorderChanged = !IsSameRecorder(_recorder, recorder);
            _recorder = recorder;
            _launchEnabled = launchEnabled;
            _closeEnabled = closeEnabled;

            if (recorderChanged)
            {
                CancelPendingShutdown();
                ResetSession();
                SetStatus(RecorderLifecycleStatus.Idle);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleSnapshotAsync(
        IReadOnlySet<Guid> activeGameIds,
        IReadOnlySet<string> runningProcessNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeGameIds);
        ArgumentNullException.ThrowIfNull(runningProcessNames);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _activeGameIds = new HashSet<Guid>(activeGameIds);
            bool recorderRunning = IsConfiguredRecorderRunning(runningProcessNames);

            if (recorderRunning)
            {
                _recorderObserved = _sessionActive;
                SetStatus(RecorderLifecycleStatus.Running);
            }
            else if (!_sessionActive)
            {
                SetStatus(RecorderLifecycleStatus.Idle);
            }

            if (_activeGameIds.Count == 0)
            {
                if (_sessionActive && _shutdownTask is null)
                {
                    ScheduleSessionCompletion();
                }

                return;
            }

            CancelPendingShutdown();
            bool isNewSession = !_sessionActive;
            if (isNewSession)
            {
                _sessionActive = true;
                _launchAttempted = false;
                _recorderObserved = recorderRunning;
                _suppressed = false;
            }
            else if (!recorderRunning && (_recorderObserved || _launchAttempted))
            {
                _suppressed = true;
                SetStatus(RecorderLifecycleStatus.Suppressed);
            }

            if (recorderRunning
                || _suppressed
                || _launchAttempted
                || !_launchEnabled
                || _recorder is null)
            {
                return;
            }

            _launchAttempted = true;
            try
            {
                bool runningAfterRequest = await _processController
                    .StartIfNotRunningAsync(_recorder, cancellationToken)
                    .ConfigureAwait(false);

                if (runningAfterRequest)
                {
                    _recorderObserved = true;
                    SetStatus(RecorderLifecycleStatus.Running);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or IOException
                                              or UnauthorizedAccessException)
            {
                _suppressed = true;
                SetStatus(RecorderLifecycleStatus.Suppressed);
                ActionFailed?.Invoke(this, new RecorderActionFailedEventArgs("start", exception));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPendingShutdown();
            ResetSession();
            SetStatus(RecorderLifecycleStatus.Idle);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingShutdown;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            pendingShutdown = _shutdownTask;
            CancelPendingShutdown();
            ResetSession();
        }
        finally
        {
            _gate.Release();
        }

        if (pendingShutdown is not null)
        {
            try
            {
                await pendingShutdown.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _gate.Dispose();
    }

    private void ScheduleSessionCompletion()
    {
        var cancellation = new CancellationTokenSource();
        _shutdownCancellation = cancellation;
        _shutdownTask = CompleteSessionAfterDelayAsync(cancellation);
    }

    private async Task CompleteSessionAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_shutdownDelay, cancellation.Token).ConfigureAwait(false);
            await _gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_shutdownCancellation, cancellation)
                    || _activeGameIds.Count != 0
                    || !_sessionActive)
                {
                    return;
                }

                bool recorderRunning = false;
                if (_recorder is not null)
                {
                    try
                    {
                        recorderRunning = await _processController
                            .IsRunningAsync(_recorder.ProcessName, cancellation.Token)
                            .ConfigureAwait(false);

                        if (_closeEnabled && recorderRunning)
                        {
                            await _processController
                                .StopAsync(_recorder.ProcessName, cancellation.Token)
                                .ConfigureAwait(false);
                            recorderRunning = false;
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                                                      or IOException
                                                      or UnauthorizedAccessException)
                    {
                        ActionFailed?.Invoke(this, new RecorderActionFailedEventArgs("stop", exception));
                    }
                }

                ResetSession();
                SetStatus(recorderRunning ? RecorderLifecycleStatus.Running : RecorderLifecycleStatus.Idle);
                _shutdownCancellation = null;
                _shutdownTask = null;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private bool IsConfiguredRecorderRunning(IReadOnlySet<string> runningProcessNames)
    {
        return _recorder is not null
            && runningProcessNames.Contains(
                ConfigurationNormalizer.NormalizeProcessName(_recorder.ProcessName));
    }

    private void CancelPendingShutdown()
    {
        CancellationTokenSource? cancellation = _shutdownCancellation;
        _shutdownCancellation = null;
        _shutdownTask = null;
        cancellation?.Cancel();
    }

    private void ResetSession()
    {
        _sessionActive = false;
        _launchAttempted = false;
        _recorderObserved = false;
        _suppressed = false;
        _activeGameIds.Clear();
    }

    private void SetStatus(RecorderLifecycleStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, new RecorderStatusChangedEventArgs(status));
    }

    private static bool IsSameRecorder(RecorderConfiguration? left, RecorderConfiguration? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
