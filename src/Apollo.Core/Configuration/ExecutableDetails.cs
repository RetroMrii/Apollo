namespace Apollo.Core.Configuration;

public sealed record ExecutableDetails(string DisplayName, string ExecutablePath, string ProcessName)
{
    public static ExecutableDetails FromPath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selected file must be a Windows executable.", nameof(executablePath));
        }

        var processName = Path.GetFileNameWithoutExtension(fullPath);
        var displayName = TryGetFriendlyName(fullPath) ?? processName;
        return new ExecutableDetails(displayName, fullPath, processName);
    }

    private static string? TryGetFriendlyName(string path)
    {
        try
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var candidate = version.FileDescription ?? version.ProductName;
            return string.IsNullOrWhiteSpace(candidate) ? null : candidate.Trim();
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException
                                          or IOException)
        {
            return null;
        }
    }
}
