namespace Apollo.Core.Configuration;

public sealed class BehaviorSettings
{
    public bool MonitoringEnabled { get; set; }

    public bool LaunchRecorderWithGames { get; set; } = true;

    public bool CloseRecorderWhenGamesExit { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }
}
