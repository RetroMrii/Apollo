namespace Apollo.Core.Configuration;

public sealed class GameConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DisplayName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
}
