namespace Apollo.Core.Monitoring;

public sealed class ProcessSnapshotEventArgs(
    IReadOnlySet<Guid> activeGameIds,
    IReadOnlySet<string> runningProcessNames) : EventArgs
{
    public IReadOnlySet<Guid> ActiveGameIds { get; } = activeGameIds;

    public IReadOnlySet<string> RunningProcessNames { get; } = runningProcessNames;
}
