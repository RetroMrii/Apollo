using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using Apollo.Core.Configuration;

namespace Apollo.Services;

internal sealed class RunningApplicationService
{
    public Task<IReadOnlyList<RunningApplicationInfo>> GetRunningApplicationsAsync(
        CancellationToken cancellationToken = default)
        => Task.Run(() => Enumerate(cancellationToken), cancellationToken);

    private static IReadOnlyList<RunningApplicationInfo> Enumerate(CancellationToken cancellationToken)
    {
        var applications = new Dictionary<string, RunningApplicationInfo>(StringComparer.OrdinalIgnoreCase);
        Process[] processes = Process.GetProcesses();

        try
        {
            foreach (Process process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                string processName;
                string windowTitle;
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    processName = ConfigurationNormalizer.NormalizeProcessName(process.ProcessName);
                    windowTitle = process.MainWindowTitle.Trim();
                    if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(windowTitle))
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                  or Win32Exception
                                                  or NotSupportedException)
                {
                    continue;
                }

                string executablePath = TryGetExecutablePath(process);
                string displayName = GetDisplayName(executablePath, processName, windowTitle);
                var candidate = new RunningApplicationInfo(
                    displayName,
                    processName,
                    executablePath,
                    ApplicationIconService.TryGetIcon(executablePath));

                if (!applications.TryGetValue(processName, out RunningApplicationInfo? existing)
                    || string.IsNullOrWhiteSpace(existing.ExecutablePath) && !string.IsNullOrWhiteSpace(executablePath))
                {
                    applications[processName] = candidate;
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        return applications.Values
            .OrderBy(application => application.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(application => application.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or NotSupportedException
                                          or SecurityException)
        {
            return string.Empty;
        }
    }

    private static string GetDisplayName(string executablePath, string processName, string windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                string friendlyName = ExecutableDetails.FromPath(executablePath).DisplayName;
                if (!string.Equals(friendlyName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return friendlyName;
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                                              or IOException
                                              or UnauthorizedAccessException)
            {
            }
        }

        return string.IsNullOrWhiteSpace(windowTitle) ? processName : windowTitle;
    }
}
