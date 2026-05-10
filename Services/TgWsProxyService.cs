using System.Diagnostics;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class TgWsProxyService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Flowseal/tg-ws-proxy/releases/latest";
    private const string AllReleasesPageUrl = "https://github.com/Flowseal/tg-ws-proxy/releases";
    private const string LatestReleasePageUrl = "https://github.com/Flowseal/tg-ws-proxy/releases/latest";
    private const string ExpandedAssetsUrl = "https://github.com/Flowseal/tg-ws-proxy/releases/expanded_assets/";
    private const string CfProxyDomainsUrl = "https://raw.githubusercontent.com/Flowseal/tg-ws-proxy/main/.github/cfproxy-domains.txt";
    private const string AutoStartRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PrimaryAutoStartValueName = "TgWsProxy";
    private static readonly string[] ExecutableFileNames = ["TgWsProxy.exe", "TgWsProxy_windows.exe"];
    private static readonly string[] ProcessNames = ["TgWsProxy", "TgWsProxy_windows"];
    private static readonly string[] AutoStartValueNames = [PrimaryAutoStartValueName, "TG WS Proxy", "ZapretManager.TgWsProxy"];
    private static readonly string[] DefaultEncodedCfProxyDomains =
    [
        "virkgj.com",
        "vmmzovy.com",
        "mkuosckvso.com",
        "zaewayzmplad.com",
        "twdmbzcm.com",
        "awzwsldi.com",
        "clngqrflngqin.com",
        "tjacxbqtj.com",
        "bxaxtxmrw.com",
        "dmohrsgmohcrwb.com"
    ];
    private static readonly int[] CfProxyTestDcIds = [1, 2, 3, 4, 5, 203];
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public sealed record TgWsProxyCfProxyDcResult(int DcId, bool IsSuccess, string Message);
    public sealed record TgWsProxyCfProxyTestResult(string? Domain, IReadOnlyList<TgWsProxyCfProxyDcResult> Results, bool UsedCustomDomain);

    public string ManagedComponentDirectoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZapretManager",
            "Components",
            "TgWsProxy");

    public string ManagedExecutablePath => Path.Combine(ManagedComponentDirectoryPath, "TgWsProxy.exe");

    public string ConfigDirectoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TgWsProxy");

    public string ConfigPath => Path.Combine(ConfigDirectoryPath, "config.json");

    public string LogPath => Path.Combine(ConfigDirectoryPath, "proxy.log");

    public string FirstRunMarkerPath => Path.Combine(ConfigDirectoryPath, ".first_run_done_mtproto");

    public string ReleasePageUrl => AllReleasesPageUrl;

    public string CfProxyGuideUrl => "https://github.com/Flowseal/tg-ws-proxy/blob/main/docs/CfProxy.md";

    public TgWsProxyConfig LoadConfig()
    {
        var config = CreateDefaultConfig();
        if (!File.Exists(ConfigPath))
        {
            return config;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject;
            if (root is null)
            {
                return config;
            }

            config.Host = ReadString(root, "host", config.Host);
            config.Port = ReadInt(root, "port", config.Port);
            config.Secret = NormalizeSecret(ReadString(root, "secret", config.Secret));
            config.DcIpRules = ReadStringList(root, "dc_ip", config.DcIpRules);
            config.EnableCfProxy = ReadBool(root, "cfproxy", config.EnableCfProxy);
            config.PreferCfProxy = ReadBool(root, "cfproxy_priority", config.PreferCfProxy);
            config.UserCfProxyDomain = ReadString(root, "cfproxy_user_domain", config.UserCfProxyDomain);
            config.VerboseLogging = ReadBool(root, "verbose", config.VerboseLogging);
            config.AutoStart = ReadBool(root, "autostart", config.AutoStart);
            config.LogMaxMegabytes = ReadInt(root, "log_max_mb", config.LogMaxMegabytes);
            config.BufferKilobytes = ReadInt(root, "buf_kb", config.BufferKilobytes);
            config.PoolSize = ReadInt(root, "pool_size", config.PoolSize);
        }
        catch
        {
            return config;
        }

        if (string.IsNullOrWhiteSpace(config.Secret))
        {
            config.Secret = GenerateSecret();
        }

        return config;
    }

    public void SaveConfig(TgWsProxyConfig config)
    {
        Directory.CreateDirectory(ConfigDirectoryPath);

        var payload = new JsonObject
        {
            ["port"] = config.Port,
            ["host"] = config.Host,
            ["secret"] = NormalizeSecret(config.Secret),
            ["dc_ip"] = new JsonArray(config.DcIpRules.Select(static item => JsonValue.Create(item)).ToArray()),
            ["verbose"] = config.VerboseLogging,
            ["check_updates"] = false,
            ["log_max_mb"] = config.LogMaxMegabytes,
            ["buf_kb"] = config.BufferKilobytes,
            ["pool_size"] = config.PoolSize,
            ["cfproxy"] = config.EnableCfProxy,
            ["cfproxy_priority"] = config.PreferCfProxy,
            ["cfproxy_user_domain"] = config.UserCfProxyDomain,
            ["autostart"] = config.AutoStart
        };

        File.WriteAllText(ConfigPath, payload.ToJsonString(JsonOptions));
        EnsureFirstRunMarkerExists();
    }

    public bool IsAutoStartEnabled(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(AutoStartRunKeyPath, writable: false);
        if (key is null)
        {
            return false;
        }

        var expectedCommand = BuildAutoStartCommand(executablePath);
        return AutoStartValueNames.Any(name =>
            string.Equals(
                (key.GetValue(name) as string)?.Trim(),
                expectedCommand.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }

    public void SetAutoStartEnabled(string? executablePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AutoStartRunKeyPath)
                        ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows для TG WS Proxy.");
        RemoveAutoStartValues(key);

        if (!enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Не удалось включить автозапуск TG WS Proxy: exe не найден.");
        }

        key.SetValue(PrimaryAutoStartValueName, BuildAutoStartCommand(executablePath), RegistryValueKind.String);
    }

    public string? ResolveExecutablePath(string? preferredPath = null)
    {
        foreach (var candidate in EnumerateCandidateExecutablePaths(preferredPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public bool IsManagedExecutable(string? executablePath)
    {
        return !string.IsNullOrWhiteSpace(executablePath) &&
               string.Equals(
                   Path.GetFullPath(executablePath),
                   Path.GetFullPath(ManagedExecutablePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    public string GetInstallTargetPath(string? resolvedExecutablePath)
    {
        return string.IsNullOrWhiteSpace(resolvedExecutablePath)
            ? ManagedExecutablePath
            : Path.GetFullPath(resolvedExecutablePath);
    }

    public string? GetInstalledVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(executablePath);
        return FirstNonEmpty(info.ProductVersion, info.FileVersion);
    }

    public string? GetRunningExecutablePath()
    {
        foreach (var process in EnumerateTgWsProxyProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return Path.GetFullPath(path);
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

        return null;
    }

    public bool IsRunning(string? executablePath)
    {
        return FindProcessesForExecutable(executablePath).Count > 0;
    }

    public async Task<TgWsProxyReleaseInfo> GetReleaseInfoAsync(
        string? currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetReleaseInfoFromApiAsync(currentVersion, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
        {
            try
            {
                return await GetReleaseInfoFromHtmlAsync(currentVersion, cancellationToken);
            }
            catch (Exception fallbackEx) when (NetworkErrorTranslator.IsNetworkException(fallbackEx))
            {
                throw NetworkErrorTranslator.CreateGitHubException(fallbackEx, "Проверка обновления TG WS Proxy");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, "Проверка обновления TG WS Proxy");
        }
    }

    private async Task<TgWsProxyReleaseInfo> GetReleaseInfoFromApiAsync(string? currentVersion, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidOperationException("GitHub вернул пустые данные по релизу TG WS Proxy.");

        var tagName = root["tag_name"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("GitHub вернул релиз TG WS Proxy без версии.");
        }

        var latestVersion = NormalizeVersion(tagName);
        var publishedAt = TryReadDateTimeOffset(root["published_at"]?.GetValue<string>());
        var asset = SelectWindowsAsset(root["assets"] as JsonArray);
        return BuildReleaseInfo(
            currentVersion,
            latestVersion,
            root["html_url"]?.GetValue<string>()?.Trim() ?? LatestReleasePageUrl,
            publishedAt,
            asset?.BrowserDownloadUrl,
            asset?.Name);
    }

    private async Task<TgWsProxyReleaseInfo> GetReleaseInfoFromHtmlAsync(string? currentVersion, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(LatestReleasePageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tagName = ExtractTagName(response);
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("GitHub не вернул тег последнего релиза TG WS Proxy.");
        }

        var latestVersion = NormalizeVersion(tagName);
        var html = await HttpClient.GetStringAsync(ExpandedAssetsUrl + tagName, cancellationToken);
        var asset = SelectWindowsAssetFromHtml(html);

        return BuildReleaseInfo(
            currentVersion,
            latestVersion,
            $"https://github.com/Flowseal/tg-ws-proxy/releases/tag/{tagName}",
            publishedAt: null,
            asset?.BrowserDownloadUrl,
            asset?.Name);
    }

    private TgWsProxyReleaseInfo BuildReleaseInfo(
        string? currentVersion,
        string latestVersion,
        string releasePageUrl,
        DateTimeOffset? publishedAt,
        string? downloadUrl,
        string? assetFileName)
    {
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var isInstalled = !string.IsNullOrWhiteSpace(normalizedCurrentVersion);
        var isUpdateAvailable = !isInstalled || CompareVersions(normalizedCurrentVersion, latestVersion) < 0;

        if (isUpdateAvailable && string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("В релизе TG WS Proxy не найден Windows exe-файл.");
        }

        return new TgWsProxyReleaseInfo
        {
            CurrentVersion = normalizedCurrentVersion,
            LatestVersion = latestVersion,
            ReleasePageUrl = releasePageUrl,
            DownloadUrl = downloadUrl,
            AssetFileName = assetFileName,
            PublishedAt = publishedAt,
            IsUpdateAvailable = isUpdateAvailable
        };
    }

    public async Task<string> DownloadReleaseAsync(
        string downloadUrl,
        string latestVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("Не указана ссылка для скачивания TG WS Proxy.");
        }

        try
        {
            Directory.CreateDirectory(ManagedComponentDirectoryPath);
            var tempPath = Path.Combine(
                ManagedComponentDirectoryPath,
                $"tg-ws-proxy-{NormalizeVersion(latestVersion)}-{Guid.NewGuid():N}.exe");

            await using var sourceStream = await HttpClient.GetStreamAsync(downloadUrl, cancellationToken);
            await using var targetStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
            await targetStream.FlushAsync(cancellationToken);
            return tempPath;
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, "Скачивание TG WS Proxy");
        }
    }

    public async Task InstallDownloadedReleaseAsync(
        string downloadedExecutablePath,
        string targetExecutablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadedExecutablePath) || !File.Exists(downloadedExecutablePath))
        {
            throw new InvalidOperationException("Не найден скачанный файл TG WS Proxy.");
        }

        if (string.IsNullOrWhiteSpace(targetExecutablePath))
        {
            throw new InvalidOperationException("Не удалось определить путь установки TG WS Proxy.");
        }

        var normalizedTargetPath = Path.GetFullPath(targetExecutablePath);
        var targetDirectory = Path.GetDirectoryName(normalizedTargetPath)
            ?? throw new InvalidOperationException("Не удалось определить папку установки TG WS Proxy.");
        Directory.CreateDirectory(targetDirectory);

        var backupPath = normalizedTargetPath + ".old";
        if (File.Exists(normalizedTargetPath))
        {
            TryDeleteFile(backupPath);
            File.Move(normalizedTargetPath, backupPath, overwrite: true);
        }

        await using var sourceStream = new FileStream(downloadedExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var targetStream = new FileStream(normalizedTargetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
        await targetStream.FlushAsync(cancellationToken);
        TryDeleteFile(downloadedExecutablePath);
    }

    public void Start(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("TG WS Proxy не найден.");
        }

        EnsureConfigExists();

        var normalizedPath = Path.GetFullPath(executablePath);
        var workingDirectory = Path.GetDirectoryName(normalizedPath) ?? ManagedComponentDirectoryPath;
        var startInfo = new ProcessStartInfo
        {
            FileName = normalizedPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить TG WS Proxy.");
    }

    public bool Stop(string? executablePath, TimeSpan? timeout = null)
    {
        var processes = FindProcessesForExecutable(executablePath);
        if (processes.Count == 0)
        {
            return false;
        }

        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        foreach (var process in processes)
        {
            try
            {
                process.WaitForExit((int)waitTimeout.TotalMilliseconds);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return true;
    }

    public string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string BuildTelegramProxyUrl(TgWsProxyConfig config)
    {
        var host = string.IsNullOrWhiteSpace(config.Host)
            ? "127.0.0.1"
            : config.Host.Trim();

        var linkHost = string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;

        return $"tg://proxy?server={linkHost}&port={config.Port}&secret=dd{NormalizeSecret(config.Secret)}";
    }

    public void EnsureConfigExists()
    {
        SaveConfig(LoadConfig());
    }

    public void EnsureFirstRunMarkerExists()
    {
        Directory.CreateDirectory(ConfigDirectoryPath);
        if (!File.Exists(FirstRunMarkerPath))
        {
            using var _ = File.Create(FirstRunMarkerPath);
        }
    }

    public async Task<TgWsProxyCfProxyTestResult> TestCfProxyAsync(string? userDomain, CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeCfProxyDomain(userDomain);
        if (!string.IsNullOrWhiteSpace(normalizedDomain))
        {
            return await TestCfProxyDomainAsync(normalizedDomain, usedCustomDomain: true, cancellationToken);
        }

        var domains = (await GetCfProxyDomainsAsync(cancellationToken)).Reverse().ToArray();
        var mergedSuccessResults = new Dictionary<int, TgWsProxyCfProxyDcResult>();

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await TestCfProxyDomainAsync(domain, usedCustomDomain: false, cancellationToken);
            if (result.Results.All(static item => item.IsSuccess))
            {
                return result;
            }

            foreach (var dcResult in result.Results.Where(static item => item.IsSuccess))
            {
                mergedSuccessResults.TryAdd(dcResult.DcId, dcResult);
            }
        }

        var mergedResults = CfProxyTestDcIds
            .Select(dcId => mergedSuccessResults.TryGetValue(dcId, out var successResult)
                ? successResult
                : new TgWsProxyCfProxyDcResult(dcId, false, "нет ответа"))
            .ToArray();

        return new TgWsProxyCfProxyTestResult(null, mergedResults, UsedCustomDomain: false);
    }

    private IEnumerable<string> EnumerateCandidateExecutablePaths(string? preferredPath)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var runningPath = GetRunningExecutablePath();
        if (!string.IsNullOrWhiteSpace(runningPath))
        {
            yielded.Add(Path.GetFullPath(runningPath));
            yield return Path.GetFullPath(runningPath);
        }

        foreach (var candidate in EnumeratePreferredCandidates(preferredPath))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (yielded.Add(normalized))
            {
                yield return normalized;
            }
        }

        foreach (var searchRoot in EnumerateSearchRoots())
        {
            foreach (var candidate in SearchForExecutable(searchRoot.Path, searchRoot.MaxDepth))
            {
                string normalized;
                try
                {
                    normalized = Path.GetFullPath(candidate);
                }
                catch
                {
                    continue;
                }

                if (yielded.Add(normalized))
                {
                    yield return normalized;
                }
            }
        }
    }

    private IEnumerable<string> EnumeratePreferredCandidates(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            yield return preferredPath;
        }

        yield return ManagedExecutablePath;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            foreach (var executableName in ExecutableFileNames)
            {
                yield return Path.Combine(localAppData, "Programs", "TgWsProxy", executableName);
                yield return Path.Combine(localAppData, "Programs", "TG WS Proxy", executableName);
                yield return Path.Combine(localAppData, "TgWsProxy", executableName);
            }
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (var executableName in ExecutableFileNames)
            {
                yield return Path.Combine(userProfile, "Desktop", executableName);
                yield return Path.Combine(userProfile, "Downloads", executableName);
            }
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            foreach (var executableName in ExecutableFileNames)
            {
                yield return Path.Combine(programFiles, "TgWsProxy", executableName);
                yield return Path.Combine(programFiles, "TG WS Proxy", executableName);
            }
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            foreach (var executableName in ExecutableFileNames)
            {
                yield return Path.Combine(programFilesX86, "TgWsProxy", executableName);
                yield return Path.Combine(programFilesX86, "TG WS Proxy", executableName);
            }
        }
    }

    private IEnumerable<(string Path, int MaxDepth)> EnumerateSearchRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return (Path.Combine(localAppData, "Programs"), 3);
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return (Path.Combine(userProfile, "Desktop"), 2);
            yield return (Path.Combine(userProfile, "Downloads"), 3);
        }
    }

    private IEnumerable<string> SearchForExecutable(string rootPath, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((rootPath, 0));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var executableName in ExecutableFileNames)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current.Path, executableName, SearchOption.TopDirectoryOnly).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }

            if (current.Depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current.Path, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push((directory, current.Depth + 1));
            }
        }
    }

    private List<Process> FindProcessesForExecutable(string? executablePath)
    {
        var normalizedTargetPath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Path.GetFullPath(executablePath);

        var result = new List<Process>();
        foreach (var process in EnumerateTgWsProxyProcesses())
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                {
                    process.Dispose();
                    continue;
                }

                var normalizedProcessPath = Path.GetFullPath(processPath);
                if (normalizedTargetPath is null ||
                    string.Equals(normalizedProcessPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process);
                    continue;
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        return result;
    }

    private async Task<TgWsProxyCfProxyTestResult> TestCfProxyDomainAsync(string domain, bool usedCustomDomain, CancellationToken cancellationToken)
    {
        var results = new List<TgWsProxyCfProxyDcResult>(CfProxyTestDcIds.Length);
        foreach (var dcId in CfProxyTestDcIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TestCfProxyDcAsync(domain, dcId, cancellationToken));
        }

        return new TgWsProxyCfProxyTestResult(domain, results, usedCustomDomain);
    }

    private async Task<TgWsProxyCfProxyDcResult> TestCfProxyDcAsync(string domain, int dcId, CancellationToken cancellationToken)
    {
        var host = $"kws{dcId}.{domain}";

        try
        {
            using var client = new TcpClient();
            using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCancellation.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(host, 443, connectCancellation.Token);

            using var ssl = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: static (_, _, _, _) => true);

            using var authCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            authCancellation.CancelAfter(TimeSpan.FromSeconds(5));
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                authCancellation.Token);

            var websocketKey = GenerateSecWebSocketKey();
            var request = string.Join(
                "\r\n",
                $"GET /apiws HTTP/1.1",
                $"Host: {host}",
                "Upgrade: websocket",
                "Connection: Upgrade",
                $"Sec-WebSocket-Key: {websocketKey}",
                "Sec-WebSocket-Version: 13",
                "Sec-WebSocket-Protocol: binary",
                string.Empty,
                string.Empty);

            var requestBytes = Encoding.ASCII.GetBytes(request);
            await ssl.WriteAsync(requestBytes, cancellationToken);
            await ssl.FlushAsync(cancellationToken);

            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCancellation.CancelAfter(TimeSpan.FromSeconds(5));
            var responseHeaders = await ReadHeadersAsync(ssl, readCancellation.Token);
            var firstLine = responseHeaders
                .Split(["\r\n"], StringSplitOptions.None)
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))
                ?.Trim()
                ?? "нет ответа";

            return new TgWsProxyCfProxyDcResult(
                dcId,
                firstLine.Contains("101", StringComparison.Ordinal),
                firstLine);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TgWsProxyCfProxyDcResult(dcId, false, "таймаут");
        }
        catch (SocketException ex)
        {
            return new TgWsProxyCfProxyDcResult(dcId, false, BuildSocketErrorMessage(ex));
        }
        catch (IOException ex) when (ex.InnerException is SocketException socketException)
        {
            return new TgWsProxyCfProxyDcResult(dcId, false, BuildSocketErrorMessage(socketException));
        }
        catch (Exception ex)
        {
            return new TgWsProxyCfProxyDcResult(dcId, false, DialogService.GetShortDisplayMessage(ex, "нет ответа"));
        }
    }

    private async Task<IReadOnlyList<string>> GetCfProxyDomainsAsync(CancellationToken cancellationToken)
    {
        var defaults = DecodeDefaultCfProxyDomains().ToArray();

        try
        {
            var content = await HttpClient.GetStringAsync(CfProxyDomainsUrl, cancellationToken);
            var domains = content
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static item => item.StartsWith('#') ? string.Empty : item)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(DecodeFetchedCfProxyDomain)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(NormalizeCfProxyDomain)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();

            if (domains.Length >= 3)
            {
                return domains;
            }
        }
        catch
        {
        }

        return defaults;
    }

    private static IEnumerable<string> DecodeDefaultCfProxyDomains()
    {
        foreach (var encodedDomain in DefaultEncodedCfProxyDomains)
        {
            var decoded = DecodeCfProxyDomain(encodedDomain);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                yield return decoded;
            }
        }
    }

    private static string DecodeFetchedCfProxyDomain(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".com", StringComparison.OrdinalIgnoreCase)
            ? DecodeCfProxyDomain(trimmed)
            : trimmed;
    }

    private static string NormalizeCfProxyDomain(string? value)
    {
        var domain = (value ?? string.Empty).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        return Uri.CheckHostName(domain) == UriHostNameType.Dns
            ? domain.ToLowerInvariant()
            : throw new InvalidOperationException($"Некорректный CF-домен: {domain}");
    }

    private static string DecodeCfProxyDomain(string encodedValue)
    {
        if (string.IsNullOrWhiteSpace(encodedValue) ||
            !encodedValue.EndsWith(".com", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var encodedCore = encodedValue[..^4];
        var shift = encodedCore.Count(char.IsLetter);
        var decodedChars = encodedCore
            .Select(character => DecodeShiftedCharacter(character, shift))
            .ToArray();

        return new string(decodedChars) + ".co.uk";
    }

    private static char DecodeShiftedCharacter(char character, int shift)
    {
        if (!char.IsLetter(character))
        {
            return character;
        }

        var baseCode = char.IsUpper(character) ? 'A' : 'a';
        var normalized = character - baseCode;
        var shifted = (normalized - shift) % 26;
        if (shifted < 0)
        {
            shifted += 26;
        }

        return (char)(baseCode + shifted);
    }

    private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, Math.Min(256, buffer.Length - total)), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (ContainsHeaderTerminator(buffer, total))
            {
                break;
            }
        }

        return total == 0
            ? string.Empty
            : Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static bool ContainsHeaderTerminator(byte[] buffer, int total)
    {
        for (var index = 3; index < total; index++)
        {
            if (buffer[index - 3] == '\r' &&
                buffer[index - 2] == '\n' &&
                buffer[index - 1] == '\r' &&
                buffer[index] == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateSecWebSocketKey()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string BuildSocketErrorMessage(SocketException exception)
    {
        var message = exception.SocketErrorCode switch
        {
            SocketError.HostNotFound => "хост не найден",
            SocketError.TimedOut => "таймаут",
            SocketError.ConnectionRefused => "соединение отклонено",
            SocketError.NetworkUnreachable => "сеть недоступна",
            SocketError.HostUnreachable => "узел недоступен",
            _ => exception.Message
        };

        return DialogService.GetShortDisplayMessage(message, "нет ответа");
    }

    private static TgWsProxyConfig CreateDefaultConfig()
    {
        return new TgWsProxyConfig
        {
            AutoStart = true,
            Secret = GenerateSecretStatic()
        };
    }

    private static string GenerateSecretStatic()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretManager");
        return client;
    }

    private static string NormalizeVersion(string? version)
    {
        var value = version?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.StartsWith('v') || value.StartsWith('V')
            ? value[1..]
            : value;
    }

    private static int CompareVersions(string currentVersion, string latestVersion)
    {
        var current = ParseComparableVersion(currentVersion);
        var latest = ParseComparableVersion(latestVersion);
        return current.CompareTo(latest);
    }

    private static Version ParseComparableVersion(string? version)
    {
        var match = Regex.Match(NormalizeVersion(version), @"\d+(?:\.\d+){0,3}");
        var parts = (match.Success ? match.Value : "0")
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => int.TryParse(part, out var value) ? value : 0)
            .ToList();

        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    private static DateTimeOffset? TryReadDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string NormalizeSecret(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string BuildAutoStartCommand(string executablePath)
    {
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }

    private static void RemoveAutoStartValues(RegistryKey key)
    {
        foreach (var valueName in AutoStartValueNames)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    private static string ReadString(JsonObject root, string key, string fallback)
    {
        return root[key]?.GetValue<string>()?.Trim() ?? fallback;
    }

    private static int ReadInt(JsonObject root, string key, int fallback)
    {
        if (root[key] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var integerValue))
        {
            return integerValue;
        }

        if (value.TryGetValue<double>(out var floatingValue))
        {
            return (int)Math.Round(floatingValue);
        }

        if (value.TryGetValue<string>(out var textValue) &&
            double.TryParse(textValue, out var parsedValue))
        {
            return (int)Math.Round(parsedValue);
        }

        return fallback;
    }

    private static bool ReadBool(JsonObject root, string key, bool fallback)
    {
        return root[key]?.GetValue<bool?>() ?? fallback;
    }

    private static List<string> ReadStringList(JsonObject root, string key, List<string> fallback)
    {
        if (root[key] is not JsonArray array)
        {
            return [.. fallback];
        }

        var items = array
            .Select(static item => item?.GetValue<string>()?.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();

        return items.Count > 0 ? items : [.. fallback];
    }

    private static ReleaseAsset? SelectWindowsAsset(JsonArray? assets)
    {
        if (assets is null)
        {
            return null;
        }

        var releaseAssets = assets
            .Select(static asset => asset as JsonObject)
            .Where(static asset => asset is not null)
            .Select(static asset => new ReleaseAsset(
                asset!["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                asset["browser_download_url"]?.GetValue<string>()?.Trim() ?? string.Empty))
            .Where(static asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .ToList();

        return releaseAssets.FirstOrDefault(static asset => string.Equals(asset.Name, "TgWsProxy_windows.exe", StringComparison.OrdinalIgnoreCase))
               ?? releaseAssets.FirstOrDefault(static asset =>
                   asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   asset.Name.Contains("windows", StringComparison.OrdinalIgnoreCase));
    }

    private static ReleaseAsset? SelectWindowsAssetFromHtml(string html)
    {
        var matches = Regex.Matches(
            html,
            "href=\"(?<href>/Flowseal/tg-ws-proxy/releases/download/[^\"]+\\.exe)\"",
            RegexOptions.IgnoreCase);

        var assets = matches
            .Select(match => match.Groups["href"].Value)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => new ReleaseAsset(
                Path.GetFileName(Uri.UnescapeDataString(href)),
                "https://github.com" + href))
            .ToList();

        return assets.FirstOrDefault(static asset => string.Equals(asset.Name, "TgWsProxy_windows.exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(static asset =>
                   asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   asset.Name.Contains("windows", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractTagName(HttpResponseMessage response)
    {
        var requestUri = response.RequestMessage?.RequestUri?.AbsoluteUri;
        if (!string.IsNullOrWhiteSpace(requestUri))
        {
            var match = Regex.Match(requestUri, "/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups["tag"].Value;
            }
        }

        if (response.Headers.Location is not null)
        {
            var location = response.Headers.Location.ToString();
            var match = Regex.Match(location, "/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups["tag"].Value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
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

    private static IEnumerable<Process> EnumerateTgWsProxyProcesses()
    {
        var yielded = new HashSet<int>();
        foreach (var processName in ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                if (yielded.Add(process.Id))
                {
                    yield return process;
                    continue;
                }

                process.Dispose();
            }
        }
    }

    private sealed record ReleaseAsset(string Name, string BrowserDownloadUrl);
}
