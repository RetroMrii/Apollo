using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Apollo.Core.Monitoring;

namespace Apollo.Services;

internal sealed class SystemProcessSnapshotProvider : IProcessSnapshotProvider
{
    public ValueTask<IReadOnlySet<string>> GetRunningProcessNamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or NotSupportedException)
        {
            throw new IOException("Windows could not enumerate running processes.", exception);
        }

        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    processNames.Add(process.ProcessName);
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                  or Win32Exception
                                                  or NotSupportedException)
                {
                    // Processes can exit or deny access while a snapshot is being read.
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

        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlySet<string>>(processNames);
    }
}
