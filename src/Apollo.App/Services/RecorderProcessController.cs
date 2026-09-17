using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Apollo.Core.Configuration;
using Apollo.Core.Recorder;

namespace Apollo.Services;

internal sealed class RecorderProcessController : IRecorderProcessController
{
    public ValueTask<bool> IsRunningAsync(string processName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process[] processes = GetProcesses(processName);
        try
        {
            return ValueTask.FromResult(processes.Length > 0);
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    public async ValueTask<bool> StartIfNotRunningAsync(
        RecorderConfiguration recorder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        if (await IsRunningAsync(recorder.ProcessName, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        string executablePath = Path.GetFullPath(recorder.ExecutablePath);
        if (!File.Exists(executablePath)
            || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The configured recorder executable could not be found.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            UseShellExecute = true,
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception
                                          or InvalidOperationException
                                          or IOException)
        {
            throw new InvalidOperationException("Windows could not start the configured recorder.", exception);
        }
    }

    public ValueTask StopAsync(string processName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process[] processes = GetProcesses(processName);
        try
        {
            foreach (Process process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    process.Kill(entireProcessTree: false);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and termination.
                }
                catch (Exception exception) when (exception is Win32Exception
                                                  or NotSupportedException)
                {
                    throw new InvalidOperationException(
                        "Windows could not stop the configured recorder process.",
                        exception);
                }
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    private static Process[] GetProcesses(string processName)
    {
        string normalizedName = ConfigurationNormalizer.NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return [];
        }

        try
        {
            return Process.GetProcessesByName(normalizedName);
        }
        catch (Exception exception) when (exception is Win32Exception
                                          or InvalidOperationException
                                          or NotSupportedException)
        {
            throw new InvalidOperationException("Windows could not inspect the recorder process.", exception);
        }
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (Process process in processes)
        {
            process.Dispose();
        }
    }
}
