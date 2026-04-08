using System.Diagnostics;
using System.Text.Json;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class ZapretProcessService
{
    public async Task StartAsync(ZapretInstallation installation, ConfigProfile profile, bool silentMode = false)
    {
        await EnableTcpTimestampsAsync();

        if (silentMode)
        {
            using var process = await ZapretBatchLauncher.StartAndAttachWinwsAsync(
                installation,
                profile,
                TimeSpan.FromSeconds(10));
            await Task.Delay(350);
            if (!process.HasExited)
            {
                return;
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = installation.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(profile.FilePath);

        Process.Start(startInfo);
    }

    public async Task StopAsync(ZapretInstallation? installation)
    {
        foreach (var process in Process.GetProcessesByName("winws"))
        {
            try
            {
                if (!BelongsToInstallation(process, installation))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var processId in GetRelatedCmdProcessIds(installation))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
        }
    }

    public async Task StopCheckUpdatesShellsAsync()
    {
        foreach (var processId in GetCheckUpdatesShellProcessIds())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
        }
    }

    public async Task StopProcessesUsingInstallationAsync(ZapretInstallation? installation)
    {
        if (installation is null)
        {
            return;
        }

        foreach (var processId in GetProcessIdsUsingInstallationRoot(installation.RootPath))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
        }
    }

    public int GetRunningProcessCount(ZapretInstallation? installation)
    {
        var count = 0;
        foreach (var process in Process.GetProcessesByName("winws"))
        {
            try
            {
                if (BelongsToInstallation(process, installation))
                {
                    count++;
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return count;
    }

    public int GetProcessCountUsingInstallation(ZapretInstallation? installation)
    {
        if (installation is null)
        {
            return 0;
        }

        return GetProcessIdsUsingInstallationRoot(installation.RootPath).Length;
    }

    private static bool BelongsToInstallation(Process process, ZapretInstallation? installation)
    {
        if (installation is null)
        {
            return true;
        }

        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ||
                   path.StartsWith(installation.RootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static async Task EnableTcpTimestampsAsync()
    {
        try
        {
            await RunHiddenAsync("netsh.exe", "interface tcp set global timestamps=enabled");
        }
        catch
        {
        }
    }

    private static async Task<string> RunHiddenAsync(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}{Environment.NewLine}{stderr}";
    }

    private static IEnumerable<int> GetRelatedCmdProcessIds(ZapretInstallation? installation)
    {
        if (installation is null)
        {
            return [];
        }

        try
        {
            var snapshots = GetProcessSnapshots("cmd.exe", "powershell.exe");
            var seedIds = snapshots
                .Where(snapshot =>
                    snapshot.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.CommandLine.Contains(installation.RootPath, StringComparison.OrdinalIgnoreCase))
                .Select(snapshot => snapshot.ProcessId)
                .ToHashSet();

            return ExpandDescendantProcessIds(snapshots, seedIds)
                .Where(processId => processId != Environment.ProcessId)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<int> GetCheckUpdatesShellProcessIds()
    {
        try
        {
            var snapshots = GetProcessSnapshots("cmd.exe", "powershell.exe");
            var seedIds = snapshots
                .Where(snapshot => IsCheckUpdatesShell(snapshot.CommandLine))
                .Select(snapshot => snapshot.ProcessId)
                .ToHashSet();

            return ExpandDescendantProcessIds(snapshots, seedIds)
                .Where(processId => processId != Environment.ProcessId)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static int[] GetProcessIdsUsingInstallationRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return [];
        }

        try
        {
            var snapshots = GetProcessSnapshots();
            var seedIds = snapshots
                .Where(snapshot => ReferencesInstallationRoot(snapshot.ExecutablePath, snapshot.CommandLine, rootPath))
                .Select(snapshot => snapshot.ProcessId)
                .ToHashSet();

            return ExpandDescendantProcessIds(snapshots, seedIds)
                .Where(processId => processId != Environment.ProcessId)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ProcessSnapshot> GetProcessSnapshots(params string[] names)
    {
        var filter = names is { Length: > 0 }
            ? string.Join(" or ", names.Select(name => $"Name = '{name.Replace("'", "''")}'"))
            : string.Empty;
        var command = string.IsNullOrWhiteSpace(filter)
            ? "Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Depth 2 -Compress"
            : $"Get-CimInstance Win32_Process -Filter \\\"{filter}\\\" | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Depth 2 -Compress";

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
        try
        {
            if (output.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<ProcessSnapshot>>(output, options) ?? [];
            }

            var item = JsonSerializer.Deserialize<ProcessSnapshot>(output, options);
            return item is null ? [] : [item];
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<int> ExpandDescendantProcessIds(IReadOnlyList<ProcessSnapshot> snapshots, HashSet<int> seedIds)
    {
        if (seedIds.Count == 0)
        {
            return [];
        }

        var childrenByParentId = snapshots
            .GroupBy(snapshot => snapshot.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.ProcessId).ToArray());

        var result = new HashSet<int>(seedIds);
        var queue = new Queue<int>(seedIds);
        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!childrenByParentId.TryGetValue(parentId, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (result.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        return result;
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

    private sealed class ProcessSnapshot
    {
        public int ProcessId { get; init; }
        public int ParentProcessId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string CommandLine { get; init; } = string.Empty;
    }
}
