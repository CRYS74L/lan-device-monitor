using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LanDeviceMonitor.Services;

internal readonly record struct EmergencyProcessDescriptor(
    int Id,
    string? ExecutablePath);

internal static class EmergencyProcessTerminator
{
    internal static IReadOnlyList<int> BuildTerminationOrder(
        IEnumerable<EmergencyProcessDescriptor> processes,
        string expectedExecutablePath,
        int currentProcessId)
    {
        if (string.IsNullOrWhiteSpace(expectedExecutablePath))
        {
            return [];
        }

        var matchingIds = processes
            .Where(process =>
                process.Id > 0 &&
                IsMatchingExecutablePath(
                    process.ExecutablePath,
                    expectedExecutablePath))
            .Select(process => process.Id)
            .Distinct()
            .ToList();

        matchingIds.Sort((left, right) =>
        {
            var leftIsCurrent = left == currentProcessId;
            var rightIsCurrent = right == currentProcessId;
            return leftIsCurrent.CompareTo(rightIsCurrent);
        });
        return matchingIds;
    }

    internal static bool IsMatchingExecutablePath(
        string? candidatePath,
        string expectedExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) ||
            string.IsNullOrWhiteSpace(expectedExecutablePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(candidatePath),
                Path.GetFullPath(expectedExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static void TerminateAllInstances()
    {
        var expectedPath = Environment.ProcessPath;
        var currentProcessId = Environment.ProcessId;
        if (string.IsNullOrWhiteSpace(expectedPath))
        {
            TerminateCurrentProcess();
            return;
        }

        var processes = FindMatchingProcesses(expectedPath);
        var descriptors = processes
            .Select(process =>
            {
                try
                {
                    return new EmergencyProcessDescriptor(
                        process.Id,
                        process.MainModule?.FileName);
                }
                catch
                {
                    return new EmergencyProcessDescriptor(process.Id, null);
                }
            })
            .ToArray();
        var terminationOrder = BuildTerminationOrder(
            descriptors,
            expectedPath,
            currentProcessId);

        foreach (var processId in terminationOrder)
        {
            if (processId == currentProcessId)
            {
                continue;
            }

            var process = processes.FirstOrDefault(item => item.Id == processId);
            if (process is not null)
            {
                TryTerminate(process);
            }
        }

        foreach (var process in processes)
        {
            process.Dispose();
        }

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            currentProcess.Kill(entireProcessTree: true);
        }
        catch
        {
            TerminateCurrentProcess();
        }
    }

    private static List<Process> FindMatchingProcesses(string expectedPath)
    {
        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        var processes = new List<Process>();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return processes;
        }

        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (IsMatchingExecutablePath(
                        process.MainModule?.FileName,
                        expectedPath))
                {
                    processes.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return processes;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // A second process may exit between enumeration and Kill. The
            // current process is handled separately below.
        }
    }

    private static void TerminateCurrentProcess()
    {
        _ = TerminateProcess(GetCurrentProcess(), 1);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);
}
