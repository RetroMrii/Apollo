using Apollo.Core.Configuration;

namespace Apollo.Core.Monitoring;

public sealed class ProcessMonitorService : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly IProcessSnapshotProvider _snapshotProvider;
    private readonly TimeSpan _interval;
    private Dictionary<Guid, string> _monitoredGames = [];
    private HashSet<Guid> _activeGameIds = [];
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;

    public ProcessMonitorService(IProcessSnapshotProvider snapshotProvider, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The monitoring interval must be positive.");
        }

        _snapshotProvider = snapshotProvider;
        _interval = interval;
    }

    public event EventHandler<ActiveGamesChangedEventArgs>? ActiveGamesChanged;

    public event EventHandler<ProcessSnapshotEventArgs>? SnapshotTaken;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _monitorCancellation is not null;
            }
        }
    }

    public void UpdateGames(IEnumerable<GameConfiguration> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        var targets = new Dictionary<Guid, string>();
        foreach (GameConfiguration game in games)
        {
            string processName = ConfigurationNormalizer.NormalizeProcessName(game.ProcessName);
            if (game.IsEnabled && game.Id != Guid.Empty && !string.IsNullOrWhiteSpace(processName))
            {
                targets[game.Id] = processName;
            }
        }

        lock (_stateLock)
        {
            _monitoredGames = targets;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        CancellationToken monitorToken;
        lock (_stateLock)
        {
            if (_monitorCancellation is not null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _monitorCancellation = new CancellationTokenSource();
            monitorToken = _monitorCancellation.Token;
        }

        try
        {
            await RefreshAsync(monitorToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (monitorToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or UnauthorizedAccessException)
        {
            // A transient initial snapshot must not disable later monitoring retries.
        }

        lock (_stateLock)
        {
            if (_monitorCancellation is not null && _monitorCancellation.Token == monitorToken)
            {
                _monitorTask = MonitorAsync(monitorToken);
            }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<Guid, string> targets;
            CancellationTokenSource? monitorGeneration;
            lock (_stateLock)
            {
                monitorGeneration = _monitorCancellation;
                if (monitorGeneration is null)
                {
                    return;
                }

                targets = new Dictionary<Guid, string>(_monitoredGames);
            }

            IReadOnlySet<string> runningProcessNames =
                await _snapshotProvider.GetRunningProcessNamesAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var activeGameIds = targets
                .Where(target => runningProcessNames.Contains(target.Value))
                .Select(target => target.Key)
                .ToHashSet();

            lock (_stateLock)
            {
                if (!ReferenceEquals(_monitorCancellation, monitorGeneration))
                {
                    return;
                }
            }

            PublishIfChanged(activeGameIds);
            SnapshotTaken?.Invoke(
                this,
                new ProcessSnapshotEventArgs(
                    new HashSet<Guid>(activeGameIds),
                    new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            _scanLock.Release();
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? monitorTask;
        lock (_stateLock)
        {
            cancellation = _monitorCancellation;
            monitorTask = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            if (monitorTask is not null)
            {
                try
                {
                    await monitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }

            cancellation.Dispose();
        }

        await _scanLock.WaitAsync().ConfigureAwait(false);
        _scanLock.Release();

        lock (_stateLock)
        {
            if (_monitorCancellation is not null)
            {
                return;
            }
        }

        PublishIfChanged([]);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _scanLock.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException
                                              or InvalidOperationException
                                              or UnauthorizedAccessException)
            {
                // Process snapshots are transient. A later interval performs a clean retry.
            }
        }
    }

    private void PublishIfChanged(HashSet<Guid> activeGameIds)
    {
        lock (_stateLock)
        {
            if (_activeGameIds.SetEquals(activeGameIds))
            {
                return;
            }

            _activeGameIds = activeGameIds;
        }

        ActiveGamesChanged?.Invoke(
            this,
            new ActiveGamesChangedEventArgs(new HashSet<Guid>(activeGameIds)));
    }
}
