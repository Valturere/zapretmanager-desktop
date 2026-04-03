using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class WindowsServiceManager
{
    private const string ServiceName = "zapret";
    private static readonly string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";
    private static readonly Encoding OemEncoding = Encoding.GetEncoding(866);
    private readonly ZapretConfigurationParser _parser = new();

    public ServiceStatusInfo GetStatus()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        var isInstalled = serviceKey is not null;
        var isRunning = false;

        if (isInstalled)
        {
            var output = RunHidden("sc.exe", $"query {ServiceName}");
            isRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }

        return new ServiceStatusInfo
        {
            IsInstalled = isInstalled,
            IsRunning = isRunning,
            ProfileName = serviceKey?.GetValue("zapret-discord-youtube") as string
        };
    }

    public async Task InstallAsync(ZapretInstallation installation, ConfigProfile profile)
    {
        _parser.EnsureUserFiles(installation);
        var arguments = _parser.BuildArguments(installation, profile);
        var winwsPath = Path.Combine(installation.BinPath, "winws.exe");

        await RunHiddenAsync("netsh.exe", ["interface", "tcp", "set", "global", "timestamps=enabled"]);
        await EnsureServiceRemovedAsync(ServiceName, TimeSpan.FromSeconds(15));

        ProcessRunResult create = new(1, string.Empty);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            create = await RunProcessAsync("sc.exe",
            [
                "create",
                ServiceName,
                "binPath=",
                $"\"{winwsPath}\" {arguments}",
                "DisplayName=",
                "zapret",
                "start=",
                "auto"
            ]);

            if (create.ExitCode == 0)
            {
                break;
            }

            if (!IsMarkedForDeletion(create.Output))
            {
                throw new InvalidOperationException($"Не удалось установить службу.{Environment.NewLine}{create.Output}");
            }

            await Task.Delay(700);
            await WaitForServiceDeletionAsync(ServiceName, TimeSpan.FromSeconds(10));
        }

        if (create.ExitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось установить службу.{Environment.NewLine}{create.Output}");
        }

        await RunHiddenAsync("sc.exe", ["description", ServiceName, "Zapret DPI bypass software"]);
        await RunHiddenAsync("reg.exe",
        [
            "add",
            $@"HKLM\System\CurrentControlSet\Services\{ServiceName}",
            "/v",
            "zapret-discord-youtube",
            "/t",
            "REG_SZ",
            "/d",
            profile.Name,
            "/f"
        ]);

        var start = await RunProcessAsync("sc.exe", ["start", ServiceName]);
        if (start.ExitCode != 0)
        {
            throw new InvalidOperationException($"Служба установлена, но не запустилась.{Environment.NewLine}{start.Output}");
        }

        await WaitForServiceStateAsync(ServiceName, shouldBeRunning: true, TimeSpan.FromSeconds(5));
        var statusOutput = RunHidden("sc.exe", $"query {ServiceName}");
        if (!statusOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Служба создалась, но не осталась запущенной.{Environment.NewLine}{statusOutput}");
        }
    }

    public async Task StopAsync()
    {
        await RunHiddenAsync("sc.exe", ["stop", ServiceName]);
    }

    public async Task StartAsync()
    {
        var start = await RunProcessAsync("sc.exe", ["start", ServiceName]);
        if (start.ExitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось запустить службу.{Environment.NewLine}{start.Output}");
        }
    }

    public async Task RemoveAsync()
    {
        await EnsureServiceRemovedAsync(ServiceName, TimeSpan.FromSeconds(15));
        await EnsureServiceRemovedAsync("WinDivert", TimeSpan.FromSeconds(10));
        await EnsureServiceRemovedAsync("WinDivert14", TimeSpan.FromSeconds(10));
    }

    public static string RunHidden(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding
        }) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}{Environment.NewLine}{stderr}";
    }

    private static async Task<string> RunHiddenAsync(string fileName, IEnumerable<string> arguments)
    {
        var result = await RunProcessAsync(fileName, arguments);
        return result.Output;
    }

    private static async Task EnsureServiceRemovedAsync(string serviceName, TimeSpan timeout)
    {
        if (!ServiceExists(serviceName))
        {
            return;
        }

        await RunHiddenAsync("sc.exe", ["stop", serviceName]);
        await Task.Delay(500);
        await RunHiddenAsync("sc.exe", ["delete", serviceName]);
        await WaitForServiceDeletionAsync(serviceName, timeout);
    }

    private static async Task WaitForServiceDeletionAsync(string serviceName, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (ServiceExists(serviceName))
        {
            if (DateTime.UtcNow - started >= timeout)
            {
                return;
            }

            await Task.Delay(400);
        }
    }

    private static async Task WaitForServiceStateAsync(string serviceName, bool shouldBeRunning, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            var statusOutput = RunHidden("sc.exe", $"query {serviceName}");
            var isRunning = statusOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            if (isRunning == shouldBeRunning)
            {
                return;
            }

            await Task.Delay(350);
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
        return servicesKey?.OpenSubKey(serviceName) is not null;
    }

    private static bool IsMarkedForDeletion(string output)
    {
        return output.Contains("1072", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("отмечена для удаления", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("marked for deletion", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessRunResult> RunProcessAsync(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessRunResult(process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}{Environment.NewLine}{stderr}");
    }

    private sealed record ProcessRunResult(int ExitCode, string Output);
}
