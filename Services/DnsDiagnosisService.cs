using System.Diagnostics;
using System.Net;
using System.Text;

namespace ZapretManager.Services;

public sealed class DnsDiagnosisService
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(6);

    public sealed record HostResolutionResult(
        string Host,
        bool SystemResolved,
        IReadOnlyList<string> SystemAddresses,
        string? SystemError,
        bool PublicResolved,
        IReadOnlyList<string> PublicAddresses,
        string? PublicError);

    public sealed record DiagnosisResult(
        bool SuggestDnsChange,
        IReadOnlyList<HostResolutionResult> Results);

    public async Task<DiagnosisResult> AnalyzeAsync(IEnumerable<string> hosts, CancellationToken cancellationToken = default)
    {
        var uniqueHosts = hosts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        var results = new List<HostResolutionResult>();
        foreach (var host in uniqueHosts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var systemResolution = await ResolveSystemAsync(host, cancellationToken);
            var publicResolution = await ResolveViaGoogleDnsAsync(host, cancellationToken);

            results.Add(new HostResolutionResult(
                host,
                systemResolution.Success,
                systemResolution.Addresses,
                systemResolution.Error,
                publicResolution.Success,
                publicResolution.Addresses,
                publicResolution.Error));
        }

        var suggestDnsChange = results.Any(item => !item.SystemResolved && item.PublicResolved);
        return new DiagnosisResult(suggestDnsChange, results);
    }

    private static async Task<(bool Success, IReadOnlyList<string> Addresses, string? Error)> ResolveSystemAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var normalized = addresses
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return normalized.Length > 0
                ? (true, normalized, null)
                : (false, [], "IPv4-адреса не найдены");
        }
        catch (Exception ex)
        {
            return (false, [], ex.Message);
        }
    }

    private static async Task<(bool Success, IReadOnlyList<string> Addresses, string? Error)> ResolveViaGoogleDnsAsync(string host, CancellationToken cancellationToken)
    {
        var escapedHost = EscapePowerShellSingleQuotedString(host);
        var script = string.Join(
            Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$records = Resolve-DnsName -Name '{escapedHost}' -Type A -Server 8.8.8.8 -ErrorAction Stop |",
            "    Where-Object { $_.IPAddress } |",
            "    Select-Object -ExpandProperty IPAddress",
            "if ($records) {",
            "    $records | ConvertTo-Json -Compress",
            "}");

        try
        {
            using var process = CreatePowerShellProcess(script);
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(ResolveTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                TryKillProcess(process);
                return (false, [], "Публичный DNS не ответил вовремя");
            }

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                return (false, [], string.IsNullOrWhiteSpace(error) ? "Публичный DNS не ответил" : error);
            }

            var addresses = ParseJsonArray(output);
            return addresses.Count > 0
                ? (true, addresses, null)
                : (false, [], "Публичный DNS не вернул IPv4-адреса");
        }
        catch (Exception ex)
        {
            return (false, [], ex.Message);
        }
    }

    private static IReadOnlyList<string> ParseJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        if (json.StartsWith('['))
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json)?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }

        var single = System.Text.Json.JsonSerializer.Deserialize<string>(json);
        return string.IsNullOrWhiteSpace(single) ? [] : [single.Trim()];
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
