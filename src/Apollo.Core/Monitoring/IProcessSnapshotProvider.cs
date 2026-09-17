namespace Apollo.Core.Monitoring;

public interface IProcessSnapshotProvider
{
    ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken);
}
