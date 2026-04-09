using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class ConnectivityTestService
{
    private static readonly TimeSpan ProbeStartDelay = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan TypicalSilentStartDuration = TimeSpan.FromSeconds(4);
    private static readonly HttpClient DnsHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };
    private readonly ZapretProcessService _processService = new();
    private sealed record ProtocolProbeDefinition(string Label, string[] CurlArgs, SslProtocols? RequiredProtocols = null);
    private sealed record ProtocolProbeResult(
        string Label,
        bool Success,
        bool IsSupported,
        string StatusText,
        string Details,
        long? DurationMs,
        bool UsedDnsFallback = false);
    private sealed record DohResolutionResult(string? Address, string? ErrorMessage);

    public async Task<ConfigProbeResult> ProbeConfigAsync(
        ZapretInstallation installation,
        ConfigProfile profile,
        string? customTarget,
        string? dohTemplate = null,
        CancellationToken cancellationToken = default)
    {
        var targets = LoadTargets(installation, customTarget).ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException("Не найдено ни одной цели для проверки.");
        }

        var dohResolutionCache = new ConcurrentDictionary<string, Lazy<Task<DohResolutionResult>>>(
            StringComparer.OrdinalIgnoreCase);

        await _processService.StopAsync(installation);
        await WaitForWinwsStateAsync(installation, shouldBeRunning: false, cancellationToken);

        try
        {
            await _processService.StartAsync(installation, profile, silentMode: true);
            await WaitForWinwsStateAsync(installation, shouldBeRunning: true, cancellationToken);
            await Task.Delay(ProbeStartDelay, cancellationToken);

            var results = await Task.WhenAll(targets.Select(target => ProbeTargetAsync(target, dohTemplate, dohResolutionCache, cancellationToken)));
            var scoredResults = results
                .Where(result => !result.IsDiagnosticOnly)
                .ToArray();
            var primaryResults = results
                .Where(result => !result.IsDiagnosticOnly && !result.IsSupplementary)
                .ToArray();
            var supplementaryResults = results
                .Where(result => !result.IsDiagnosticOnly && result.IsSupplementary)
                .ToArray();
            var successCount = scoredResults.Count(result => result.Outcome == ProbeOutcomeKind.Success);
            var primarySuccessCount = primaryResults.Count(result => result.Outcome == ProbeOutcomeKind.Success);
            var supplementarySuccessCount = supplementaryResults.Count(result => result.Outcome == ProbeOutcomeKind.Success);

            var hardFailures = primaryResults
                .Where(result => result.Outcome == ProbeOutcomeKind.Failure)
                .Select(result => result.TargetName)
                .ToArray();
            var primaryPartials = primaryResults
                .Where(result => result.Outcome == ProbeOutcomeKind.Partial)
                .Select(result => result.TargetName)
                .ToArray();
            var supplementaryFailures = supplementaryResults
                .Where(result => result.Outcome != ProbeOutcomeKind.Success)
                .Select(result => result.TargetName)
                .ToArray();

            var softIssues = primaryPartials
                .Concat(supplementaryFailures)
                .ToArray();
            var hardSummaryNames = CollapseSummaryNames(hardFailures);
            var softSummaryNames = CollapseSummaryNames(softIssues);

            var outcome = BuildConfigOutcome(hardSummaryNames, softSummaryNames);
            var summary = BuildSummary(hardSummaryNames, softSummaryNames);
            var details = BuildDetails(outcome, hardSummaryNames, softSummaryNames);

            var latencyValues = results
                .Where(result => result.PingMilliseconds.HasValue)
                .Select(result => result.PingMilliseconds!.Value)
                .ToArray();

            return new ConfigProbeResult
            {
                ConfigName = profile.Name,
                Outcome = outcome,
                SuccessCount = successCount,
                TotalCount = scoredResults.Length,
                PartialCount = softIssues.Length,
                PrimarySuccessCount = primarySuccessCount,
                PrimaryTotalCount = primaryResults.Length,
                PrimaryPartialCount = primaryPartials.Length,
                SupplementarySuccessCount = supplementarySuccessCount,
                SupplementaryTotalCount = supplementaryResults.Length,
                Summary = summary,
                Details = details,
                FailedTargetNames = hardFailures.ToList(),
                PrimaryFailedTargetNames = hardFailures.ToList(),
                PartialTargetNames = softIssues.ToList(),
                PrimaryPartialTargetNames = primaryPartials.ToList(),
                SupplementaryFailedTargetNames = supplementaryFailures.ToList(),
                TargetResults = results
                    .OrderBy(result => GetSummarySortOrder(result.TargetName))
                    .ThenBy(result => GetDetailSortOrder(result.TargetName))
                    .ThenBy(result => result.TargetName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                AveragePingMilliseconds = latencyValues.Length == 0 ? null : (long)Math.Round(latencyValues.Average())
            };
        }
        finally
        {
            await _processService.StopAsync(installation);
            await WaitForWinwsStateAsync(installation, shouldBeRunning: false, CancellationToken.None);
        }
    }

    public TimeSpan GetEstimatedProfileProbeDuration()
    {
        return TypicalSilentStartDuration
               + ProbeStartDelay
               + ProbeTimeout
               + TimeSpan.FromMilliseconds(1200);
    }

    public IReadOnlyList<ConnectivityTarget> LoadTargets(ZapretInstallation installation, string? customTarget)
    {
        if (!string.IsNullOrWhiteSpace(customTarget))
        {
            return BuildCustomTargets(customTarget);
        }

        var targetsPath = Path.Combine(installation.UtilsPath, "targets.txt");
        var targets = new List<ConnectivityTarget>();

        if (File.Exists(targetsPath))
        {
            foreach (var rawLine in File.ReadLines(targetsPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var match = Regex.Match(line, "^(?<name>[\\p{L}\\p{N}_-]+)\\s*=\\s*\"(?<value>[^\"]+)\"$", RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                var target = BuildTarget(match.Groups["name"].Value, match.Groups["value"].Value, classifySupplementary: true);
                if (target is not null)
                {
                    targets.Add(target);
                }
            }
        }

        AddStrictYouTubeTargets(targets);
        AddStrictDiscordTargets(targets);
        return targets;
    }

    public IReadOnlyDictionary<string, ConnectivityTarget> BuildTargetMap(ZapretInstallation installation, string? customTarget)
    {
        return LoadTargets(installation, customTarget)
            .Where(target => !target.IsDiagnosticOnly && !target.IsSupplementary)
            .GroupBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ConnectivityTargetResult> ProbeTargetAsync(
        ConnectivityTarget target,
        string? dohTemplate,
        ConcurrentDictionary<string, Lazy<Task<DohResolutionResult>>> dohResolutionCache,
        CancellationToken cancellationToken)
    {
        if (target.Url is null)
        {
            return await ProbePingTargetAsync(target);
        }

        var pingTask = ProbePingHostAsync(target.PingHost);
        var primaryProtocolChecks = await Task.WhenAll(GetPrimaryProtocolDefinitions(target.Url ?? throw new InvalidOperationException("Для основной цели не указан URL."))
            .Select(definition => ProbePrimaryProtocolAsync(target.Url!, definition, dohTemplate, dohResolutionCache, cancellationToken)));
        var protocolChecks = ShouldProbeDiscordGatewayWebSocket(target)
            ? [.. primaryProtocolChecks, await ProbeDiscordGatewayWebSocketAsync(cancellationToken)]
            : primaryProtocolChecks;
        var pingMilliseconds = await pingTask;
        if ((!pingMilliseconds.HasValue || pingMilliseconds.Value <= 0) &&
            !string.IsNullOrWhiteSpace(dohTemplate) &&
            Uri.TryCreate(dohTemplate, UriKind.Absolute, out var dohUri))
        {
            var dohResolution = await ResolveHostViaDohAsync(target.PingHost, dohUri, dohResolutionCache, cancellationToken);
            if (!string.IsNullOrWhiteSpace(dohResolution.Address))
            {
                pingMilliseconds = await ProbePingAddressAsync(dohResolution.Address);
            }
        }

        var supportedChecks = protocolChecks
            .Where(result => result.IsSupported)
            .ToArray();
        var successCount = supportedChecks.Count(result => result.Success);
        var dnsFallbackCount = supportedChecks.Count(result => result.UsedDnsFallback);
        var outcome = supportedChecks.Length == 0
            ? ProbeOutcomeKind.Partial
            : successCount == supportedChecks.Length && dnsFallbackCount == 0
            ? ProbeOutcomeKind.Success
            : successCount > 0 || dnsFallbackCount > 0
                ? ProbeOutcomeKind.Partial
                : ProbeOutcomeKind.Failure;
        var latencyValues = protocolChecks
            .Where(result => result.DurationMs.HasValue)
            .Select(result => result.DurationMs!.Value)
            .ToArray();

        var effectivePing = pingMilliseconds.HasValue && pingMilliseconds.Value > 0
            ? pingMilliseconds
            : latencyValues.Length == 0
                ? null
                : (long)Math.Round(latencyValues.Average());

        return new ConnectivityTargetResult
        {
            TargetName = target.Name,
            Success = outcome == ProbeOutcomeKind.Success,
            Outcome = outcome,
            HasDnsFallback = dnsFallbackCount > 0,
            HttpStatus = string.Join(" ", protocolChecks.Select(result => $"{result.Label}:{result.StatusText}")),
            Details = BuildPrimaryProtocolDetails(target.Name, outcome, protocolChecks),
            PingMilliseconds = effectivePing,
            IsDiagnosticOnly = target.IsDiagnosticOnly,
            IsSupplementary = target.IsSupplementary
        };
    }

    private static async Task<ProtocolProbeResult> ProbePrimaryProtocolAsync(
        Uri targetUri,
        ProtocolProbeDefinition definition,
        string? dohTemplate,
        ConcurrentDictionary<string, Lazy<Task<DohResolutionResult>>> dohResolutionCache,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var process = new Process
            {
                StartInfo = CreateCurlStartInfo(targetUri, definition)
            };
            process.Start();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            stopwatch.Stop();
            var standardOutput = (await standardOutputTask).Trim();
            var standardError = (await standardErrorTask).Trim();

            if (IsDnsResolutionFailure(standardOutput, standardError))
            {
                var fallbackResult = await TryProbeViaDohAsync(targetUri, definition, dohTemplate, dohResolutionCache, cancellationToken);
                if (fallbackResult is not null)
                {
                    return fallbackResult;
                }

                return new ProtocolProbeResult(
                    definition.Label,
                    Success: false,
                    IsSupported: true,
                    StatusText: "DNS",
                    Details: BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                    DurationMs: null);
            }

            if (IsCertificateFailure(standardOutput, standardError))
            {
                return new ProtocolProbeResult(
                    definition.Label,
                    Success: false,
                    IsSupported: true,
                    StatusText: "SSL",
                    Details: BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                    DurationMs: null);
            }

            var unsupported = IsUnsupportedProtocol(process.ExitCode, standardOutput, standardError);
            if (unsupported)
            {
                return new ProtocolProbeResult(
                    definition.Label,
                    Success: false,
                    IsSupported: false,
                    StatusText: "UNSUP",
                    Details: BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                    DurationMs: null);
            }

            var success = TryParseCurlProbeOutput(standardOutput, process.ExitCode, out var httpCode);

            return new ProtocolProbeResult(
                definition.Label,
                Success: success,
                IsSupported: true,
                StatusText: success ? "OK" : "ERROR",
                Details: success
                    ? $"HTTP {httpCode}"
                    : BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                DurationMs: success ? stopwatch.ElapsedMilliseconds : null);
        }
        catch (Exception ex)
        {
            return new ProtocolProbeResult(
                definition.Label,
                Success: false,
                IsSupported: true,
                StatusText: "ERROR",
                Details: ex.Message,
                DurationMs: null);
        }
    }

    private static ProcessStartInfo CreateCurlStartInfo(Uri targetUri, ProtocolProbeDefinition definition, string? resolvedAddress = null)
    {
        var startInfo = new ProcessStartInfo("curl.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var argument in GetCurlArguments(targetUri, definition, resolvedAddress))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static IEnumerable<string> GetCurlArguments(Uri targetUri, ProtocolProbeDefinition definition, string? resolvedAddress = null)
    {
        yield return "-I";
        yield return "-s";
        yield return "-m";
        yield return ((int)Math.Ceiling(ProbeTimeout.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        yield return "-o";
        yield return "NUL";
        yield return "-w";
        yield return "%{http_code}";
        yield return "--show-error";

        foreach (var argument in definition.CurlArgs)
        {
            yield return argument;
        }

        if (!string.IsNullOrWhiteSpace(resolvedAddress))
        {
            yield return "--resolve";
            yield return $"{targetUri.Host}:{targetUri.Port}:{resolvedAddress}";
        }

        yield return targetUri.AbsoluteUri;
    }

    private static bool TryParseCurlProbeOutput(string output, int exitCode, out string httpCode)
    {
        httpCode = "ERR";

        if (exitCode != 0)
        {
            return false;
        }

        var parts = output
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            httpCode = "000";
            return true;
        }

        if (!Regex.IsMatch(parts[0], "^\\d{3}$"))
        {
            return false;
        }

        httpCode = parts[0];
        return true;
    }

    private static bool IsUnsupportedProtocol(int exitCode, string standardOutput, string standardError)
    {
        return exitCode == 35 ||
               Regex.IsMatch(
                   $"{standardOutput} {standardError}",
                   "does not support|not supported|protocol\\s+'?.+'?\\s+not\\s+supported|unsupported protocol|TLS.*not supported|Unrecognized option|Unknown option|unsupported option|unsupported feature|schannel",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsDnsResolutionFailure(string standardOutput, string standardError)
    {
        return Regex.IsMatch(
            $"{standardOutput} {standardError}",
            "Could not resolve host|No such host is known|Couldn't resolve host|Temporary failure in name resolution|operation refused|DNS.*refused|resolve.*denied|name resolution",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsCertificateFailure(string standardOutput, string standardError)
    {
        return Regex.IsMatch(
            $"{standardOutput} {standardError}",
            "certificate|SSL certificate problem|self[- ]?signed|certificate verify failed|unable to get local issuer certificate|schannel.*certificate|tlsv1 alert",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static async Task<ProtocolProbeResult?> TryProbeViaDohAsync(
        Uri targetUri,
        ProtocolProbeDefinition definition,
        string? dohTemplate,
        ConcurrentDictionary<string, Lazy<Task<DohResolutionResult>>> dohResolutionCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dohTemplate) || !Uri.TryCreate(dohTemplate, UriKind.Absolute, out var dohUri))
        {
            return null;
        }

        var resolution = await ResolveHostViaDohAsync(targetUri.Host, dohUri, dohResolutionCache, cancellationToken);
        if (string.IsNullOrWhiteSpace(resolution.Address))
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var process = new Process
            {
                StartInfo = CreateCurlStartInfo(targetUri, definition, resolution.Address)
            };
            process.Start();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            stopwatch.Stop();

            var standardOutput = (await standardOutputTask).Trim();
            var standardError = (await standardErrorTask).Trim();
            var unsupported = IsUnsupportedProtocol(process.ExitCode, standardOutput, standardError);
            if (unsupported)
            {
                return new ProtocolProbeResult(
                    definition.Label,
                    Success: false,
                    IsSupported: false,
                    StatusText: "UNSUP",
                    Details: BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                    DurationMs: null);
            }

            if (IsCertificateFailure(standardOutput, standardError))
            {
                return new ProtocolProbeResult(
                    definition.Label,
                    Success: false,
                    IsSupported: true,
                    StatusText: "SSL",
                    Details: BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError),
                    DurationMs: null);
            }

            var success = TryParseCurlProbeOutput(standardOutput, process.ExitCode, out var httpCode);
            return new ProtocolProbeResult(
                definition.Label,
                Success: success,
                IsSupported: true,
                StatusText: "DNS",
                Details: success
                    ? $"Системный DNS не прошёл, но цель подтверждена через DoH fallback ({resolution.Address}), HTTP {httpCode}"
                    : $"DoH fallback ({resolution.Address}) не помог: {BuildCurlFailureDetails(process.ExitCode, standardOutput, standardError)}",
                DurationMs: success ? stopwatch.ElapsedMilliseconds : null,
                UsedDnsFallback: success);
        }
        catch (Exception ex)
        {
            return new ProtocolProbeResult(
                definition.Label,
                Success: false,
                IsSupported: true,
                StatusText: "DNS",
                Details: $"DoH fallback не сработал: {ex.Message}",
                DurationMs: null);
        }
    }

    private static Task<DohResolutionResult> ResolveHostViaDohAsync(
        string host,
        Uri dohUri,
        ConcurrentDictionary<string, Lazy<Task<DohResolutionResult>>> dohResolutionCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{dohUri}|{host}";
        var lazyTask = dohResolutionCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<DohResolutionResult>>(
                () => ResolveHostViaDohCoreAsync(host, dohUri, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyTask.Value;
    }

    private static async Task<DohResolutionResult> ResolveHostViaDohCoreAsync(string host, Uri dohUri, CancellationToken cancellationToken)
    {
        try
        {
            var ipv4 = await QueryDohAddressAsync(host, dohUri, type: 1, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ipv4))
            {
                return new DohResolutionResult(ipv4, null);
            }

            var ipv6 = await QueryDohAddressAsync(host, dohUri, type: 28, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ipv6))
            {
                return new DohResolutionResult(ipv6, null);
            }

            return new DohResolutionResult(null, "DoH не вернул ни одного IP-адреса.");
        }
        catch (Exception ex)
        {
            return new DohResolutionResult(null, ex.Message);
        }
    }

    private static async Task<string?> QueryDohAddressAsync(string host, Uri dohUri, ushort type, CancellationToken cancellationToken)
    {
        var requestUri = BuildDohRequestUri(dohUri, BuildDnsQuery(host, type));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("accept", "application/dns-message");

        using var response = await DnsHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return TryExtractDnsAddress(payload, type);
    }

    private static Uri BuildDohRequestUri(Uri dohUri, byte[] dnsQuery)
    {
        var separator = string.IsNullOrEmpty(dohUri.Query) ? "?" : "&";
        var encoded = Convert.ToBase64String(dnsQuery)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new Uri($"{dohUri}{separator}dns={encoded}");
    }

    private static byte[] BuildDnsQuery(string host, ushort type)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x01);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x01);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);
        stream.WriteByte(0x00);

        foreach (var label in host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)labelBytes.Length);
            stream.Write(labelBytes, 0, labelBytes.Length);
        }

        stream.WriteByte(0x00);
        stream.WriteByte((byte)(type >> 8));
        stream.WriteByte((byte)(type & 0xff));
        stream.WriteByte(0x00);
        stream.WriteByte(0x01);
        return stream.ToArray();
    }

    private static string? TryExtractDnsAddress(byte[] payload, ushort expectedType)
    {
        if (payload.Length < 12)
        {
            return null;
        }

        var questionCount = ReadUInt16(payload, 4);
        var answerCount = ReadUInt16(payload, 6);
        var offset = 12;

        for (var index = 0; index < questionCount; index++)
        {
            if (!TrySkipDnsName(payload, offset, out offset) || offset + 4 > payload.Length)
            {
                return null;
            }

            offset += 4;
        }

        for (var index = 0; index < answerCount; index++)
        {
            if (!TrySkipDnsName(payload, offset, out offset) || offset + 10 > payload.Length)
            {
                return null;
            }

            var answerType = ReadUInt16(payload, offset);
            offset += 2;
            offset += 2;
            offset += 4;
            var rdLength = ReadUInt16(payload, offset);
            offset += 2;

            if (offset + rdLength > payload.Length)
            {
                return null;
            }

            if (answerType == expectedType)
            {
                if (answerType == 1 && rdLength == 4)
                {
                    return new IPAddress(payload[offset..(offset + 4)]).ToString();
                }

                if (answerType == 28 && rdLength == 16)
                {
                    return new IPAddress(payload[offset..(offset + 16)]).ToString();
                }
            }

            offset += rdLength;
        }

        return null;
    }

    private static bool TrySkipDnsName(byte[] payload, int offset, out int nextOffset)
    {
        nextOffset = offset;
        var jumps = 0;

        while (nextOffset < payload.Length)
        {
            var length = payload[nextOffset];
            if (length == 0)
            {
                nextOffset++;
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (nextOffset + 1 >= payload.Length)
                {
                    return false;
                }

                nextOffset += 2;
                return true;
            }

            nextOffset += length + 1;
            jumps++;
            if (jumps > 128)
            {
                return false;
            }
        }

        return false;
    }

    private static ushort ReadUInt16(byte[] payload, int offset)
    {
        return (ushort)((payload[offset] << 8) | payload[offset + 1]);
    }

    private static string BuildCurlFailureDetails(int exitCode, string standardOutput, string standardError)
    {
        if (!string.IsNullOrWhiteSpace(standardError))
        {
            return $"curl exit {exitCode}: {standardError.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            return $"curl exit {exitCode}: {standardOutput.Trim()}";
        }

        return $"curl exit {exitCode}";
    }

    private static async Task<long?> ProbePingHostAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var samples = new List<long>(3);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var reply = await ping.SendPingAsync(host, 700);
                if (reply.Status == IPStatus.Success)
                {
                    samples.Add(reply.RoundtripTime);
                }
            }

            return samples.Count == 0
                ? null
                : (long)Math.Round(samples.Average());
        }
        catch
        {
            return null;
        }
    }

    private static async Task<long?> ProbePingAddressAsync(string address)
    {
        try
        {
            using var ping = new Ping();
            var samples = new List<long>(3);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var reply = await ping.SendPingAsync(address, 700);
                if (reply.Status == IPStatus.Success)
                {
                    samples.Add(reply.RoundtripTime);
                }
            }

            return samples.Count == 0
                ? null
                : NormalizeDisplayedPing((long)Math.Round(samples.Average()));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ConnectivityTargetResult> ProbePingTargetAsync(ConnectivityTarget target)
    {
        try
        {
            using var ping = new Ping();
            var samples = new List<long>(3);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var reply = await ping.SendPingAsync(target.PingHost, 700);
                if (reply.Status == IPStatus.Success)
                {
                    samples.Add(reply.RoundtripTime);
                }
            }

            var success = samples.Count > 0;
            return new ConnectivityTargetResult
            {
                TargetName = target.Name,
                Success = success,
                Outcome = success ? ProbeOutcomeKind.Success : ProbeOutcomeKind.Failure,
                HttpStatus = success ? "PING OK" : "Timeout",
                PingMilliseconds = success ? NormalizeDisplayedPing((long)Math.Round(samples.Average())) : null,
                IsDiagnosticOnly = target.IsDiagnosticOnly,
                IsSupplementary = target.IsSupplementary
            };
        }
        catch (Exception ex)
        {
            return new ConnectivityTargetResult
            {
                TargetName = target.Name,
                Success = false,
                Outcome = ProbeOutcomeKind.Failure,
                HttpStatus = ex.GetType().Name,
                Details = ex.Message,
                IsDiagnosticOnly = target.IsDiagnosticOnly,
                IsSupplementary = target.IsSupplementary
            };
        }
    }

    private static bool ShouldProbeDiscordGatewayWebSocket(ConnectivityTarget target)
    {
        return string.Equals(target.Name, "DiscordGateway", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(target.PingHost, "gateway.discord.gg", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProtocolProbeResult> ProbeDiscordGatewayWebSocketAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            var stopwatch = Stopwatch.StartNew();
            await socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), timeoutCts.Token);
            stopwatch.Stop();

            var success = socket.State == WebSocketState.Open;
            return new ProtocolProbeResult(
                "WS",
                Success: success,
                IsSupported: true,
                StatusText: success ? "OK" : "ERROR",
                Details: success
                    ? "WebSocket gateway доступен."
                    : $"WebSocket gateway вернул состояние {socket.State}.",
                DurationMs: success ? stopwatch.ElapsedMilliseconds : null);
        }
        catch (Exception ex)
        {
            return new ProtocolProbeResult(
                "WS",
                Success: false,
                IsSupported: true,
                StatusText: "ERROR",
                Details: $"WebSocket gateway недоступен: {ex.Message}",
                DurationMs: null);
        }
    }

    private static long NormalizeDisplayedPing(long pingMilliseconds)
    {
        return pingMilliseconds <= 0
            ? 1
            : pingMilliseconds;
    }

    private static ConnectivityTarget? BuildTarget(string name, string value, bool classifySupplementary)
    {
        if (value.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectivityTarget
            {
                Name = name,
                PingHost = value["PING:".Length..].Trim(),
                IsDiagnosticOnly = true
            };
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return new ConnectivityTarget
            {
                Name = name,
                Url = uri,
                PingHost = uri.Host,
                IsDiagnosticOnly = false,
                IsSupplementary = classifySupplementary && IsSupplementaryDefaultTarget(name, uri)
            };
        }

        return null;
    }

    private static IReadOnlyList<ConnectivityTarget> BuildCustomTargets(string customTarget)
    {
        var values = customTarget
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (values.Length == 0)
        {
            throw new InvalidOperationException("Введите домен или URL в понятном формате.");
        }

        var targets = new List<ConnectivityTarget>();
        foreach (var value in values)
        {
            var normalized = value.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            var target = BuildTarget(ToFriendlyCustomTargetName(value.Trim(), normalized), normalized, classifySupplementary: false);
            if (target is not null)
            {
                targets.Add(target);
            }
        }

        if (targets.Count == 1 && targets[0].PingHost.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            targets.Clear();
            targets.AddRange(new[]
            {
                new ConnectivityTarget { Name = "YouTube Main", Url = new Uri("https://www.youtube.com"), PingHost = "www.youtube.com" },
                new ConnectivityTarget { Name = "YouTube Watch", Url = new Uri("https://www.youtube.com/watch?v=jNQXAC9IVRw"), PingHost = "www.youtube.com" },
                new ConnectivityTarget { Name = "YouTube Short", Url = new Uri("https://youtu.be/jNQXAC9IVRw"), PingHost = "youtu.be" },
                new ConnectivityTarget { Name = "YouTube Image", Url = new Uri("https://i.ytimg.com"), PingHost = "i.ytimg.com" },
                new ConnectivityTarget { Name = "GoogleVideo Redirect", Url = new Uri("https://redirector.googlevideo.com"), PingHost = "redirector.googlevideo.com" }
            });
        }

        return targets;
    }

    private static void AddStrictYouTubeTargets(List<ConnectivityTarget> targets)
    {
        if (!targets.Any(target => target.Name.Equals("YouTubeWatch", StringComparison.OrdinalIgnoreCase)))
        {
            targets.Add(new ConnectivityTarget
            {
                Name = "YouTubeWatch",
                Url = new Uri("https://www.youtube.com/watch?v=jNQXAC9IVRw"),
                PingHost = "www.youtube.com"
            });
        }
    }

    private static void AddStrictDiscordTargets(List<ConnectivityTarget> targets)
    {
        if (!targets.Any(target => target.Name.Equals("DiscordGateway", StringComparison.OrdinalIgnoreCase)))
        {
            targets.Add(new ConnectivityTarget
            {
                Name = "DiscordGateway",
                Url = new Uri("https://gateway.discord.gg"),
                PingHost = "gateway.discord.gg"
            });
        }
    }

    private static string[] DistinctNames(IEnumerable<string> names)
    {
        var result = new List<string>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(name);
        }

        return [.. result];
    }

    private static string[] CollapseSummaryNames(IEnumerable<string> names)
        => DistinctNames(names.Select(ToSummaryDisplayName));

    private static string BuildSummary(string[] hardFailures, string[] softIssues)
    {
        if (hardFailures.Length == 0 && softIssues.Length == 0)
        {
            return "✓";
        }

        if (hardFailures.Length > 0)
        {
            var visibleFailures = hardFailures.Take(4).ToArray();
            var hiddenFailureCount = Math.Max(0, hardFailures.Length - visibleFailures.Length);
            return hiddenFailureCount > 0
                ? $"✕ {string.Join(", ", visibleFailures)} +{hiddenFailureCount}"
                : $"✕ {string.Join(", ", visibleFailures)}";
        }

        var visibleIssues = softIssues.Take(4).ToArray();
        var hiddenIssueCount = Math.Max(0, softIssues.Length - visibleIssues.Length);
        return hiddenIssueCount > 0
            ? $"! {string.Join(", ", visibleIssues)} +{hiddenIssueCount}"
            : $"! {string.Join(", ", visibleIssues)}";
    }

    private static string BuildDetails(
        ProbeOutcomeKind outcome,
        string[] hardSummaryNames,
        string[] softSummaryNames)
    {
        if (outcome == ProbeOutcomeKind.Success)
        {
            return "✓ Все цели доступны.";
        }

        return outcome == ProbeOutcomeKind.Failure
            ? $"✕ Проблемы: {FormatFailureList(hardSummaryNames, 4)}."
            : $"! Есть ограничения: {FormatFailureList(softSummaryNames, 4)}.";
    }

    private static string FormatFailureList(string[] failures, int visibleCount)
    {
        var visibleFailures = failures.Take(visibleCount).ToArray();
        var hiddenFailureCount = Math.Max(0, failures.Length - visibleFailures.Length);
        if (hiddenFailureCount <= 0)
        {
            return string.Join(", ", visibleFailures);
        }

        return $"{string.Join(", ", visibleFailures)} и ещё {hiddenFailureCount}";
    }

    private static ProbeOutcomeKind BuildConfigOutcome(string[] hardFailures, string[] softIssues)
    {
        if (hardFailures.Length > 0)
        {
            return ProbeOutcomeKind.Failure;
        }

        return softIssues.Length > 0
            ? ProbeOutcomeKind.Partial
            : ProbeOutcomeKind.Success;
    }

    private static ProtocolProbeDefinition[] GetPrimaryProtocolDefinitions(Uri targetUri)
    {
        return string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ?
            [
                new ProtocolProbeDefinition("HTTP", ["--http1.1"]),
                new ProtocolProbeDefinition("TLS1.2", ["--tlsv1.2", "--tls-max", "1.2"], SslProtocols.Tls12),
                new ProtocolProbeDefinition("TLS1.3", ["--tlsv1.3", "--tls-max", "1.3"], SslProtocols.Tls13)
            ]
            :
            [new ProtocolProbeDefinition("HTTP", ["--http1.1"])];
    }

    private static async Task WaitForWinwsStateAsync(ZapretInstallation installation, bool shouldBeRunning, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
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
                });

            if (isRunning == shouldBeRunning)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static bool IsSupplementaryDefaultTarget(string name, Uri uri)
    {
        var normalizedHost = NormalizeHost(uri.Host);

        if (name.EndsWith("CDN", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedHost switch
        {
            "updates.discord.com" => true,
            "i.ytimg.com" => true,
            "redirector.googlevideo.com" => true,
            "gstatic.com" => true,
            "static-cdn.jtvnw.net" => true,
            _ => false
        };
    }

    private static string NormalizeHost(string host)
    {
        var normalizedHost = host.Trim().ToLowerInvariant();
        return normalizedHost.StartsWith("www.", StringComparison.Ordinal)
            ? normalizedHost["www.".Length..]
            : normalizedHost;
    }

    private static string ToFriendlyCustomTargetName(string originalName, string normalizedValue)
    {
        if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri))
        {
            return originalName;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        return host switch
        {
            "www.youtube.com" when path.Contains("/watch") => "YouTubeWatch",
            "www.youtube.com" => "YouTubeWeb",
            "youtu.be" => "YouTubeShort",
            "i.ytimg.com" => "YouTubeImage",
            "redirector.googlevideo.com" => "YouTubeVideoRedirect",
            "discord.com" => "DiscordMain",
            "gateway.discord.gg" => "DiscordGateway",
            "cdn.discordapp.com" => "DiscordCDN",
            "updates.discord.com" => "DiscordUpdates",
            "google.com" => "GoogleMain",
            "gstatic.com" => "GoogleGstatic",
            "cloudflare.com" => "CloudflareWeb",
            "cdnjs.cloudflare.com" => "CloudflareCDN",
            "instagram.com" or "www.instagram.com" => "InstagramMain",
            "cdninstagram.com" => "InstagramCDN",
            "tiktok.com" or "www.tiktok.com" => "TikTokMain",
            "tiktokcdn.com" => "TikTokCDN",
            "x.com" or "twitter.com" => "XMain",
            "twimg.com" => "XCDN",
            "twitch.tv" or "www.twitch.tv" => "TwitchMain",
            "static-cdn.jtvnw.net" => "TwitchCDN",
            _ => originalName
        };
    }

    private static int GetSummarySortOrder(string targetName)
    {
        return ToSummaryDisplayName(targetName) switch
        {
            "Discord" => 0,
            "YouTube" => 1,
            "Google" => 2,
            "Cloudflare" => 3,
            "Instagram" => 4,
            "TikTok" => 5,
            "X / Twitter" => 6,
            "Twitch" => 7,
            _ => 20
        };
    }

    public static int GetDetailSortOrder(string targetName)
    {
        return targetName switch
        {
            "DiscordMain" => 0,
            "DiscordGateway" => 1,
            "DiscordCDN" => 2,
            "DiscordUpdates" => 3,
            "YouTubeWeb" => 10,
            "YouTubeShort" => 11,
            "YouTubeWatch" => 12,
            "YouTubeImage" => 13,
            "YouTubeVideoRedirect" => 14,
            "GoogleMain" => 20,
            "GoogleGstatic" => 21,
            "CloudflareWeb" => 30,
            "CloudflareCDN" => 31,
            "CloudflareDNS1111" => 40,
            "CloudflareDNS1001" => 41,
            "GoogleDNS8888" => 42,
            "GoogleDNS8844" => 43,
            "Quad9DNS9999" => 44,
            _ => 100
        };
    }

    public static string ToSummaryDisplayName(string targetName)
    {
        return targetName switch
        {
            "DiscordMain" or "DiscordGateway" or "DiscordCDN" or "DiscordUpdates" => "Discord",
            "YouTubeWeb" or "YouTubeShort" or "YouTubeImage" or "YouTubeVideoRedirect" or "YouTubeWatch" => "YouTube",
            "GoogleMain" or "GoogleGstatic" => "Google",
            "CloudflareWeb" or "CloudflareCDN" => "Cloudflare",
            "InstagramMain" or "InstagramCDN" => "Instagram",
            "TikTokMain" or "TikTokCDN" => "TikTok",
            "XMain" or "XCDN" => "X / Twitter",
            "TwitchMain" or "TwitchCDN" => "Twitch",
            _ => ToFriendlyTargetName(targetName)
        };
    }

    public static string ToFriendlyTargetName(string targetName)
    {
        return targetName switch
        {
            "DiscordMain" => "Discord",
            "DiscordGateway" => "Discord Gateway",
            "DiscordCDN" => "Discord CDN",
            "DiscordUpdates" => "Discord Updates",
            "YouTubeWeb" => "YouTube Web",
            "YouTubeShort" => "YouTube Short",
            "YouTubeImage" => "YouTube Image",
            "YouTubeVideoRedirect" => "YouTube Video Redirect",
            "YouTubeWatch" => "YouTube Watch",
            "GoogleMain" => "Google Main",
            "GoogleGstatic" => "Google Gstatic",
            "CloudflareWeb" => "Cloudflare Web",
            "CloudflareCDN" => "Cloudflare CDN",
            _ => targetName
        };
    }

    private static string BuildPrimaryProtocolDetails(string targetName, ProbeOutcomeKind outcome, IReadOnlyList<ProtocolProbeResult> protocolResults)
    {
        var dnsFallbackLabels = protocolResults
            .Where(result => result.UsedDnsFallback)
            .Select(result => result.Label)
            .ToArray();

        if (dnsFallbackLabels.Length > 0 && outcome == ProbeOutcomeKind.Partial)
        {
            return $"{ToFriendlyTargetName(targetName)}: доступ подтверждён только через DNS fallback ({string.Join(", ", dnsFallbackLabels)}).";
        }

        if (outcome == ProbeOutcomeKind.Success)
        {
            return string.Empty;
        }

        var summary = string.Join(", ", protocolResults.Select(result => $"{result.Label}:{result.StatusText}"));
        return outcome == ProbeOutcomeKind.Partial
            ? $"{ToFriendlyTargetName(targetName)}: работает частично ({summary})."
            : $"{ToFriendlyTargetName(targetName)}: не отвечает ({summary}).";
    }
}
