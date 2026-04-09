using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class DiagnosticsService
{
    private static readonly string[] ConflictingBypassServices = ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"];
    private static readonly Encoding OemEncoding = Encoding.GetEncoding(866);
    private static readonly string LocalAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string[] VpnMarkers =
    [
        "VPN",
        "HAPP",
        "WINTUN",
        "WIREGUARD",
        "OPENVPN",
        "TAP",
        "TUN",
        "TAILSCALE",
        "ZEROTIER",
        "AMNEZIA",
        "OUTLINE",
        "TUN2SOCKS",
        "RADMIN"
    ];
    private static readonly (string BrowserName, string LocalStatePath)[] BrowserSecureDnsCandidates =
    [
        ("Chrome", Path.Combine(LocalAppDataPath, "Google", "Chrome", "User Data", "Local State")),
        ("Edge", Path.Combine(LocalAppDataPath, "Microsoft", "Edge", "User Data", "Local State")),
        ("Brave", Path.Combine(LocalAppDataPath, "BraveSoftware", "Brave-Browser", "User Data", "Local State")),
        ("Yandex", Path.Combine(LocalAppDataPath, "Yandex", "YandexBrowser", "User Data", "Local State"))
    ];

    public async Task<DiagnosticsReport> RunAsync(
        ZapretInstallation? installation,
        IProgress<DiagnosticsProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<DiagnosticsCheckItem>();
        var totalChecks = installation is not null ? 14 : 13;

        void ReportStatus(string text)
        {
            progress?.Report(new DiagnosticsProgressUpdate
            {
                StatusText = text,
                CompletedChecks = items.Count,
                TotalChecks = totalChecks
            });
        }

        void AddItem(DiagnosticsCheckItem item)
        {
            items.Add(item);
            progress?.Report(new DiagnosticsProgressUpdate
            {
                StatusText = $"Проверено {items.Count} из {totalChecks}: {item.Title}",
                Item = item,
                CompletedChecks = items.Count,
                TotalChecks = totalChecks
            });
        }

        async Task RunCheckAsync(string statusText, Func<DiagnosticsCheckItem> check)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportStatus(statusText);
            var item = await Task.Run(check, cancellationToken);
            AddItem(item);
        }

        async Task<T> RunStepAsync<T>(string statusText, Func<T> step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportStatus(statusText);
            return await Task.Run(step, cancellationToken);
        }

        await RunCheckAsync("Проверяем Base Filtering Engine...", CheckBaseFilteringEngine);
        await RunCheckAsync("Проверяем системный прокси...", CheckProxy);
        await RunCheckAsync("Проверяем Adguard...", CheckAdguard);
        await RunCheckAsync("Проверяем Killer...", CheckKillerServices);
        await RunCheckAsync("Проверяем Intel Connectivity...", CheckIntelConnectivity);
        await RunCheckAsync("Проверяем Check Point...", CheckCheckPoint);
        await RunCheckAsync("Проверяем SmartByte...", CheckSmartByte);

        if (installation is not null)
        {
            await RunCheckAsync("Проверяем драйвер WinDivert...", () => CheckWinDivertDriverFile(installation));
        }

        await RunCheckAsync("Проверяем VPN...", CheckVpnServices);
        await RunCheckAsync("Проверяем Secure DNS...", CheckSecureDns);
        await RunCheckAsync("Проверяем hosts...", CheckHostsFile);

        var tcpCheck = await RunStepAsync("Проверяем TCP timestamps...", () =>
            CheckTcpTimestampsAsync(cancellationToken).GetAwaiter().GetResult());
        AddItem(tcpCheck.Item);

        var staleWinDivertCheck = await RunStepAsync("Проверяем подвисший WinDivert...", CheckStaleWinDivert);
        AddItem(staleWinDivertCheck.Item);

        var conflictingServicesCheck = await RunStepAsync("Проверяем конфликтующие bypass-службы...", CheckConflictingServices);
        AddItem(conflictingServicesCheck.Item);

        ReportStatus("Диагностика завершена.");

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
        var enabled = IsTcpTimestampsEnabled(output);

        return (new DiagnosticsCheckItem
        {
            Title = "TCP timestamps",
            Severity = enabled ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Warning,
            Message = enabled
                ? "TCP timestamps включены."
                : "TCP timestamps выключены. Flowseal рекомендует включить их."
        }, !enabled);
    }

    private static bool IsTcpTimestampsEnabled(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!ContainsAny(line, "timestamp", "timestamps", "rfc 1323", "временн", "метк"))
            {
                continue;
            }

            if (ContainsAny(line, "enabled", "включ", "разреш"))
            {
                return true;
            }

            if (ContainsAny(line, "disabled", "выключ", "отключ", "запрещ"))
            {
                return false;
            }
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] markers)
    {
        return markers.Any(marker => value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
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
        var activeAdapters = GetActiveVpnAdapterMatches();
        var componentMatches = GetVpnComponentMatches();

        return new DiagnosticsCheckItem
        {
            Title = "VPN",
            Severity = activeAdapters.Length > 0 ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Success,
            Message = activeAdapters.Length > 0
                ? "Обнаружен активный VPN."
                : "Активный VPN не обнаружен.",
            Details = activeAdapters.Length > 0
                ? $"Обнаружен активный VPN: {string.Join(", ", activeAdapters)}. На время проверки и работы zapret лучше отключить VPN."
                : componentMatches.Length > 0
                    ? $"Активный VPN не обнаружен. В системе есть установленные VPN-компоненты: {string.Join(", ", componentMatches)}."
                    : null
        };
    }

    private static string[] GetActiveVpnAdapterMatches()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    IsVpnLikeAdapter(adapter) &&
                    HasActiveVpnAddressing(adapter))
                .Select(adapter => $"адаптер {adapter.Name}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string[] GetVpnComponentMatches()
    {
        try
        {
            var serviceMatches = GetServices()
                .Where(service => MatchesVpnSignature(service.ServiceName) || MatchesVpnSignature(service.DisplayName))
                .Select(service => $"служба {service.DisplayName}")
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var adapterMatches = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsVpnLikeAdapter)
                .Select(adapter => $"адаптер {adapter.Name}")
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return serviceMatches
                .Concat(adapterMatches)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsVpnLikeAdapter(NetworkInterface adapter)
    {
        return adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
               MatchesVpnSignature(adapter.Name) ||
               MatchesVpnSignature(adapter.Description);
    }

    private static bool HasActiveVpnAddressing(NetworkInterface adapter)
    {
        try
        {
            var properties = adapter.GetIPProperties();
            var hasUsableAddress = properties.UnicastAddresses.Any(address =>
            {
                var ip = address.Address;
                if (IPAddress.IsLoopback(ip))
                {
                    return false;
                }

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = ip.GetAddressBytes();
                    return !(bytes[0] == 169 && bytes[1] == 254);
                }

                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return !ip.IsIPv6LinkLocal;
                }

                return false;
            });

            if (!hasUsableAddress)
            {
                return false;
            }

            var hasGateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                !gateway.Address.Equals(IPAddress.Any) &&
                !gateway.Address.Equals(IPAddress.IPv6Any) &&
                !gateway.Address.Equals(IPAddress.None) &&
                !gateway.Address.Equals(IPAddress.IPv6None));

            var hasDns = properties.DnsAddresses.Any(address =>
                address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                !IPAddress.IsLoopback(address));

            return hasGateway || hasDns;
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesVpnSignature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return VpnMarkers.Any(marker => ContainsIgnoreCase(value, marker));
    }

    private static DiagnosticsCheckItem CheckSecureDns()
    {
        try
        {
            var windowsEnabled = IsWindowsSecureDnsConfigured();
            var browserStatuses = GetBrowserSecureDnsStatuses();
            var confirmedBrowsers = browserStatuses
                .Where(status => status.IsConfirmed)
                .Select(status => status.BrowserName)
                .ToArray();
            var automaticBrowsers = browserStatuses
                .Where(status => !status.IsConfirmed)
                .Select(status => status.BrowserName)
                .ToArray();

            var details = BuildSecureDnsDetails(windowsEnabled, confirmedBrowsers, automaticBrowsers);

            if (windowsEnabled && confirmedBrowsers.Length > 0)
            {
                return new DiagnosticsCheckItem
                {
                    Title = "Secure DNS",
                    Severity = DiagnosticsSeverity.Success,
                    Message = $"Обнаружен Secure DNS в Windows и браузере: {string.Join(", ", confirmedBrowsers)}.",
                    Details = details
                };
            }

            if (windowsEnabled)
            {
                return new DiagnosticsCheckItem
                {
                    Title = "Secure DNS",
                    Severity = DiagnosticsSeverity.Success,
                    Message = "Обнаружен Secure DNS в Windows (DoH).",
                    Details = details
                };
            }

            if (confirmedBrowsers.Length > 0)
            {
                return new DiagnosticsCheckItem
                {
                    Title = "Secure DNS",
                    Severity = DiagnosticsSeverity.Success,
                    Message = $"Обнаружен Secure DNS в браузере: {string.Join(", ", confirmedBrowsers)}.",
                    Details = details
                };
            }

            if (automaticBrowsers.Length > 0)
            {
                return new DiagnosticsCheckItem
                {
                    Title = "Secure DNS",
                    Severity = DiagnosticsSeverity.Warning,
                    Message = $"В браузере включён Secure DNS в авто-режиме: {string.Join(", ", automaticBrowsers)}.",
                    Details = details
                };
            }

            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            return new DiagnosticsCheckItem
            {
                Title = "Secure DNS",
                Severity = HasEncryptedDnsFlag(root) ? DiagnosticsSeverity.Success : DiagnosticsSeverity.Warning,
                Message = "Secure DNS не обнаружен. Flowseal рекомендует настроить его в браузере или Windows."
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

    private static string? BuildSecureDnsDetails(bool windowsEnabled, IReadOnlyList<string> confirmedBrowsers, IReadOnlyList<string> automaticBrowsers)
    {
        var parts = new List<string>();

        if (windowsEnabled)
        {
            parts.Add("Windows DoH обнаружен.");
        }

        if (confirmedBrowsers.Count > 0)
        {
            parts.Add($"Браузерный Secure DNS обнаружен: {string.Join(", ", confirmedBrowsers)}.");
        }

        if (automaticBrowsers.Count > 0)
        {
            parts.Add($"Авто-режим Secure DNS включён: {string.Join(", ", automaticBrowsers)}.");
        }

        if (!windowsEnabled && (confirmedBrowsers.Count > 0 || automaticBrowsers.Count > 0))
        {
            parts.Add("Это влияет на браузер, но проверка конфигов и системные приложения используют системный DNS.");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
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

    private static bool IsWindowsSecureDnsConfigured()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            if (HasEncryptedDnsFlag(root) || HasModernDohMarkers(root))
            {
                return true;
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            var output = RunCommandCaptureAsync(
                    "powershell.exe",
                    "-NoProfile -Command \"Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue | Where-Object { $_.DohTemplate -or $_.DohTemplateTemplate -or $_.AutoUpgrade -or $_.AllowFallbackToUdp } | ForEach-Object { $_.ServerAddress }\"",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => !string.IsNullOrWhiteSpace(line));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasModernDohMarkers(RegistryKey? key)
    {
        if (key is null)
        {
            return false;
        }

        if (key.GetValueNames().Any(name =>
                string.Equals(name, "DohTemplate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "DohFlags", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "AutoUpgrade", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "AllowFallbackToUdp", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            if (string.Equals(childName, "DohProfileSettings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "DohInterfaceSettings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "Doh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "Doh6", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            using var child = key.OpenSubKey(childName);
            if (HasModernDohMarkers(child))
            {
                return true;
            }
        }

        return false;
    }

    private static BrowserSecureDnsStatus[] GetBrowserSecureDnsStatuses()
    {
        var results = new List<BrowserSecureDnsStatus>();

        foreach (var candidate in BrowserSecureDnsCandidates)
        {
            var status = TryReadBrowserSecureDnsStatus(candidate.BrowserName, candidate.LocalStatePath);
            if (status is not null)
            {
                results.Add(status);
            }
        }

        return [.. results];
    }

    private static BrowserSecureDnsStatus? TryReadBrowserSecureDnsStatus(string browserName, string localStatePath)
    {
        try
        {
            if (!File.Exists(localStatePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (!document.RootElement.TryGetProperty("dns_over_https", out var dnsNode) ||
                dnsNode.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var mode = TryGetJsonString(dnsNode, "mode");
            if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var templates = TryGetJsonString(dnsNode, "templates");
            var isConfirmed =
                string.Equals(mode, "secure", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(templates) && !string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase));

            return new BrowserSecureDnsStatus(browserName, mode, isConfirmed);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private sealed record BrowserSecureDnsStatus(string BrowserName, string Mode, bool IsConfirmed);

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
            foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var serviceName = line["SERVICE_NAME:".Length..].Trim();
                services.Add((serviceName, GetServiceDisplayName(serviceName)));
            }

            return [.. services];
        }
        catch
        {
            return [];
        }
    }

    private static string GetServiceDisplayName(string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", $"getdisplayname \"{serviceName}\"")
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
                return serviceName;
            }

            var displayNameLine = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains("NAME =", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(displayNameLine))
            {
                return serviceName;
            }

            var separatorIndex = displayNameLine.IndexOf('=');
            return separatorIndex >= 0
                ? displayNameLine[(separatorIndex + 1)..].Trim()
                : serviceName;
        }
        catch
        {
            return serviceName;
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
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding,
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
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding,
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
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }
}
