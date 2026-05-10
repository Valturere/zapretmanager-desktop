using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class UpdateService
{
    private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    private const string ReleaseBaseUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/tag/";
    private const string ExpandedAssetsUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/expanded_assets/";
    private const string ManagerAppDataFolderName = "ZapretManager";
    private const string ManagerStorageContainerFolderName = "PreviousVersions";
    private const string ManagerStorageFolderName = ".zapret-manager";
    private const string PreviousVersionFolderName = "previous";
    private const string SwapVersionFolderName = "swap-current";

    private static readonly string[] PreservedRelativePaths =
    [
        @"lists\list-general-user.txt",
        @"lists\list-exclude-user.txt",
        @"lists\ipset-exclude-user.txt",
        @"lists\ipset-all.txt",
        @"lists\ipset-all.txt.backup",
        @"utils\targets.txt",
        @"utils\game_filter.enabled"
    ];

    private readonly HttpClient _httpClient = new();

    public UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretManager");
    }

    public bool HasStoredPreviousVersion(string currentRootPath)
    {
        return TryGetStoredPreviousVersionPath(currentRootPath) is not null;
    }

    public string? TryGetStoredPreviousVersionPath(string currentRootPath)
    {
        if (string.IsNullOrWhiteSpace(currentRootPath))
        {
            return null;
        }

        return ResolveStoredPreviousVersionPath(currentRootPath);
    }

    public async Task<string?> DeleteStoredPreviousVersionAsync(string currentRootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentRootPath))
        {
            return null;
        }

        string? pendingDeletePath = null;
        foreach (var path in EnumerateStoredPreviousVersionPaths(currentRootPath))
        {
            var deletedPath = await DeleteDirectoryAsync(path, cancellationToken);
            if (pendingDeletePath is null && !string.IsNullOrWhiteSpace(deletedPath))
            {
                pendingDeletePath = deletedPath;
            }
        }

        TryDeleteLegacyManagerStorageIfEmpty(currentRootPath);
        return pendingDeletePath;
    }

    public async Task<UpdateOperationResult> RestorePreviousVersionAsync(
        string currentRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(currentRootPath))
        {
            throw new DirectoryNotFoundException("Текущая папка zapret не найдена.");
        }

        EnsureManagerIsNotRunningInside(currentRootPath);

        var previousPath = ResolveStoredPreviousVersionPath(currentRootPath);
        if (string.IsNullOrWhiteSpace(previousPath))
        {
            throw new InvalidOperationException("Сохранённая предыдущая версия zapret не найдена.");
        }

        var discoveryService = new ZapretDiscoveryService();
        var previousInstallation = discoveryService.TryLoad(previousPath)
            ?? throw new InvalidOperationException("Сохранённая предыдущая версия zapret повреждена и не может быть восстановлена.");

        var rootDirectory = Directory.GetParent(currentRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Не удалось определить родительскую папку для отката zapret.");
        }

        EnsureManagerStorageExists(currentRootPath);
        var swapPath = GetSwapStoragePath(currentRootPath);
        await DeleteDirectoryAsync(swapPath, cancellationToken);

        var currentMoved = false;
        var previousMoved = false;
        string? restoredRootPath = null;

        try
        {
            if (ArePathsOnSameVolume(currentRootPath, swapPath))
            {
                Directory.Move(currentRootPath, swapPath);
            }
            else
            {
                CopyDirectory(currentRootPath, swapPath);
                await DeleteDirectoryAsync(currentRootPath, cancellationToken);
            }
            currentMoved = true;

            restoredRootPath = GetUniquePath(Path.Combine(rootDirectory, BuildReleaseFolderName(previousInstallation.Version)));
            if (ArePathsOnSameVolume(previousPath, restoredRootPath))
            {
                Directory.Move(previousPath, restoredRootPath);
            }
            else
            {
                CopyDirectory(previousPath, restoredRootPath);
                await DeleteDirectoryAsync(previousPath, cancellationToken);
            }
            previousMoved = true;

            Directory.Move(swapPath, previousPath);
            DisableInternalCheckUpdates(previousPath);
            DisableInternalCheckUpdatesForSiblingInstallations(restoredRootPath);

            return new UpdateOperationResult
            {
                ActiveRootPath = restoredRootPath,
                InstalledVersion = previousInstallation.Version,
                BackupRootPath = previousPath,
                ServiceWasInstalled = false,
                ServiceWasRunning = false
            };
        }
        catch
        {
            if (previousMoved &&
                !string.IsNullOrWhiteSpace(restoredRootPath) &&
                Directory.Exists(restoredRootPath) &&
                !Directory.Exists(previousPath))
            {
                try
                {
                    if (ArePathsOnSameVolume(restoredRootPath, previousPath))
                    {
                        Directory.Move(restoredRootPath, previousPath);
                    }
                    else
                    {
                        CopyDirectory(restoredRootPath, previousPath);
                        await DeleteDirectoryAsync(restoredRootPath, cancellationToken);
                    }
                }
                catch
                {
                }
            }

            if (currentMoved &&
                Directory.Exists(swapPath) &&
                !Directory.Exists(currentRootPath))
            {
                try
                {
                    if (ArePathsOnSameVolume(swapPath, currentRootPath))
                    {
                        Directory.Move(swapPath, currentRootPath);
                    }
                    else
                    {
                        CopyDirectory(swapPath, currentRootPath);
                        await DeleteDirectoryAsync(swapPath, cancellationToken);
                    }
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            SafeDeleteDirectory(swapPath);
        }
    }

    public async Task<UpdateInfo> GetUpdateInfoAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetUpdateInfoFromApiAsync(currentVersion, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            try
            {
                return await GetUpdateInfoFromLegacySourcesAsync(currentVersion, cancellationToken);
            }
            catch (Exception fallbackEx) when (NetworkErrorTranslator.IsNetworkException(fallbackEx))
            {
                throw NetworkErrorTranslator.CreateGitHubException(fallbackEx, "Не удалось проверить обновления");
            }
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            try
            {
                return await GetUpdateInfoFromLegacySourcesAsync(currentVersion, cancellationToken);
            }
            catch (Exception fallbackEx) when (NetworkErrorTranslator.IsNetworkException(fallbackEx))
            {
                throw NetworkErrorTranslator.CreateGitHubException(fallbackEx, "Не удалось проверить обновления");
            }
        }
    }

    private async Task<UpdateInfo> GetUpdateInfoFromApiAsync(string currentVersion, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidOperationException("GitHub вернул пустые данные по релизу zapret.");

        var tagName = root["tag_name"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("GitHub вернул релиз zapret без версии.");
        }

        var latestVersion = NormalizeReleaseTag(tagName);
        var asset = SelectZipAsset(root["assets"] as JsonArray);
        return new UpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            DownloadUrl = asset?.DownloadUrl,
            ReleasePageUrl = root["html_url"]?.GetValue<string>()?.Trim() ?? (ReleaseBaseUrl + latestVersion)
        };
    }

    private async Task<UpdateInfo> GetUpdateInfoFromLegacySourcesAsync(string currentVersion, CancellationToken cancellationToken)
    {
        var latestVersion = (await _httpClient.GetStringAsync(VersionUrl, cancellationToken)).Trim();
        var html = await _httpClient.GetStringAsync(ExpandedAssetsUrl + latestVersion, cancellationToken);
        var match = Regex.Match(
            html,
            "href=\"(?<href>/Flowseal/zapret-discord-youtube/releases/download/[^\"]+\\.zip)\"",
            RegexOptions.IgnoreCase);

        return new UpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = NormalizeReleaseTag(latestVersion),
            DownloadUrl = match.Success ? "https://github.com" + match.Groups["href"].Value : null,
            ReleasePageUrl = ReleaseBaseUrl + NormalizeReleaseTag(latestVersion)
        };
    }

    private static string NormalizeReleaseTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim();
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized[1..]
            : normalized;
    }

    private static ReleaseAsset? SelectZipAsset(JsonArray? assets)
    {
        return assets?
            .Select(static asset => asset as JsonObject)
            .Where(static asset => asset is not null)
            .Select(static asset => new ReleaseAsset(
                asset!["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                asset["browser_download_url"]?.GetValue<string>()?.Trim() ?? string.Empty))
            .FirstOrDefault(static asset =>
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                asset.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ReleaseAsset(string Name, string DownloadUrl);

    public async Task<UpdateOperationResult> InstallFreshAsync(string selectedPath, CancellationToken cancellationToken = default)
    {
        var update = await GetUpdateInfoAsync("unknown", cancellationToken);
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("Не удалось найти ссылку на скачивание актуальной сборки zapret.");
        }

        var package = await DownloadAndExtractAsync(update.DownloadUrl, update.LatestVersion, cancellationToken);
        try
        {
            var targetRoot = ResolveFreshInstallTarget(selectedPath, package.Version);
            Directory.CreateDirectory(targetRoot);
            CopyDirectory(package.SourceRoot, targetRoot);
            DisableInternalCheckUpdatesForSiblingInstallations(targetRoot);

            return new UpdateOperationResult
            {
                ActiveRootPath = targetRoot,
                InstalledVersion = package.Version,
                BackupRootPath = null,
                ServiceWasInstalled = false,
                ServiceWasRunning = false
            };
        }
        finally
        {
            SafeDeleteDirectory(package.TempRoot);
        }
    }

    public async Task<UpdateOperationResult> ApplyUpdateAsync(
        string rootPath,
        string downloadUrl,
        string latestVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("Текущая папка zapret не найдена.");
        }

        var rootDirectory = Directory.GetParent(rootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Не удалось определить родительскую папку для обновления zapret.");
        }

        var package = await DownloadAndExtractAsync(downloadUrl, latestVersion, cancellationToken);
        string? newRootPath = null;
        string? previousVersionPath = null;
        var previousVersionWasBusy = false;
        var previousVersionMoved = false;

        var serviceManager = new WindowsServiceManager();
        var serviceStatus = serviceManager.GetStatus();
        var processService = new ZapretProcessService();
        var fileLockInspector = new FileLockInspectorService();
        var currentInstallation = new ZapretDiscoveryService().TryLoad(rootPath);
        string? busyProcessSummary = null;

        try
        {
            EnsureManagerIsNotRunningInside(rootPath);
            DisableInternalCheckUpdatesForSiblingInstallations(rootPath);

            var preservedFiles = BackupPreservedFiles(rootPath);
            if (serviceStatus.IsInstalled)
            {
                await serviceManager.RemoveAsync();
            }

            await processService.StopCheckUpdatesShellsAsync();
            if (currentInstallation is not null)
            {
                await processService.StopAsync(currentInstallation);
                await processService.StopProcessesUsingInstallationAsync(currentInstallation);
                await TryReleaseSafeLockingProcessesAsync(fileLockInspector, rootPath);
                await WaitForInstallationReleaseAsync(processService, currentInstallation, TimeSpan.FromSeconds(20));
            }

            await WaitForDriverReleaseAsync(rootPath, TimeSpan.FromSeconds(60));
            previousVersionPath = GetPreviousVersionStoragePath(rootPath);
            await DeleteDirectoryAsync(previousVersionPath, cancellationToken);
            EnsureManagerStorageExists(rootPath);
            if (ArePathsOnSameVolume(rootPath, previousVersionPath))
            {
                try
                {
                    Directory.Move(rootPath, previousVersionPath);
                    previousVersionMoved = true;
                    DisableInternalCheckUpdates(previousVersionPath);
                }
                catch (Exception ex) when (IsFolderBusyException(ex))
                {
                    await TryReleaseBlockingProcessesAsync(fileLockInspector, rootPath, cancellationToken);
                    if (TryMoveDirectory(rootPath, previousVersionPath))
                    {
                        previousVersionMoved = true;
                        DisableInternalCheckUpdates(previousVersionPath);
                    }
                    else
                    {
                        busyProcessSummary = BuildBusyProcessSummary(fileLockInspector, rootPath);
                        throw CreateBusyPreviousVersionException(busyProcessSummary, ex);
                    }
                }
            }
            else
            {
                CopyDirectory(rootPath, previousVersionPath);
                previousVersionMoved = true;
                DisableInternalCheckUpdates(previousVersionPath);
                var pendingDeletePath = await DeleteDirectoryAsync(rootPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(pendingDeletePath))
                {
                    previousVersionWasBusy = true;
                    busyProcessSummary = BuildBusyProcessSummary(fileLockInspector, rootPath);
                }
            }

            newRootPath = GetUniquePath(Path.Combine(rootDirectory, BuildReleaseFolderName(package.Version)));
            Directory.CreateDirectory(newRootPath);
            CopyDirectory(package.SourceRoot, newRootPath);
            RestorePreservedFiles(newRootPath, preservedFiles);
            DisableInternalCheckUpdatesForSiblingInstallations(newRootPath);

            return new UpdateOperationResult
            {
                ActiveRootPath = newRootPath,
                InstalledVersion = package.Version,
                BackupRootPath = previousVersionPath,
                PreviousVersionWasBusy = previousVersionWasBusy,
                PreviousVersionBusyProcessSummary = busyProcessSummary,
                ServiceWasInstalled = serviceStatus.IsInstalled,
                ServiceWasRunning = serviceStatus.IsRunning
            };
        }
        catch (Exception ex)
        {
            SafeDeleteDirectory(newRootPath);
            if (previousVersionMoved &&
                !string.IsNullOrWhiteSpace(previousVersionPath) &&
                Directory.Exists(previousVersionPath) &&
                !Directory.Exists(rootPath))
            {
                try
                {
                    Directory.Move(previousVersionPath, rootPath);
                }
                catch
                {
                }
            }

            throw CreateFriendlyUpdateException(ex);
        }
        finally
        {
            SafeDeleteDirectory(package.TempRoot);
        }
    }

    private async Task<DownloadedPackage> DownloadAndExtractAsync(string downloadUrl, string latestVersion, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ZapretManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var zipPath = Path.Combine(tempRoot, "zapret-update.zip");
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            await using (var stream = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken))
            await using (var output = File.Create(zipPath))
            {
                await stream.CopyToAsync(output, cancellationToken);
            }
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, "Не удалось скачать актуальную сборку zapret");
        }

        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
        var sourceRoot = FindExtractedRoot(extractPath);

        return new DownloadedPackage(tempRoot, sourceRoot, latestVersion);
    }

    private static string FindExtractedRoot(string extractPath)
    {
        if (File.Exists(Path.Combine(extractPath, "service.bat")) &&
            File.Exists(Path.Combine(extractPath, "bin", "winws.exe")))
        {
            return extractPath;
        }

        var candidate = Directory
            .EnumerateDirectories(extractPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                File.Exists(Path.Combine(path, "service.bat")) &&
                File.Exists(Path.Combine(path, "bin", "winws.exe")));

        if (candidate is null)
        {
            throw new InvalidOperationException("Не удалось найти распакованную сборку zapret.");
        }

        return candidate;
    }

    private static Dictionary<string, byte[]> BackupPreservedFiles(string rootPath)
    {
        var backup = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in PreservedRelativePaths)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            if (File.Exists(fullPath))
            {
                backup[relativePath] = ReadAllBytesWithRetries(fullPath, TimeSpan.FromSeconds(10));
            }
        }

        return backup;
    }

    public void DisableInternalCheckUpdatesForSiblingInstallations(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        DisableInternalCheckUpdates(rootPath);

        var parentPath = Directory.GetParent(rootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(parentPath, "zapret*"))
        {
            if (!File.Exists(Path.Combine(directory, "service.bat")) ||
                !File.Exists(Path.Combine(directory, "bin", "winws.exe")))
            {
                continue;
            }

            DisableInternalCheckUpdates(directory);
        }
    }

    private static void RestorePreservedFiles(string rootPath, IReadOnlyDictionary<string, byte[]> backup)
    {
        foreach (var (relativePath, bytes) in backup)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var destination = Path.Combine(destinationPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string ResolveFreshInstallTarget(string selectedPath, string version)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new InvalidOperationException("Не выбрана папка для установки zapret.");
        }

        var fullSelectedPath = Path.GetFullPath(selectedPath);
        if (!Directory.Exists(fullSelectedPath))
        {
            throw new DirectoryNotFoundException("Выбранная папка для установки не найдена.");
        }

        if (!Directory.EnumerateFileSystemEntries(fullSelectedPath).Any())
        {
            return fullSelectedPath;
        }

        return GetUniquePath(Path.Combine(fullSelectedPath, BuildReleaseFolderName(version)));
    }

    private static string BuildReleaseFolderName(string version)
    {
        var cleanedVersion = string.Concat(version.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        if (string.IsNullOrWhiteSpace(cleanedVersion))
        {
            cleanedVersion = "latest";
        }

        return $"zapret-discord-youtube-{cleanedVersion}";
    }

    private static string GetManagerStoragePath(string rootPath)
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            throw new InvalidOperationException("Не удалось определить папку хранения служебных данных zapret.");
        }

        var storageKey = BuildManagerStorageKey(rootPath);
        return Path.Combine(appDataRoot, ManagerAppDataFolderName, ManagerStorageContainerFolderName, storageKey);
    }

    private static string GetPreviousVersionStoragePath(string rootPath)
    {
        return Path.Combine(GetManagerStoragePath(rootPath), PreviousVersionFolderName);
    }

    private static string GetSwapStoragePath(string rootPath)
    {
        return Path.Combine(GetManagerStoragePath(rootPath), SwapVersionFolderName);
    }

    private static void EnsureManagerStorageExists(string rootPath)
    {
        var storagePath = GetManagerStoragePath(rootPath);
        Directory.CreateDirectory(storagePath);
    }

    private static string GetLegacyManagerStoragePath(string rootPath)
    {
        var rootDirectory = Directory.GetParent(Path.GetFullPath(rootPath))?.FullName;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Не удалось определить папку хранения служебных данных zapret.");
        }

        return Path.Combine(rootDirectory, ManagerStorageFolderName);
    }

    private static string GetLegacyPreviousVersionStoragePath(string rootPath)
    {
        return Path.Combine(GetLegacyManagerStoragePath(rootPath), PreviousVersionFolderName);
    }

    private static bool IsValidStoredPreviousVersion(string path)
    {
        return Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "service.bat")) &&
               File.Exists(Path.Combine(path, "bin", "winws.exe"));
    }

    private static IEnumerable<string> EnumerateStoredPreviousVersionPaths(string rootPath)
    {
        var currentPath = GetPreviousVersionStoragePath(rootPath);
        var legacyPath = GetLegacyPreviousVersionStoragePath(rootPath);

        if (Directory.Exists(currentPath))
        {
            yield return currentPath;
        }

        if (!string.Equals(currentPath, legacyPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(legacyPath))
        {
            yield return legacyPath;
        }
    }

    private static string? ResolveStoredPreviousVersionPath(string rootPath)
    {
        var currentPath = GetPreviousVersionStoragePath(rootPath);
        if (IsValidStoredPreviousVersion(currentPath))
        {
            return currentPath;
        }

        var legacyPath = GetLegacyPreviousVersionStoragePath(rootPath);
        if (!IsValidStoredPreviousVersion(legacyPath))
        {
            TryDeleteLegacyManagerStorageIfEmpty(rootPath);
            return null;
        }

        try
        {
            EnsureManagerStorageExists(rootPath);
            if (!Directory.Exists(currentPath))
            {
                Directory.Move(legacyPath, currentPath);
                TryDeleteLegacyManagerStorageIfEmpty(rootPath);
                return currentPath;
            }
        }
        catch
        {
        }

        return legacyPath;
    }

    private static void TryDeleteLegacyManagerStorageIfEmpty(string rootPath)
    {
        try
        {
            var legacyRoot = GetLegacyManagerStoragePath(rootPath);
            if (!Directory.Exists(legacyRoot))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(legacyRoot).Any())
            {
                return;
            }

            Directory.Delete(legacyRoot, recursive: false);
        }
        catch
        {
        }
    }

    private static string BuildManagerStorageKey(string rootPath)
    {
        var parentDirectory = Directory.GetParent(Path.GetFullPath(rootPath))?.FullName ?? Path.GetFullPath(rootPath);
        var normalized = parentDirectory.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private static string GetUniquePath(string preferredPath)
    {
        if (!Directory.Exists(preferredPath) && !File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var parent = Path.GetDirectoryName(preferredPath) ?? Directory.GetCurrentDirectory();
        var baseName = Path.GetFileName(preferredPath);
        var counter = 2;

        while (true)
        {
            var candidate = Path.Combine(parent, $"{baseName} ({counter})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private static void SafeDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static void DisableInternalCheckUpdates(string rootPath)
    {
        var flagPath = Path.Combine(rootPath, "utils", "check_updates.enabled");
        if (!File.Exists(flagPath))
        {
            return;
        }

        var started = DateTime.UtcNow;
        while (true)
        {
            try
            {
                File.Delete(flagPath);
                return;
            }
            catch (IOException) when (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
            {
                Thread.Sleep(250);
            }
            catch
            {
                return;
            }
        }
    }

    private static void EnsureManagerIsNotRunningInside(string rootPath)
    {
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath))
        {
            return;
        }

        var fullRootPath = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullExePath = Path.GetFullPath(currentExePath);

        if (fullExePath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Менеджер запущен из папки текущей сборки zapret. Переместите Zapret Manager в другую папку и повторите обновление.");
        }
    }

    private static byte[] ReadAllBytesWithRetries(string path, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException) when (DateTime.UtcNow - started < timeout)
            {
                Thread.Sleep(350);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow - started < timeout)
            {
                Thread.Sleep(350);
            }
        }
    }

    private static async Task<bool> TryDeleteDirectoryWithRetriesAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                DeleteDirectoryTree(path);
                return true;
            }
            catch (IOException) when (DateTime.UtcNow - started < timeout)
            {
                await Task.Delay(350, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow - started < timeout)
            {
                await Task.Delay(350, cancellationToken);
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }

            if (DateTime.UtcNow - started >= timeout)
            {
                return false;
            }
        }
    }

    private static async Task<string?> DeleteDirectoryAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        var deleted = await TryDeleteDirectoryWithRetriesAsync(rootPath, TimeSpan.FromSeconds(12), cancellationToken);
        if (deleted)
        {
            return null;
        }

        var pendingDeletePath = $"{rootPath}.delete-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (Directory.Exists(pendingDeletePath))
        {
            pendingDeletePath = $"{pendingDeletePath}-{Guid.NewGuid():N}";
        }

        try
        {
            Directory.Move(rootPath, pendingDeletePath);
        }
        catch
        {
            StartBackgroundDelete(rootPath);
            return rootPath;
        }

        deleted = await TryDeleteDirectoryWithRetriesAsync(pendingDeletePath, TimeSpan.FromSeconds(8), cancellationToken);
        if (deleted)
        {
            return null;
        }

        StartBackgroundDelete(pendingDeletePath);
        return pendingDeletePath;
    }

    private static async Task WaitForInstallationReleaseAsync(ZapretProcessService processService, ZapretInstallation installation, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (processService.GetRunningProcessCount(installation) == 0 &&
                processService.GetProcessCountUsingInstallation(installation) == 0)
            {
                return;
            }

            await Task.Delay(700);
        }
    }

    private static bool IsFolderBusyException(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return true;
        }

        if (exception is not IOException)
        {
            return false;
        }

        return exception.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("используется другим процессом", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("не удается получить доступ к файлу", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("процесс не может получить доступ", StringComparison.OrdinalIgnoreCase);
    }

    private static Exception CreateFriendlyUpdateException(Exception exception)
    {
        return IsFolderBusyException(exception)
            ? new InvalidOperationException(
                "Не удалось обновить сборку zapret, потому что старая папка занята другим процессом. Закройте окна cmd или PowerShell, Проводник и другие программы, открытые из этой папки, затем повторите обновление.",
                exception)
            : exception;
    }

    private static async Task TryReleaseSafeLockingProcessesAsync(FileLockInspectorService fileLockInspector, string rootPath)
    {
        var lockingProcesses = fileLockInspector.FindLockingProcesses(rootPath);
        if (lockingProcesses.Count == 0)
        {
            return;
        }

        await fileLockInspector.CloseSafeProcessesAsync(lockingProcesses, rootPath);
        await Task.Delay(900);
    }

    private static async Task TryReleaseBlockingProcessesAsync(
        FileLockInspectorService fileLockInspector,
        string rootPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryReleaseSafeLockingProcessesAsync(fileLockInspector, rootPath);

            var remainingLocks = fileLockInspector.FindLockingProcesses(rootPath);
            if (remainingLocks.Count == 0)
            {
                return;
            }

            await Task.Delay(700, cancellationToken);
        }
    }

    private static bool TryMoveDirectory(string sourcePath, string destinationPath)
    {
        try
        {
            Directory.Move(sourcePath, destinationPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ArePathsOnSameVolume(string sourcePath, string destinationPath)
    {
        try
        {
            var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
            var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));
            return string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? BuildBusyProcessSummary(FileLockInspectorService fileLockInspector, string rootPath)
    {
        var lockingProcesses = fileLockInspector.FindLockingProcesses(rootPath);
        if (lockingProcesses.Count == 0)
        {
            return null;
        }

        return fileLockInspector.BuildDisplaySummary(lockingProcesses);
    }

    private static Exception CreateBusyPreviousVersionException(string? busyProcessSummary, Exception innerException)
    {
        var message = string.IsNullOrWhiteSpace(busyProcessSummary)
            ? "Не удалось сохранить предыдущую версию zapret для отката, потому что старая папка всё ещё занята. Закройте окна cmd или PowerShell, Проводник и другие программы, открытые из этой папки, затем повторите обновление."
            : $"Не удалось сохранить предыдущую версию zapret для отката. Папку удерживают процессы: {busyProcessSummary}. Закройте их и повторите обновление.";

        return new InvalidOperationException(message, innerException);
    }

    private static async Task WaitForDriverReleaseAsync(string rootPath, TimeSpan timeout)
    {
        var driverPaths = new[]
        {
            Path.Combine(rootPath, "WinDivert64.sys"),
            Path.Combine(rootPath, "WinDivert32.sys"),
            Path.Combine(rootPath, "bin", "WinDivert64.sys"),
            Path.Combine(rootPath, "bin", "WinDivert32.sys")
        }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (driverPaths.Length == 0)
        {
            return;
        }

        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            var allReleased = true;
            foreach (var driverPath in driverPaths)
            {
                try
                {
                    using var stream = new FileStream(driverPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    allReleased = false;
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    allReleased = false;
                    break;
                }
            }

            if (allReleased)
            {
                return;
            }

            await Task.Delay(1000);
        }
    }

    private static void DeleteDirectoryTree(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        Directory.Delete(rootPath, recursive: true);
    }

    private static void StartBackgroundDelete(string path)
    {
        try
        {
            var escapedPath = path.Replace("'", "''");
            var script = $"for ($i=0; $i -lt 40; $i++) {{ try {{ Remove-Item -LiteralPath '{escapedPath}' -Recurse -Force -ErrorAction Stop; break }} catch {{ Start-Sleep -Seconds 3 }} }}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
        }
    }

    private sealed record DownloadedPackage(string TempRoot, string SourceRoot, string Version);
}
