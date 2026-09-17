using Apollo.Core.Configuration;

namespace Apollo.Core.Recorder;

public interface IRecorderProcessController
{
    ValueTask<bool> IsRunningAsync(string processName, CancellationToken cancellationToken);

    ValueTask<bool> StartIfNotRunningAsync(
        RecorderConfiguration recorder,
        CancellationToken cancellationToken);

    ValueTask StopAsync(string processName, CancellationToken cancellationToken);
}
