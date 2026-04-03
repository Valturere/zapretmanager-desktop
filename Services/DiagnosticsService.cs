using System.Diagnostics;
using Microsoft.Win32;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class DiagnosticsService
{
    private static readonly string[] ConflictingBypassServices = ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"];

    public async Task<DiagnosticsReport> RunAsync(ZapretInstallation? installation, CancellationToken cancellationToken = default)
    {
        var items = new List<DiagnosticsCheckItem>();
        items.Add(CheckBaseFilteringEngine());
        items.Add(CheckProxy());

        var tcpCheck = await CheckTcpTimestampsAsync(cancellationToken);
        items.Add(tcpCheck.Item);

        items.Add(CheckAdguard());
        items.Add(CheckKillerServices());
        items.Add(CheckIntelConnectivity());
        items.Add(CheckCheckPoint());
        items.Add(CheckSmartByte());

        if (installation is not null)
        {
            items.Add(CheckWinDivertDriverFile(installation));
        }

        items.Add(CheckVpnServices());
        items.Add(CheckSecureDns());
        items.Add(CheckHostsFile());

        var staleWinDivertCheck = CheckStaleWinDivert();
        items.Add(staleWinDivertCheck.Item);

        var conflictingServicesCheck = CheckConflictingServices();
        items.Add(conflictingServicesCheck.Item);

        return new DiagnosticsReport
        {
            Items = items,
            NeedsTcpTimestampFix = tcpCheck.NeedsFix,
            HasStaleWinDivert = staleWinDivertCheck.HasStaleWinDivert,
            ConflictingServices = conflictingServicesCheck.Services
        };
    }

    public Task<bool> EnableTcpTimestampsAsync(CancellationToken cancellationToken = default)
        => RunNetshMutationAsync("interface tcp set global timestamps=enabled", cancellationToken);

    public async Task<bool> RemoveStaleWinDivertAsync(CancellationToken cancellationToken = default)
    {
        var stopMain = await RunScMutationAsync("stop", "WinDivert", cancellationToken);
        var deleteMain = await RunScMutationAsync("delete", "WinDivert", cancellationToken);
        _ = await RunScMutationAsync("stop", "WinDivert14", cancellationToken);
        _ = await RunScMutationAsync("delete", "WinDivert14", cancellationToken);
        return stopMain || deleteMain;
    }

    public async Task<IReadOnlyList<string>> RemoveConflictingServicesAsync(IEnumerable<string> services, CancellationToken cancellationToken = default)
    {
        var removed = new List<string>();

        foreach (var serviceName in services.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!ServiceExists(serviceName))
            {
                continue;
            }

            _ = await RunScMutationAsync("stop", serviceName, cancellationToken);
            if (await RunScMutationAsync("delete", serviceName, cancellationToken))
            {
                removed.Add(serviceName);
            }
        }

        return removed;
    }

    private static DiagnosticsCheckItem CheckBaseFilteringEngine()
    {
        var bfeRunning = string.Equals(GetServiceState("BFE"), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var firewallRunning = string.Equals(GetServiceState("MpsSvc"), "RUNNING", StringComparison.OrdinalIgnoreCase);

        return new DiagnosticsCheckItem
        {
            Title = "Base Filtering Engine",
            Severity = bfeRunning
                ? DiagnosticsSeverity.Success
                : firewallRunning
                    ? DiagnosticsSeverity.Error
                    : DiagnosticsSeverity.Warning,
            Message = bfeRunning
                ? "Служба BFE запущена."
                : "Служба BFE не запущена."
        };
    }

    private static DiagnosticsCheckItem CheckProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var proxyEnabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1;
            var proxyServer = key?.GetValue("ProxyServer")?.ToString();

            return new DiagnosticsCheckItem
            {
                Title = "Системный прокси",
                Severity = proxyEnabled ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Success,
                Message = proxyEnabled
                    ? $"Прокси включён: {proxyServer ?? "неизвестно"}. Убедись, что он действительно нужен."
                    : "Прокси не включён."
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheckItem
            {
                Title = "Системный прокси",
                Severity = DiagnosticsSeverity.Warning,
                Message = $"Не удалось проверить прокси: {ex.Message}"
            };
        }
    }

    private static async Task<(DiagnosticsCheckItem Item, bool NeedsFix)> CheckTcpTimestampsAsync(CancellationToken cancellationToken)
    {
        var output = await RunCommandCaptureAsync("netsh", "interface tcp show global", cancellationToken);
        var enabled = output.IndexOf("timestamp", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      output.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0;

        return (new DiagnosticsCheckItem
        {
            Title = "TCP timestamps",
            Severity = enabled ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Warning,
            Message = enabled
                ? "TCP timestamps включены."
                : "TCP timestamps выключены. Flowseal рекомендует включить их."
        }, !enabled);
    }

    private static DiagnosticsCheckItem CheckAdguard()
    {
        var found = Process.GetProcessesByName("AdguardSvc").Any();
        return new DiagnosticsCheckItem
        {
            Title = "Adguard",
            Severity = found ? DiagnosticsSeverity.Error : DiagnosticsSeverity.Success,
            Message = found
                ? "Обнаружен AdguardSvc.exe. Он может конфликтовать с Discord и zapret."
                : "Adguard не обнаружен."
        };
    }

    private static DiagnosticsCheckItem CheckKillerServices()
    {
        var found = GetServices()
            .Where(service => ContainsIgnoreCase(service.ServiceName, "Killer") || ContainsIgnoreCase(service.DisplayName, "Killer"))
            .Select(service => service.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DiagnosticsCheckItem
        {
            Title = "Killer",
            Severity = found.Length > 0 ? DiagnosticsSeverity.Error : DiagnosticsSeverity.Success,
            Message = found.Length > 0
                ? $"Найдены службы Killer: {string.Join(", ", found)}."
                : "Конфликтующих служб Killer не найдено."
        };
    }

    private static DiagnosticsCheckItem CheckIntelConnectivity()
    {
        var found = GetServices()
            .Where(service =>
                ContainsIgnoreCase(service.ServiceName, "Intel") &&
                ContainsIgnoreCase(service.DisplayName, "Connectivity") &&
                ContainsIgnoreCase(service.DisplayName, "Network"))
            .Select(service => service.DisplayName)
            .FirstOrDefault();

        return new DiagnosticsCheckItem
        {
            Title = "Intel Connectivity",
            Severity = found is null ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Error,
            Message = found is null
                ? "Конфликтующих служб Intel Connectivity не найдено."
                : $"Найдена служба {found}. Она может конфликтовать с zapret."
        };
    }

    private static DiagnosticsCheckItem CheckCheckPoint()
    {
        var found = GetServices()
            .Where(service => ContainsIgnoreCase(service.ServiceName, "TracSrvWrapper") || ContainsIgnoreCase(service.ServiceName, "EPWD"))
            .Select(service => service.ServiceName)
            .ToArray();

        return new DiagnosticsCheckItem
        {
            Title = "Check Point",
            Severity = found.Length > 0 ? DiagnosticsSeverity.Error : DiagnosticsSeverity.Success,
            Message = found.Length > 0
                ? $"Найдены службы Check Point: {string.Join(", ", found)}."
                : "Службы Check Point не найдены."
        };
    }

    private static DiagnosticsCheckItem CheckSmartByte()
    {
        var found = GetServices()
            .Where(service => ContainsIgnoreCase(service.ServiceName, "SmartByte") || ContainsIgnoreCase(service.DisplayName, "SmartByte"))
            .Select(service => service.DisplayName)
            .ToArray();

        return new DiagnosticsCheckItem
        {
            Title = "SmartByte",
            Severity = found.Length > 0 ? DiagnosticsSeverity.Error : DiagnosticsSeverity.Success,
            Message = found.Length > 0
                ? $"Найдены службы SmartByte: {string.Join(", ", found)}."
                : "SmartByte не обнаружен."
        };
    }

    private static DiagnosticsCheckItem CheckWinDivertDriverFile(ZapretInstallation installation)
    {
        var hasSys = Directory.Exists(installation.BinPath) &&
                     Directory.EnumerateFiles(installation.BinPath, "*.sys", SearchOption.TopDirectoryOnly).Any();

        return new DiagnosticsCheckItem
        {
            Title = "WinDivert драйвер",
            Severity = hasSys ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Error,
            Message = hasSys
                ? "Файл драйвера WinDivert найден."
                : "Файл WinDivert64.sys не найден в папке bin."
        };
    }

    private static DiagnosticsCheckItem CheckVpnServices()
    {
        var found = GetServices()
            .Where(service => ContainsIgnoreCase(service.ServiceName, "VPN") || ContainsIgnoreCase(service.DisplayName, "VPN"))
            .Select(service => service.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DiagnosticsCheckItem
        {
            Title = "VPN",
            Severity = found.Length > 0 ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Success,
            Message = found.Length > 0
                ? $"Найдены VPN-службы: {string.Join(", ", found)}. Убедись, что они выключены."
                : "VPN-службы не обнаружены."
        };
    }

    private static DiagnosticsCheckItem CheckSecureDns()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            var encryptedDnsFound = HasEncryptedDnsFlag(root);
            return new DiagnosticsCheckItem
            {
                Title = "Secure DNS",
                Severity = encryptedDnsFound ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Warning,
                Message = encryptedDnsFound
                    ? "Обнаружен включённый secure DNS."
                    : "Secure DNS не обнаружен. Flowseal рекомендует настроить его в браузере или Windows."
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheckItem
            {
                Title = "Secure DNS",
                Severity = DiagnosticsSeverity.Warning,
                Message = $"Не удалось проверить secure DNS: {ex.Message}"
            };
        }
    }

    private static DiagnosticsCheckItem CheckHostsFile()
    {
        try
        {
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (!File.Exists(hostsPath))
            {
                return new DiagnosticsCheckItem
                {
                    Title = "hosts",
                    Severity = DiagnosticsSeverity.Success,
                    Message = "Конфликтующих записей в hosts не обнаружено."
                };
            }

            var content = File.ReadAllText(hostsPath);
            var hasYoutubeEntries = content.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                                    content.Contains("yotou.be", StringComparison.OrdinalIgnoreCase);

            return new DiagnosticsCheckItem
            {
                Title = "hosts",
                Severity = hasYoutubeEntries ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Success,
                Message = hasYoutubeEntries
                    ? "В hosts найдены записи для youtube.com или yotou.be."
                    : "Конфликтующих записей в hosts не обнаружено."
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheckItem
            {
                Title = "hosts",
                Severity = DiagnosticsSeverity.Warning,
                Message = $"Не удалось проверить hosts: {ex.Message}"
            };
        }
    }

    private static (DiagnosticsCheckItem Item, bool HasStaleWinDivert) CheckStaleWinDivert()
    {
        var winwsRunning = Process.GetProcessesByName("winws").Any();
        var windivertState = GetServiceState("WinDivert");
        var stale = !winwsRunning &&
                    (string.Equals(windivertState, "RUNNING", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(windivertState, "STOP_PENDING", StringComparison.OrdinalIgnoreCase));

        return (new DiagnosticsCheckItem
        {
            Title = "WinDivert",
            Severity = stale ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Success,
            Message = stale
                ? "winws.exe не запущен, но служба WinDivert активна. Её лучше удалить."
                : "Подвисшей службы WinDivert не обнаружено."
        }, stale);
    }

    private static (DiagnosticsCheckItem Item, IReadOnlyList<string> Services) CheckConflictingServices()
    {
        var found = ConflictingBypassServices.Where(ServiceExists).ToArray();
        return (new DiagnosticsCheckItem
        {
            Title = "Конфликтующие bypass-службы",
            Severity = found.Length > 0 ? DiagnosticsSeverity.Error : DiagnosticsSeverity.Success,
            Message = found.Length > 0
                ? $"Найдены конфликтующие службы: {string.Join(", ", found)}."
                : "Конфликтующих bypass-служб не найдено."
        }, found);
    }

    private static bool HasEncryptedDnsFlag(RegistryKey? key)
    {
        if (key is null)
        {
            return false;
        }

        if (Convert.ToInt32(key.GetValue("DohFlags") ?? 0) > 0)
        {
            return true;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName);
            if (HasEncryptedDnsFlag(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ServiceExists(string serviceName)
    {
        return !string.IsNullOrWhiteSpace(GetServiceState(serviceName));
    }

    private static string? GetServiceState(string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            var stateLine = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains("STATE", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(stateLine))
            {
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(stateLine, "\\b(RUNNING|STOPPED|STOP_PENDING|START_PENDING)\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpperInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private static (string ServiceName, string DisplayName)[] GetServices()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", "query state= all")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return [];
            }

            var services = new List<(string ServiceName, string DisplayName)>();
            string? currentName = null;
            foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = line["SERVICE_NAME:".Length..].Trim();
                    services.Add((currentName, currentName));
                }
            }

            return [.. services];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsIgnoreCase(string value, string fragment)
        => value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

    private static async Task<string> RunCommandCaptureAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        return string.IsNullOrWhiteSpace(error) ? output : $"{output}{Environment.NewLine}{error}".Trim();
    }

    private static async Task<bool> RunNetshMutationAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("netsh", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    private static async Task<bool> RunScMutationAsync(string command, string serviceName, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("sc.exe", $"{command} \"{serviceName}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }
}
