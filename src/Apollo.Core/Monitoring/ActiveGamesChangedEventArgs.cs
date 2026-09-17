namespace Apollo.Core.Monitoring;

public sealed class ActiveGamesChangedEventArgs(IReadOnlySet<Guid> activeGameIds) : EventArgs
{
    public IReadOnlySet<Guid> ActiveGameIds { get; } = activeGameIds;
}
