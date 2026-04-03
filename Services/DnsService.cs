using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ZapretManager.Services;

public sealed class DnsService
{
    private static readonly TimeSpan PowerShellCommandTimeout = TimeSpan.FromSeconds(20);

    public const string SystemProfileKey = "system";
    public const string XboxProfileKey = "xbox";
    public const string CloudflareProfileKey = "cloudflare";
    public const string GoogleProfileKey = "google";
    public const string Quad9ProfileKey = "quad9";
    public const string CustomProfileKey = "custom";
    public const string XboxDohTemplate = "https://xbox-dns.ru/dns-query";
    public const string CloudflareDohTemplate = "https://cloudflare-dns.com/dns-query";
    public const string GoogleDohTemplate = "https://dns.google/dns-query";
    public const string Quad9DohTemplate = "https://dns.quad9.net/dns-query";

    public sealed record DnsProfileDefinition(string Key, string Label, IReadOnlyList<string> ServerAddresses, string? DohTemplate);

    public sealed record DnsAdapterStatus(
        string InterfaceAlias,
        int InterfaceIndex,
        bool HasDefaultGateway,
        IReadOnlyList<string> ServerAddresses);

    public sealed record DnsStatusSnapshot(IReadOnlyList<DnsAdapterStatus> Adapters);

    public IReadOnlyList<DnsProfileDefinition> GetPresetDefinitions(
        string? customPrimary = null,
        string? customSecondary = null,
        string? customDohTemplate = null)
    {
        var customServers = NormalizeDnsServers(customPrimary, customSecondary);

        return
        [
            new(SystemProfileKey, "Системный (DHCP)", [], null),
            new(XboxProfileKey, "XBOX DNS", ["111.88.96.50", "111.88.96.51"], XboxDohTemplate),
            new(CloudflareProfileKey, "Cloudflare DNS", ["1.1.1.1", "1.0.0.1"], CloudflareDohTemplate),
            new(GoogleProfileKey, "Google DNS", ["8.8.8.8", "8.8.4.4"], GoogleDohTemplate),
            new(Quad9ProfileKey, "Quad9 DNS", ["9.9.9.9", "149.112.112.112"], Quad9DohTemplate),
            new(CustomProfileKey, "Пользовательский DNS", customServers, NormalizeDohTemplate(customDohTemplate))
        ];
    }

    public bool HasCustomDns(string? customPrimary, string? customSecondary)
    {
        return NormalizeDnsServers(customPrimary, customSecondary).Count > 0;
    }

    public string GetProfileLabel(
        string profileKey,
        string? customPrimary = null,
        string? customSecondary = null,
        string? customDohTemplate = null,
        bool useDnsOverHttps = false)
    {
        var label = GetPresetDefinitions(customPrimary, customSecondary, customDohTemplate)
            .FirstOrDefault(item => string.Equals(item.Key, profileKey, StringComparison.OrdinalIgnoreCase))
            ?.Label
            ?? "DNS";

        return useDnsOverHttps && !string.Equals(profileKey, SystemProfileKey, StringComparison.OrdinalIgnoreCase)
            ? $"{label} + DoH"
            : label;
    }

    public DnsStatusSnapshot GetCurrentStatus()
    {
        const string script = """
            $items = Get-NetIPConfiguration |
                Where-Object { $_.NetAdapter -and $_.NetAdapter.Status -eq 'Up' -and $_.IPv4Address } |
                ForEach-Object {
                    $dns = @()
                    try {
                        $dns = @((Get-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses | Where-Object { $_ })
                    } catch {
                    }

                    [PSCustomObject]@{
                        InterfaceAlias = $_.InterfaceAlias
                        InterfaceIndex = $_.InterfaceIndex
                        HasDefaultGateway = ($null -ne $_.IPv4DefaultGateway)
                        ServerAddresses = @($dns)
                    }
                }

            if ($items) {
                $items | ConvertTo-Json -Depth 4 -Compress
            }
            """;

        var output = ExecutePowerShell(script);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new DnsStatusSnapshot([]);
        }

        var adapters = new List<DnsAdapterStatus>();
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var adapter = ParseAdapter(element);
                if (adapter is not null)
                {
                    adapters.Add(adapter);
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            var adapter = ParseAdapter(document.RootElement);
            if (adapter is not null)
            {
                adapters.Add(adapter);
            }
        }

        return new DnsStatusSnapshot(adapters);
    }

    public string? MatchPresetKey(DnsStatusSnapshot status, string? customPrimary = null, string? customSecondary = null)
    {
        var currentServers = status.Adapters
            .SelectMany(item => item.ServerAddresses)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (currentServers.Length == 0)
        {
            return null;
        }

        foreach (var profile in GetPresetDefinitions(customPrimary, customSecondary))
        {
            if (profile.ServerAddresses.Count == 0)
            {
                continue;
            }

            var normalizedProfile = profile.ServerAddresses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (currentServers.SequenceEqual(normalizedProfile, StringComparer.OrdinalIgnoreCase))
            {
                return profile.Key;
            }
        }

        return null;
    }

    public string GetStatusDescription(DnsStatusSnapshot status, string? customPrimary = null, string? customSecondary = null)
    {
        if (status.Adapters.Count == 0)
        {
            return "Активные IPv4-подключения не найдены. Можно всё равно выбрать профиль и применить его позже.";
        }

        var matchedKey = MatchPresetKey(status, customPrimary, customSecondary);
        var currentLabel = matchedKey is not null
            ? GetProfileLabel(matchedKey, customPrimary, customSecondary)
            : "Пользовательский DNS";

        var lines = new List<string>
        {
            $"Текущий DNS: {currentLabel}"
        };

        foreach (var adapter in status.Adapters.OrderByDescending(item => item.HasDefaultGateway))
        {
            var value = adapter.ServerAddresses.Count == 0
                ? "получается автоматически"
                : string.Join(", ", adapter.ServerAddresses);

            lines.Add($"{adapter.InterfaceAlias}: {value}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task ApplyProfileAsync(
        string profileKey,
        string? customPrimary = null,
        string? customSecondary = null,
        bool useDnsOverHttps = false,
        string? customDohTemplate = null)
    {
        var profile = GetPresetDefinitions(customPrimary, customSecondary, customDohTemplate)
            .FirstOrDefault(item => string.Equals(item.Key, profileKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Неизвестный DNS-профиль.");

        if (string.Equals(profile.Key, SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            useDnsOverHttps = false;
        }

        if (string.Equals(profile.Key, CustomProfileKey, StringComparison.OrdinalIgnoreCase) &&
            profile.ServerAddresses.Count == 0)
        {
            throw new InvalidOperationException("Сначала укажите хотя бы один адрес пользовательского DNS.");
        }

        if (useDnsOverHttps &&
            !string.Equals(profile.Key, SystemProfileKey, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(profile.DohTemplate))
        {
            throw new InvalidOperationException("Для выбранного DNS-профиля не указан DoH URL.");
        }

        var status = GetCurrentStatus();
        var interfaceIndexes = status.Adapters
            .Where(item => item.HasDefaultGateway)
            .Select(item => item.InterfaceIndex)
            .Distinct()
            .ToArray();

        if (interfaceIndexes.Length == 0)
        {
            interfaceIndexes = status.Adapters
                .Select(item => item.InterfaceIndex)
                .Distinct()
                .ToArray();
        }

        if (interfaceIndexes.Length == 0)
        {
            throw new InvalidOperationException("Не удалось определить активный сетевой адаптер для изменения DNS.");
        }

        var serverArrayLiteral = profile.ServerAddresses.Count == 0
            ? string.Empty
            : string.Join(", ", profile.ServerAddresses.Select(address => $"'{EscapePowerShellSingleQuotedString(address)}'"));

        var scriptBuilder = new StringBuilder();
        scriptBuilder.AppendLine("$ErrorActionPreference = 'Stop'");
        scriptBuilder.AppendLine($"$interfaceIndexes = @({string.Join(", ", interfaceIndexes)})");

        if (useDnsOverHttps)
        {
            scriptBuilder.AppendLine("if (-not (Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) { throw 'На этой версии Windows не найдена команда Add-DnsClientDohServerAddress.' }");
            scriptBuilder.AppendLine("if (-not (Get-Command Set-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) { throw 'На этой версии Windows не найдена команда Set-DnsClientDohServerAddress.' }");
            scriptBuilder.AppendLine($"$serverAddresses = @({serverArrayLiteral})");
            scriptBuilder.AppendLine($"$dohTemplate = '{EscapePowerShellSingleQuotedString(profile.DohTemplate!)}'");
            scriptBuilder.AppendLine("foreach ($serverAddress in $serverAddresses) {");
            scriptBuilder.AppendLine("    $existing = Get-DnsClientDohServerAddress -ServerAddress $serverAddress -ErrorAction SilentlyContinue");
            scriptBuilder.AppendLine("    if ($null -ne $existing) {");
            scriptBuilder.AppendLine("        Set-DnsClientDohServerAddress -ServerAddress $serverAddress -DohTemplate $dohTemplate -AllowFallbackToUdp $false -AutoUpgrade $true -ErrorAction Stop | Out-Null");
            scriptBuilder.AppendLine("    }");
            scriptBuilder.AppendLine("    else {");
            scriptBuilder.AppendLine("        Add-DnsClientDohServerAddress -ServerAddress $serverAddress -DohTemplate $dohTemplate -AllowFallbackToUdp $false -AutoUpgrade $true -ErrorAction Stop | Out-Null");
            scriptBuilder.AppendLine("    }");
            scriptBuilder.AppendLine("}");
            scriptBuilder.AppendLine("& netsh dnsclient set global doh=yes | Out-Null");
            scriptBuilder.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'Не удалось включить DNS-over-HTTPS.' }");
        }
        else
        {
            scriptBuilder.AppendLine("& netsh dnsclient set global doh=no | Out-Null");
            scriptBuilder.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'Не удалось отключить DNS-over-HTTPS.' }");
        }

        if (profile.ServerAddresses.Count == 0)
        {
            scriptBuilder.AppendLine("foreach ($interfaceIndex in $interfaceIndexes) {");
            scriptBuilder.AppendLine("    Set-DnsClientServerAddress -InterfaceIndex $interfaceIndex -ResetServerAddresses -ErrorAction Stop");
            scriptBuilder.AppendLine("}");
        }
        else
        {
            scriptBuilder.AppendLine($"$serverAddresses = @({serverArrayLiteral})");
            scriptBuilder.AppendLine("foreach ($interfaceIndex in $interfaceIndexes) {");
            scriptBuilder.AppendLine("    Set-DnsClientServerAddress -InterfaceIndex $interfaceIndex -ServerAddresses $serverAddresses -ErrorAction Stop");
            scriptBuilder.AppendLine("}");
        }

        scriptBuilder.AppendLine("ipconfig /flushdns | Out-Null");
        await ExecutePowerShellAsync(scriptBuilder.ToString());
    }

    public IReadOnlyList<string> NormalizeDnsServers(string? primary, string? secondary)
    {
        return new[] { primary, secondary }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? NormalizeDohTemplate(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DnsAdapterStatus? ParseAdapter(JsonElement element)
    {
        if (!element.TryGetProperty("InterfaceAlias", out var aliasElement) ||
            !element.TryGetProperty("InterfaceIndex", out var indexElement))
        {
            return null;
        }

        var servers = new List<string>();
        if (element.TryGetProperty("ServerAddresses", out var serversElement) &&
            serversElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var serverElement in serversElement.EnumerateArray())
            {
                var value = serverElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    servers.Add(value.Trim());
                }
            }
        }

        var hasDefaultGateway = false;
        if (element.TryGetProperty("HasDefaultGateway", out var gatewayElement) &&
            (gatewayElement.ValueKind == JsonValueKind.True || gatewayElement.ValueKind == JsonValueKind.False))
        {
            hasDefaultGateway = gatewayElement.GetBoolean();
        }

        return new DnsAdapterStatus(
            aliasElement.GetString() ?? "Неизвестный адаптер",
            indexElement.GetInt32(),
            hasDefaultGateway,
            servers);
    }

    private static string ExecutePowerShell(string script)
    {
        using var process = CreatePowerShellProcess(script);
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)PowerShellCommandTimeout.TotalMilliseconds))
        {
            TryKillProcess(process);
            throw new TimeoutException("Не удалось вовремя получить состояние DNS.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Не удалось получить настройки DNS."
                : error.Trim());
        }

        return output.Trim();
    }

    private static async Task ExecutePowerShellAsync(string script)
    {
        using var process = CreatePowerShellProcess(script);
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(PowerShellCommandTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new TimeoutException("Смена DNS заняла слишком много времени и была остановлена.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? string.IsNullOrWhiteSpace(output) ? "Не удалось изменить DNS." : output.Trim()
                : error.Trim());
        }
    }

    private static Process CreatePowerShellProcess(string script)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };
    }

    private static void TryKillProcess(Process process)
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
        }
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''");
    }
}
