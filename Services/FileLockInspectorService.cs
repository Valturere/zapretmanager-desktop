using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ZapretManager.Services;

public sealed class FileLockInspectorService
{
    public IReadOnlyList<LockingProcessInfo> FindLockingProcesses(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return [];
        }

        if (File.Exists(rootPath))
        {
            return FindLockingProcessesForResources([Path.GetFullPath(rootPath)]);
        }

        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        var resources = BuildProbeResources(rootPath);
        if (resources.Count == 0)
        {
            return [];
        }

        var result = new Dictionary<int, LockingProcessInfo>();
        foreach (var resource in resources)
        {
            foreach (var process in FindLockingProcessesForResources([resource]))
            {
                result[process.ProcessId] = process;
            }
        }

        return result.Values.ToArray();
    }

    public async Task<IReadOnlyList<LockingProcessInfo>> CloseSafeProcessesAsync(IEnumerable<LockingProcessInfo> processes, string rootPath)
    {
        var closed = new List<LockingProcessInfo>();
        foreach (var process in processes.Where(item => CanAutoClose(item, rootPath)))
        {
            try
            {
                using var nativeProcess = Process.GetProcessById(process.ProcessId);
                nativeProcess.Kill(entireProcessTree: true);
                await nativeProcess.WaitForExitAsync();
                closed.Add(process);
            }
            catch
            {
            }
        }

        return closed;
    }

    public string BuildDisplaySummary(IEnumerable<LockingProcessInfo> processes)
    {
        return string.Join(", ",
            processes
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key));
    }

    private static List<string> BuildProbeResources(string rootPath)
    {
        var resources = new List<string>();
        void Add(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                (File.Exists(path) || Directory.Exists(path)) &&
                !resources.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                resources.Add(path);
            }
        }

        Add(rootPath);
        Add(Path.Combine(rootPath, "service.bat"));
        Add(Path.Combine(rootPath, "bin", "winws.exe"));
        Add(Path.Combine(rootPath, "utils", "check_updates.enabled"));

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.bat", SearchOption.TopDirectoryOnly))
        {
            Add(file);
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).Take(256))
        {
            Add(file);
        }

        return resources;
    }

    private static IReadOnlyList<LockingProcessInfo> FindLockingProcessesForResources(IReadOnlyCollection<string> resources)
    {
        if (resources.Count == 0)
        {
            return [];
        }

        var sessionKey = Guid.NewGuid().ToString("N");
        var result = RmStartSession(out var sessionHandle, 0, sessionKey);
        if (result != ErrorSuccess)
        {
            return [];
        }

        try
        {
            result = RmRegisterResources(
                sessionHandle,
                (uint)resources.Count,
                resources.ToArray(),
                0,
                null,
                0,
                null);
            if (result != ErrorSuccess)
            {
                return [];
            }

            uint processInfoNeeded = 0;
            uint processInfoCount = 0;
            uint rebootReasons = 0;

            result = RmGetList(sessionHandle, out processInfoNeeded, ref processInfoCount, null, ref rebootReasons);
            if (processInfoNeeded == 0)
            {
                return [];
            }

            if (result is not (ErrorMoreData or ErrorSuccess))
            {
                return [];
            }

            var processInfos = new RmProcessInfo[processInfoNeeded];
            processInfoCount = processInfoNeeded;
            result = RmGetList(sessionHandle, out processInfoNeeded, ref processInfoCount, processInfos, ref rebootReasons);
            if (result != ErrorSuccess)
            {
                return [];
            }

            var detailsById = GetProcessDetails(processInfos
                .Take((int)processInfoCount)
                .Select(item => item.Process.dwProcessId));

            return processInfos
                .Take((int)processInfoCount)
                .Select(item => CreateLockingProcessInfo(item, detailsById))
                .Where(item => item.ProcessId != Environment.ProcessId)
                .DistinctBy(item => item.ProcessId)
                .ToArray();
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    private static Dictionary<int, ProcessDetails> GetProcessDetails(IEnumerable<int> processIds)
    {
        var ids = processIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        try
        {
            var filter = string.Join(" or ", ids.Select(id => $"ProcessId = {id}"));
            var command = $"Get-CimInstance Win32_Process -Filter \\\"{filter}\\\" | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Depth 2 -Compress";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (string.IsNullOrWhiteSpace(output))
            {
                return [];
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var details = output.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<List<ProcessDetails>>(output, options) ?? []
                : JsonSerializer.Deserialize<ProcessDetails>(output, options) is { } item ? [item] : [];

            return details.ToDictionary(item => item.ProcessId);
        }
        catch
        {
            return [];
        }
    }

    private static LockingProcessInfo CreateLockingProcessInfo(RmProcessInfo processInfo, IReadOnlyDictionary<int, ProcessDetails> detailsById)
    {
        var processId = processInfo.Process.dwProcessId;
        detailsById.TryGetValue(processId, out var details);

        return new LockingProcessInfo(
            processId,
            string.IsNullOrWhiteSpace(details?.Name) ? processInfo.AppName : details.Name,
            details?.ExecutablePath ?? string.Empty,
            details?.CommandLine ?? string.Empty,
            details?.ParentProcessId ?? 0);
    }

    private static bool CanAutoClose(LockingProcessInfo process, string rootPath)
    {
        if (process.ProcessId == Environment.ProcessId)
        {
            return false;
        }

        var name = process.Name ?? string.Empty;
        var isShell = name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);

        if (!isShell)
        {
            return false;
        }

        return ReferencesInstallationRoot(process.ExecutablePath, process.CommandLine, rootPath) ||
               IsCheckUpdatesShell(process.CommandLine);
    }

    private static bool ReferencesInstallationRoot(string executablePath, string commandLine, string rootPath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                if (Path.GetFullPath(executablePath).StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return !string.IsNullOrWhiteSpace(commandLine) &&
               commandLine.Contains(rootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCheckUpdatesShell(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        return commandLine.Contains("check_updates", StringComparison.OrdinalIgnoreCase) ||
               commandLine.Contains("service check_updates", StringComparison.OrdinalIgnoreCase) ||
               (commandLine.Contains("Flowseal/zapret-discord-youtube", StringComparison.OrdinalIgnoreCase) &&
                (commandLine.Contains("version.txt", StringComparison.OrdinalIgnoreCase) ||
                 commandLine.Contains("releases/latest", StringComparison.OrdinalIgnoreCase) ||
                 commandLine.Contains("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase)));
    }

    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        [In] RmUniqueProcess[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RmProcessInfo[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string ServiceShortName;

        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    private sealed class ProcessDetails
    {
        public int ProcessId { get; init; }
        public int ParentProcessId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string CommandLine { get; init; } = string.Empty;
    }
}

public sealed record LockingProcessInfo(
    int ProcessId,
    string Name,
    string ExecutablePath,
    string CommandLine,
    int ParentProcessId);
