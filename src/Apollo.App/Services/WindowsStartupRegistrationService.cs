using System.IO;
using Microsoft.Win32;

namespace Apollo.Services;

internal sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName;
    private readonly string? _startupCommand;

    public WindowsStartupRegistrationService()
        : this(Environment.ProcessPath, "Apollo")
    {
    }

    internal WindowsStartupRegistrationService(string? executablePath, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            _startupCommand = $"\"{Path.GetFullPath(executablePath)}\"";
        }
    }

    public bool IsSupported => OperatingSystem.IsWindows() && _startupCommand is not null;

    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(_valueName) is string value
            && string.Equals(value, _startupCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Windows startup registration is not available.");
        }

        if (enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("The current-user startup registry key could not be opened.");
            key.SetValue(_valueName, _startupCommand!, RegistryValueKind.String);
            return;
        }

        using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existingKey?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
