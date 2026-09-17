namespace Apollo.Core.Recorder;

public sealed class RecorderActionFailedEventArgs(string action, Exception exception) : EventArgs
{
    public string Action { get; } = action;

    public Exception Exception { get; } = exception;
}
