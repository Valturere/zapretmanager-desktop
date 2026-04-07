using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class WindowsServiceManager
{
    public const string ServiceName = "zapret";
    private const string ServiceHostArgument = "--service-host";
    private static readonly string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";
    private static readonly Encoding OemEncoding = Encoding.GetEncoding(866);

    public ServiceStatusInfo GetStatus()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        var isInstalled = serviceKey is not null;
        var isRunning = false;
        string? executablePath = null;
        string? installationRootPath = null;
        string? profileToken = null;

        if (isInstalled)
        {
            var output = RunHidden("sc.exe", $"query {ServiceName}");
            isRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            var imagePath = serviceKey?.GetValue("ImagePath") as string;
            executablePath = TryExtractExecutablePath(imagePath);
            _ = TryParseServiceHostArguments(imagePath, out installationRootPath, out profileToken);
        }

        return new ServiceStatusInfo
        {
            IsInstalled = isInstalled,
            IsRunning = isRunning,
            ProfileName = serviceKey?.GetValue("zapret-discord-youtube") as string,
            ExecutablePath = executablePath,
            InstallationRootPath = installationRootPath,
            ProfileToken = profileToken
        };
    }

    public Task InstallAsync(ZapretInstallation installation, ConfigProfile profile)
    {
        return InstallAsync(installation, profile, null);
    }

    public async Task InstallAsync(ZapretInstallation installation, ConfigProfile profile, string? serviceHostExecutablePath)
    {
        var managerExecutablePath = GetManagerExecutablePath(serviceHostExecutablePath);
        var imagePath = BuildServiceBinaryPath(managerExecutablePath, installation.RootPath, profile.FileName);

        await RunHiddenAsync("netsh.exe", ["interface", "tcp", "set", "global", "timestamps=enabled"]);
        await EnsureServiceRemovedAsync(ServiceName, TimeSpan.FromSeconds(15));
        await EnsureServiceRemovedAsync("WinDivert", TimeSpan.FromSeconds(10));
        await EnsureServiceRemovedAsync("WinDivert14", TimeSpan.FromSeconds(10));

        ProcessRunResult create = new(1, string.Empty);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            create = await RunProcessAsync("sc.exe",
            [
                "create",
                ServiceName,
                "binPath=",
                imagePath,
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

        await RunHiddenAsync("sc.exe", ["config", ServiceName, "start=", "delayed-auto"]);
        await RunHiddenAsync("sc.exe", ["failure", ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/120000/restart/180000"]);
        await RunHiddenAsync("sc.exe", ["failureflag", ServiceName, "1"]);
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
            throw new InvalidOperationException(
                $"Служба установлена, но не запустилась. Возможно, мешают остатки старой службы или WinDivert.{Environment.NewLine}{start.Output}");
        }

        await WaitForServiceStateAsync(ServiceName, shouldBeRunning: true, TimeSpan.FromSeconds(10));
        var statusOutput = RunHidden("sc.exe", $"query {ServiceName}");
        if (!statusOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Служба создалась, но не осталась запущенной. Возможно, служба не смогла стартовать winws.{Environment.NewLine}{statusOutput}");
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

    public bool UsesExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var configured = GetConfiguredExecutablePath();
        return !string.IsNullOrWhiteSpace(configured) &&
               string.Equals(
                   Path.GetFullPath(configured),
                   Path.GetFullPath(executablePath),
                   StringComparison.OrdinalIgnoreCase);
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

    private static string GetManagerExecutablePath(string? explicitExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutablePath))
        {
            var explicitPath = Path.GetFullPath(explicitExecutablePath);
            if (!File.Exists(explicitPath))
            {
                throw new InvalidOperationException("Не найден указанный exe-файл ZapretManager для службы.");
            }

            return explicitPath;
        }

        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        var expectedAppHostPath = !string.IsNullOrWhiteSpace(entryAssemblyName)
            ? Path.Combine(AppContext.BaseDirectory, entryAssemblyName + ".exe")
            : null;

        var candidates = new[]
        {
            Environment.ProcessPath,
            Process.GetCurrentProcess().MainModule?.FileName,
            Environment.GetCommandLineArgs().FirstOrDefault(),
            expectedAppHostPath
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        .ToArray();

        var processPath = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Файл ZapretManager был перемещён после запуска. Закройте программу и откройте её заново из новой папки.");
        }

        return processPath;
    }

    private static string BuildServiceBinaryPath(string managerExecutablePath, string installationRootPath, string profileFileName)
    {
        return $"{QuoteArgument(managerExecutablePath)} {ServiceHostArgument} {QuoteArgument(installationRootPath)} {QuoteArgument(profileFileName)}";
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private string? GetConfiguredExecutablePath()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        var imagePath = serviceKey?.GetValue("ImagePath") as string;
        return TryExtractExecutablePath(imagePath);
    }

    private static string? TryExtractExecutablePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var trimmed = imagePath.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static bool TryParseServiceHostArguments(string? imagePath, out string? installationRootPath, out string? profileToken)
    {
        installationRootPath = null;
        profileToken = null;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        var match = Regex.Match(
            imagePath,
            "\"[^\"]+\"\\s+--service-host\\s+\"(?<root>[^\"]+)\"\\s+\"(?<profile>[^\"]+)\"",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        installationRootPath = match.Groups["root"].Value;
        profileToken = match.Groups["profile"].Value;
        return !string.IsNullOrWhiteSpace(installationRootPath) && !string.IsNullOrWhiteSpace(profileToken);
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

        var stopResult = await RunProcessAsync("sc.exe", ["stop", serviceName]);
        if (stopResult.ExitCode != 0 && !CanIgnoreStopFailure(stopResult.Output))
        {
            throw new InvalidOperationException(
                $"Не удалось остановить службу {serviceName}.{Environment.NewLine}{stopResult.Output}");
        }

        if (!await WaitForServiceStoppedOrGoneAsync(serviceName, TimeSpan.FromSeconds(Math.Min(timeout.TotalSeconds, 10))))
        {
            throw new InvalidOperationException($"Служба {serviceName} не остановилась вовремя перед удалением.");
        }

        if (!ServiceExists(serviceName))
        {
            return;
        }

        var deleteResult = await RunProcessAsync("sc.exe", ["delete", serviceName]);
        if (deleteResult.ExitCode != 0 && !CanIgnoreDeleteFailure(deleteResult.Output))
        {
            throw new InvalidOperationException(
                $"Не удалось удалить службу {serviceName}.{Environment.NewLine}{deleteResult.Output}");
        }

        if (!await WaitForServiceDeletionAsync(serviceName, timeout))
        {
            throw new InvalidOperationException($"Служба {serviceName} не была удалена вовремя.");
        }
    }

    private static async Task<bool> WaitForServiceDeletionAsync(string serviceName, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (ServiceExists(serviceName))
        {
            if (DateTime.UtcNow - started >= timeout)
            {
                return false;
            }

            await Task.Delay(400);
        }

        return true;
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

    private static async Task<bool> WaitForServiceStoppedOrGoneAsync(string serviceName, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (!ServiceExists(serviceName))
            {
                return true;
            }

            var statusOutput = RunHidden("sc.exe", $"query {serviceName}");
            if (statusOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ||
                statusOutput.Contains("1060", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(350);
        }

        return false;
    }

    private static bool CanIgnoreStopFailure(string output)
    {
        return output.Contains("1062", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("1060", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("already been stopped", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("already stopped", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("has not been started", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не была запущена", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не запущена", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не существует", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanIgnoreDeleteFailure(string output)
    {
        return IsMarkedForDeletion(output) ||
               output.Contains("1060", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("не существует", StringComparison.OrdinalIgnoreCase);
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

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = string.IsNullOrWhiteSpace(stderr)
            ? stdout.Trim()
            : $"{stdout}{Environment.NewLine}{stderr}".Trim();

        return new ProcessRunResult(process.ExitCode, output);
    }

    private sealed record ProcessRunResult(int ExitCode, string Output);
}
