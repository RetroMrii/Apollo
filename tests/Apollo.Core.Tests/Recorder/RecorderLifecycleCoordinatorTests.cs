using Apollo.Core.Configuration;
using Apollo.Core.Recorder;

namespace Apollo.Core.Tests.Recorder;

[TestClass]
public sealed class RecorderLifecycleCoordinatorTests
{
    private static readonly Guid GameA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GameB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [TestMethod]
    public async Task ActiveGame_StartsRecorderOnce_ThenManualCloseSuppressesRestart()
    {
        var controller = new FakeRecorderProcessController();
        await using var coordinator = CreateCoordinator(controller);
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());
        controller.IsRunning = false;
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());

        Assert.AreEqual(1, controller.StartCount);
        Assert.IsTrue(coordinator.IsSuppressedForCurrentSession);
        Assert.AreEqual(RecorderLifecycleStatus.Suppressed, coordinator.Status);
    }

    [TestMethod]
    public async Task FinalGameStops_WaitsThenRechecksAndClosesRecorder()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(20));
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await controller.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, controller.StartCount);
        Assert.AreEqual(1, controller.StopCount);
        Assert.AreEqual(RecorderLifecycleStatus.Idle, coordinator.Status);
    }

    [TestMethod]
    public async Task GameReappearsDuringShutdownDelay_CancelsRecorderClose()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(80));
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await Task.Delay(15);
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await Task.Delay(120);

        Assert.AreEqual(0, controller.StopCount);
        Assert.AreEqual(RecorderLifecycleStatus.Running, coordinator.Status);
    }

    [TestMethod]
    public async Task MultipleGames_KeepRecorderUntilEveryGameStops()
    {
        var controller = new FakeRecorderProcessController();
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(20));
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA, GameB), ProcessNames());
        await coordinator.HandleSnapshotAsync(GameIds(GameB), ProcessNames("Medal"));
        await Task.Delay(40);

        Assert.AreEqual(1, controller.StartCount);
        Assert.AreEqual(0, controller.StopCount);

        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await controller.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, controller.StopCount);
    }

    [TestMethod]
    public async Task RecorderAlreadyRunning_IsNotDuplicatedAndIsClosedAtSessionEnd()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.Zero);
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await controller.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, controller.StartCount);
        Assert.AreEqual(1, controller.StopCount);
    }

    [TestMethod]
    public async Task SuppressionResetsAfterSessionCompletes()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(20));
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        controller.IsRunning = false;
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames());
        await Task.Delay(60);
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());

        Assert.AreEqual(1, controller.StartCount);
        Assert.IsFalse(coordinator.IsSuppressedForCurrentSession);
        Assert.AreEqual(RecorderLifecycleStatus.Running, coordinator.Status);
    }

    [TestMethod]
    public async Task DisabledLaunchSetting_DoesNotStartRecorder()
    {
        var controller = new FakeRecorderProcessController();
        await using var coordinator = CreateCoordinator(controller);
        await ConfigureAsync(coordinator, launchEnabled: false);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());

        Assert.AreEqual(0, controller.StartCount);
        Assert.AreEqual(RecorderLifecycleStatus.Idle, coordinator.Status);
    }

    [TestMethod]
    public async Task DisabledCloseSetting_LeavesRecorderRunning()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(20));
        await ConfigureAsync(coordinator, closeEnabled: false);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await Task.Delay(60);

        Assert.AreEqual(0, controller.StopCount);
        Assert.AreEqual(RecorderLifecycleStatus.Running, coordinator.Status);
    }

    [TestMethod]
    public async Task LaunchFailure_IsReportedAndNotRetriedDuringSession()
    {
        var controller = new FakeRecorderProcessController { StartException = new InvalidOperationException("Failed") };
        await using var coordinator = CreateCoordinator(controller);
        await ConfigureAsync(coordinator);
        int failureCount = 0;
        coordinator.ActionFailed += (_, args) =>
        {
            Assert.AreEqual("start", args.Action);
            failureCount++;
        };

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());

        Assert.AreEqual(1, controller.StartCount);
        Assert.AreEqual(1, failureCount);
        Assert.IsTrue(coordinator.IsSuppressedForCurrentSession);
    }

    [TestMethod]
    public async Task RepeatedSessions_LaunchAndCloseExactlyOncePerSession()
    {
        var controller = new FakeRecorderProcessController();
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(1));
        await ConfigureAsync(coordinator);

        const int sessionCount = 50;
        for (int session = 1; session <= sessionCount; session++)
        {
            await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames());
            await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
            await WaitUntilAsync(() => controller.StopCount == session);
        }

        Assert.AreEqual(sessionCount, controller.StartCount);
        Assert.AreEqual(sessionCount, controller.StopCount);
        Assert.AreEqual(RecorderLifecycleStatus.Idle, coordinator.Status);
    }

    [TestMethod]
    public async Task RapidGameFlapping_CancelsEveryPendingCloseUntilFinalExit()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        await using var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(20));
        await ConfigureAsync(coordinator);

        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        for (int iteration = 0; iteration < 50; iteration++)
        {
            await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
            await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        }

        await Task.Delay(40);
        Assert.AreEqual(0, controller.StopCount);

        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));
        await WaitUntilAsync(() => controller.StopCount == 1);
        Assert.AreEqual(1, controller.StopCount);
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsPendingRecorderClose()
    {
        var controller = new FakeRecorderProcessController { IsRunning = true };
        var coordinator = CreateCoordinator(controller, TimeSpan.FromMilliseconds(40));
        await ConfigureAsync(coordinator);
        await coordinator.HandleSnapshotAsync(GameIds(GameA), ProcessNames("Medal"));
        await coordinator.HandleSnapshotAsync(GameIds(), ProcessNames("Medal"));

        await coordinator.DisposeAsync();
        await Task.Delay(60);

        Assert.AreEqual(0, controller.StopCount);
    }

    private static RecorderLifecycleCoordinator CreateCoordinator(
        FakeRecorderProcessController controller,
        TimeSpan? delay = null)
        => new(controller, delay ?? TimeSpan.FromSeconds(5));

    private static Task ConfigureAsync(
        RecorderLifecycleCoordinator coordinator,
        bool launchEnabled = true,
        bool closeEnabled = true)
        => coordinator.UpdateConfigurationAsync(
            new RecorderConfiguration
            {
                DisplayName = "Medal",
                ExecutablePath = @"C:\Apps\Medal.exe",
                ProcessName = "Medal",
            },
            launchEnabled,
            closeEnabled);

    private static IReadOnlySet<Guid> GameIds(params Guid[] ids) => new HashSet<Guid>(ids);

    private static IReadOnlySet<string> ProcessNames(params string[] names)
        => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(2, cancellation.Token);
        }
    }

    private sealed class FakeRecorderProcessController : IRecorderProcessController
    {
        public bool IsRunning { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Exception? StartException { get; init; }

        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<bool> IsRunningAsync(string processName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(IsRunning);
        }

        public ValueTask<bool> StartIfNotRunningAsync(
            RecorderConfiguration recorder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }

            IsRunning = true;
            return ValueTask.FromResult(true);
        }

        public ValueTask StopAsync(string processName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            IsRunning = false;
            Stopped.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
