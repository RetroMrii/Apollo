using Apollo.Core.Configuration;
using Apollo.Core.Monitoring;

namespace Apollo.Core.Tests.Monitoring;

[TestClass]
public sealed class ProcessMonitorServiceTests
{
    [TestMethod]
    public async Task StartAsync_ReconcilesEnabledGamesImmediatelyAndCaseInsensitively()
    {
        Guid activeId = Guid.NewGuid();
        Guid disabledId = Guid.NewGuid();
        var provider = new MutableSnapshotProvider("TESTGAME", "DisabledGame");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = activeId, ProcessName = "TestGame.exe", IsEnabled = true },
            new GameConfiguration { Id = disabledId, ProcessName = "DisabledGame", IsEnabled = false },
        ]);

        await monitor.StartAsync();

        Assert.IsTrue(monitor.IsRunning);
        Assert.HasCount(1, activeIds);
        Assert.Contains(activeId, activeIds);
        Assert.DoesNotContain(disabledId, activeIds);
    }

    [TestMethod]
    public async Task RefreshAsync_PublishesOnlyWhenActiveSetChanges()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new MutableSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        int notificationCount = 0;
        monitor.ActiveGamesChanged += (_, _) => notificationCount++;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        await monitor.StartAsync();
        await monitor.RefreshAsync();
        provider.SetProcesses();
        await monitor.RefreshAsync();

        Assert.AreEqual(2, notificationCount);
    }

    [TestMethod]
    public async Task StopAsync_ClearsActiveGamesAndStopsTimer()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new MutableSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        await monitor.StartAsync();
        await monitor.StopAsync();

        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(activeIds);
    }

    [TestMethod]
    public async Task StartAsync_WhenInitialSnapshotFails_RemainsAvailableForRetry()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new FailingOnceSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        await monitor.StartAsync();
        await monitor.RefreshAsync();

        Assert.IsTrue(monitor.IsRunning);
        Assert.Contains(gameId, activeIds);
    }

    [TestMethod]
    public async Task SnapshotFailure_PreservesLastKnownActiveGames()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new FailingAfterSuccessSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        await monitor.StartAsync();
        await Assert.ThrowsAsync<IOException>(() => monitor.RefreshAsync());

        Assert.Contains(gameId, activeIds);
        Assert.IsTrue(monitor.IsRunning);
    }

    [TestMethod]
    public async Task StopAsync_DuringInitialSnapshot_DoesNotPublishStaleActivity()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new BlockingSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        Task startTask = monitor.StartAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task stopTask = monitor.StopAsync();
        provider.Release.TrySetResult();
        await Task.WhenAll(startTask, stopTask);

        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(activeIds);
    }

    [TestMethod]
    public async Task RepeatedStartStop_LeavesNoWorkerOrActiveGames()
    {
        Guid gameId = Guid.NewGuid();
        var provider = new MutableSnapshotProvider("Game");
        await using var monitor = new ProcessMonitorService(provider, TimeSpan.FromHours(1));
        IReadOnlySet<Guid> activeIds = new HashSet<Guid>();
        monitor.ActiveGamesChanged += (_, args) => activeIds = args.ActiveGameIds;
        monitor.UpdateGames(
        [
            new GameConfiguration { Id = gameId, ProcessName = "Game", IsEnabled = true },
        ]);

        for (int iteration = 0; iteration < 100; iteration++)
        {
            await monitor.StartAsync();
            await monitor.StopAsync();
        }

        Assert.IsFalse(monitor.IsRunning);
        Assert.IsEmpty(activeIds);
    }

    private sealed class MutableSnapshotProvider(params string[] processNames) : IProcessSnapshotProvider
    {
        private IReadOnlySet<string> _processNames = CreateSet(processNames);

        public ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_processNames);
        }

        public void SetProcesses(params string[] processNames) => _processNames = CreateSet(processNames);

        private static IReadOnlySet<string> CreateSet(IEnumerable<string> processNames)
            => new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FailingOnceSnapshotProvider(string processName) : IProcessSnapshotProvider
    {
        private bool _hasFailed;

        public ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new IOException("Transient snapshot failure.");
            }

            IReadOnlySet<string> processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                processName,
            };
            return ValueTask.FromResult(processNames);
        }
    }

    private sealed class BlockingSnapshotProvider(string processName) : IProcessSnapshotProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { processName };
        }
    }

    private sealed class FailingAfterSuccessSnapshotProvider(string processName) : IProcessSnapshotProvider
    {
        private int _callCount;

        public ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _callCount) > 1)
            {
                throw new IOException("Transient process enumeration failure.");
            }

            IReadOnlySet<string> processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                processName,
            };
            return ValueTask.FromResult(processes);
        }
    }
}
