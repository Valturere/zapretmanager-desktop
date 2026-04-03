using System.Diagnostics;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class ZapretProcessService
{
    private readonly ZapretConfigurationParser _parser = new();

    public async Task StartAsync(ZapretInstallation installation, ConfigProfile profile, bool silentMode = false)
    {
        await EnableTcpTimestampsAsync();

        if (silentMode)
        {
            _parser.EnsureUserFiles(installation);
            var arguments = _parser.BuildArguments(installation, profile);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installation.BinPath, "winws.exe"),
                Arguments = arguments,
                WorkingDirectory = installation.BinPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            await Task.Delay(350);
            if (process is not null && !process.HasExited)
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
        foreach (var processId in GetCheckUpdatesCmdProcessIds())
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
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_Process -Filter \\\"name = 'cmd.exe'\\\" | ForEach-Object { '{0}|{1}' -f $_.ProcessId, (($_.CommandLine ?? '') -replace '[\\r\\n]+', ' ') }\"",
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

            var rootPath = installation.RootPath;
            return output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('|', 2))
                .Where(parts => parts.Length == 2 &&
                                parts[1].Contains(rootPath, StringComparison.OrdinalIgnoreCase) &&
                                int.TryParse(parts[0], out _))
                .Select(parts => int.Parse(parts[0]))
                .Distinct()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<int> GetCheckUpdatesCmdProcessIds()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_Process -Filter \\\"name = 'cmd.exe'\\\" | ForEach-Object { '{0}|{1}' -f $_.ProcessId, (($_.CommandLine ?? '') -replace '[\\r\\n]+', ' ') }\"",
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

            return output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('|', 2))
                .Where(parts => parts.Length == 2 &&
                                parts[1].Contains("service check_updates soft", StringComparison.OrdinalIgnoreCase) &&
                                int.TryParse(parts[0], out _))
                .Select(parts => int.Parse(parts[0]))
                .Distinct()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
