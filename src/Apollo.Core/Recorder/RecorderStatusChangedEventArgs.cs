namespace Apollo.Core.Recorder;

public sealed class RecorderStatusChangedEventArgs(RecorderLifecycleStatus status) : EventArgs
{
    public RecorderLifecycleStatus Status { get; } = status;
}
