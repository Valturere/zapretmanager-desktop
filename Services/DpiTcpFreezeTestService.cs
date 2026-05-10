using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class DpiTcpFreezeTestService
{
    private const string SuiteUrl = "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.v2.json";
    private const int TimeoutSeconds = 5;
    private const int RangeBytes = 65536;
    private const int MaxParallelTargets = 8;
    private static readonly JsonSerializerOptions SuiteJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly ZapretProcessService _processService = new();

    public async Task<TcpFreezeRunReport> RunAsync(
        ZapretInstallation installation,
        IReadOnlyList<ConfigProfile> profiles,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Нет конфигов для проверки.");
        }

        EnsureCurlAvailable();

        var targets = await LoadSuiteTargetsAsync(cancellationToken);
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить цели для проверки TCP 16-20.");
        }

        var payloadPath = CreatePayloadFile();
        try
        {
            progress?.Report($"Загружен suite TCP 16-20: {targets.Count} целей.");
            progress?.Report($"Параметры: range 0-{RangeBytes - 1}, timeout {TimeoutSeconds} сек., параллельность {MaxParallelTargets}.");

            var summaries = new List<TcpFreezeConfigResult>();
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(string.Empty);
                progress?.Report($"=== {profile.Name} ===");

                await StopInstallationAsync(installation, cancellationToken);
                await _processService.StartAsync(installation, profile, silentMode: true);
                await WaitForWinwsStateAsync(installation, shouldBeRunning: true, cancellationToken);
                await Task.Delay(1500, cancellationToken);

                IReadOnlyList<TcpFreezeTargetResult> targetResults;
                try
                {
                    targetResults = await ProbeTargetsAsync(targets, payloadPath, progress, cancellationToken);
                }
                finally
                {
                    await StopInstallationAsync(installation, cancellationToken);
                }

                var summary = BuildSummary(profile, targetResults);
                summaries.Add(summary);
                progress?.Report(
                    $"Итог {summary.ConfigName}: OK {summary.OkCount}, BLOCKED {summary.BlockedCount}, FAIL {summary.FailCount}, UNSUP {summary.UnsupportedCount}.");
                if (summary.BlockedTargets.Count > 0)
                {
                    progress?.Report($"Подозрение на TCP 16-20 block: {string.Join(", ", summary.BlockedTargets.Take(6))}");
                }
            }

            var recommended = summaries
                .OrderByDescending(item => item.OkCount)
                .ThenBy(item => item.BlockedCount)
                .ThenBy(item => item.FailCount)
                .ThenBy(item => item.UnsupportedCount)
                .ThenBy(item => item.ConfigName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            progress?.Report(string.Empty);
            progress?.Report("=== Сводка ===");
            foreach (var summary in summaries)
            {
                progress?.Report(
                    $"{summary.ConfigName}: OK {summary.OkCount}, BLOCKED {summary.BlockedCount}, FAIL {summary.FailCount}, UNSUP {summary.UnsupportedCount}");
            }

            if (recommended is not null)
            {
                progress?.Report(string.Empty);
                progress?.Report($"Наиболее устойчивый конфиг по TCP 16-20: {recommended.ConfigName}");
            }

            return new TcpFreezeRunReport
            {
                ConfigResults = summaries,
                RecommendedConfigPath = recommended?.FilePath
            };
        }
        finally
        {
            TryDeleteFile(payloadPath);
            await StopInstallationAsync(installation, CancellationToken.None);
        }
    }

    private static async Task<IReadOnlyList<SuiteTarget>> LoadSuiteTargetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(SuiteUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var targets = await JsonSerializer.DeserializeAsync<List<SuiteTarget>>(stream, SuiteJsonOptions, cancellationToken) ?? [];
            return targets
                .Where(target => !string.IsNullOrWhiteSpace(target.Host))
                .ToList();
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, "Не удалось загрузить suite для TCP 16-20");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretManager");
        return client;
    }

    private static string CreatePayloadFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZapretManager", $"tcp-freeze-{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new byte[RangeBytes];
        RandomNumberGenerator.Fill(payload);
        File.WriteAllBytes(path, payload);
        return path;
    }

    private async Task<IReadOnlyList<TcpFreezeTargetResult>> ProbeTargetsAsync(
        IReadOnlyList<SuiteTarget> targets,
        string payloadPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var results = new TcpFreezeTargetResult[targets.Count];
        using var semaphore = new SemaphoreSlim(MaxParallelTargets);

        var tasks = targets.Select(async (target, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await ProbeTargetAsync(target, payloadPath, cancellationToken);
                results[index] = result;
                progress?.Report(BuildTargetLogLine(result));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private async Task<TcpFreezeTargetResult> ProbeTargetAsync(SuiteTarget target, string payloadPath, CancellationToken cancellationToken)
    {
        var checks = new List<TcpFreezeProtocolResult>(3);
        foreach (var definition in GetProtocolDefinitions())
        {
            checks.Add(await RunProtocolCheckAsync(target, payloadPath, definition, cancellationToken));
        }

        return new TcpFreezeTargetResult
        {
            TargetId = target.Id,
            Provider = target.Provider,
            Country = target.Country,
            Host = target.Host,
            Checks = checks
        };
    }

    private static IEnumerable<ProtocolDefinition> GetProtocolDefinitions()
    {
        yield return new ProtocolDefinition("HTTP", ["--http1.1"]);
        yield return new ProtocolDefinition("TLS1.2", ["--tlsv1.2", "--tls-max", "1.2"]);
        yield return new ProtocolDefinition("TLS1.3", ["--tlsv1.3", "--tls-max", "1.3"]);
    }

    private static async Task<TcpFreezeProtocolResult> RunProtocolCheckAsync(
        SuiteTarget target,
        string payloadPath,
        ProtocolDefinition definition,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateCurlStartInfo(target.Host, payloadPath, definition)
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        var combined = string.Join(" ", new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (TryParseCurlMetrics(stdout, out var metrics))
        {
            var status = process.ExitCode == 0
                ? TcpFreezeProtocolStatus.Ok
                : metrics.UpBytes > 0 && metrics.DownBytes == 0 && metrics.TimeSeconds >= TimeoutSeconds
                    ? TcpFreezeProtocolStatus.LikelyBlocked
                    : TcpFreezeProtocolStatus.Fail;

            return new TcpFreezeProtocolResult
            {
                Label = definition.Label,
                Status = status,
                Code = metrics.HttpCode,
                UpBytes = metrics.UpBytes,
                DownBytes = metrics.DownBytes,
                TimeSeconds = metrics.TimeSeconds,
                Details = combined
            };
        }

        if (LooksLikeUnsupportedProtocol(process.ExitCode, combined))
        {
            return new TcpFreezeProtocolResult
            {
                Label = definition.Label,
                Status = TcpFreezeProtocolStatus.Unsupported,
                Code = "UNSUP",
                UpBytes = 0,
                DownBytes = 0,
                TimeSeconds = 0,
                Details = combined
            };
        }

        return new TcpFreezeProtocolResult
        {
            Label = definition.Label,
            Status = TcpFreezeProtocolStatus.Fail,
            Code = "ERR",
            UpBytes = 0,
            DownBytes = 0,
            TimeSeconds = 0,
            Details = combined
        };
    }

    private static ProcessStartInfo CreateCurlStartInfo(string host, string payloadPath, ProtocolDefinition definition)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--range");
        startInfo.ArgumentList.Add($"0-{RangeBytes - 1}");
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(TimeoutSeconds.ToString());
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("%{http_code} %{size_upload} %{size_download} %{time_total}");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("NUL");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("POST");
        startInfo.ArgumentList.Add("--data-binary");
        startInfo.ArgumentList.Add("@" + payloadPath);
        startInfo.ArgumentList.Add("-s");

        foreach (var argument in definition.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("https://" + host);
        return startInfo;
    }

    private static bool TryParseCurlMetrics(string text, out CurlMetrics metrics)
    {
        var match = Regex.Match(
            text,
            "^(?<code>\\d{3})\\s+(?<up>\\d+)\\s+(?<down>\\d+)\\s+(?<time>[\\d\\.]+)$",
            RegexOptions.CultureInvariant);

        if (match.Success &&
            long.TryParse(match.Groups["up"].Value, out var upBytes) &&
            long.TryParse(match.Groups["down"].Value, out var downBytes) &&
            double.TryParse(match.Groups["time"].Value, System.Globalization.CultureInfo.InvariantCulture, out var timeSeconds))
        {
            metrics = new CurlMetrics(match.Groups["code"].Value, upBytes, downBytes, timeSeconds);
            return true;
        }

        metrics = default;
        return false;
    }

    private static bool LooksLikeUnsupportedProtocol(int exitCode, string text)
    {
        return exitCode == 35 ||
               Regex.IsMatch(
                   text,
                   "not supported|does not support|protocol\\s+'.+'\\s+not\\s+supported|unsupported protocol|TLS.not supported|Unrecognized option|Unknown option|unsupported option|unsupported feature|schannel|SSL",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildTargetLogLine(TcpFreezeTargetResult result)
    {
        var parts = result.Checks
            .Select(check => $"{check.Label}:{ToShortStatus(check.Status, check.Code)}");
        return $"{result.Country} {result.Provider} [{result.TargetId}] {result.Host} -> {string.Join(" | ", parts)}";
    }

    private static string ToShortStatus(TcpFreezeProtocolStatus status, string code)
    {
        return status switch
        {
            TcpFreezeProtocolStatus.Ok => $"OK[{code}]",
            TcpFreezeProtocolStatus.LikelyBlocked => $"BLOCKED[{code}]",
            TcpFreezeProtocolStatus.Unsupported => $"UNSUP[{code}]",
            _ => $"FAIL[{code}]"
        };
    }

    private static TcpFreezeConfigResult BuildSummary(ConfigProfile profile, IReadOnlyList<TcpFreezeTargetResult> targetResults)
    {
        var okCount = 0;
        var failCount = 0;
        var unsupportedCount = 0;
        var blockedCount = 0;
        var blockedTargets = new List<string>();

        foreach (var target in targetResults)
        {
            var targetBlocked = false;
            foreach (var check in target.Checks)
            {
                switch (check.Status)
                {
                    case TcpFreezeProtocolStatus.Ok:
                        okCount++;
                        break;
                    case TcpFreezeProtocolStatus.LikelyBlocked:
                        blockedCount++;
                        targetBlocked = true;
                        break;
                    case TcpFreezeProtocolStatus.Unsupported:
                        unsupportedCount++;
                        break;
                    default:
                        failCount++;
                        break;
                }
            }

            if (targetBlocked)
            {
                blockedTargets.Add(target.Host);
            }
        }

        return new TcpFreezeConfigResult
        {
            ConfigName = profile.Name,
            FileName = profile.FileName,
            FilePath = profile.FilePath,
            OkCount = okCount,
            FailCount = failCount,
            UnsupportedCount = unsupportedCount,
            BlockedCount = blockedCount,
            BlockedTargets = blockedTargets,
            TargetResults = targetResults.ToArray()
        };
    }

    private static void EnsureCurlAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return;
            }
        }
        catch
        {
        }

        throw new InvalidOperationException("Для TCP 16-20 проверки нужен curl.exe в системе.");
    }

    private async Task StopInstallationAsync(ZapretInstallation installation, CancellationToken cancellationToken)
    {
        await _processService.StopAsync(installation);
        await WaitForWinwsStateAsync(installation, shouldBeRunning: false, cancellationToken);
    }

    private static async Task WaitForWinwsStateAsync(ZapretInstallation installation, bool shouldBeRunning, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var isRunning = Process.GetProcessesByName("winws")
                .Any(process =>
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        return !string.IsNullOrWhiteSpace(path) &&
                               path.StartsWith(installation.RootPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                });

            if (isRunning == shouldBeRunning)
            {
                return;
            }

            await Task.Delay(300, cancellationToken);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record ProtocolDefinition(string Label, string[] Arguments);

    private readonly record struct CurlMetrics(string HttpCode, long UpBytes, long DownBytes, double TimeSeconds);

    private sealed record SuiteTarget
    {
        public string Id { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string Country { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
    }

}
