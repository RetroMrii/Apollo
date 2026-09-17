using System.Windows.Media;

namespace Apollo.Services;

internal sealed record RunningApplicationInfo(
    string DisplayName,
    string ProcessName,
    string ExecutablePath,
    ImageSource? Icon)
{
    public string DisplayProcessName => $"{ProcessName}.exe";

    public string DisplayPath => string.IsNullOrWhiteSpace(ExecutablePath)
        ? "Executable path unavailable"
        : ExecutablePath;
}
