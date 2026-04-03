using System.Diagnostics;
using System.Net.Http;
using System.Text;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class RepositoryMaintenanceService
{
    private const string IpSetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
    private const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
    private const string HostsBlockStart = "# >>> Zapret Manager hosts begin";
    private const string HostsBlockEnd = "# <<< Zapret Manager hosts end";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IpSetService _ipSetService = new();

    public async Task<IpSetUpdateResult> UpdateIpSetListAsync(ZapretInstallation installation, CancellationToken cancellationToken = default)
    {
        var content = NormalizeRemoteText(await DownloadTextAsync(IpSetUrl, "Не удалось обновить список IPSet", cancellationToken));
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Не удалось получить актуальный список IPSet.");
        }

        var currentMode = _ipSetService.GetModeValue(installation);
        var listFile = Path.Combine(installation.ListsPath, "ipset-all.txt");
        var backupFile = listFile + ".backup";
        Directory.CreateDirectory(installation.ListsPath);

        var entryCount = SplitLines(content).Count(line => !string.IsNullOrWhiteSpace(line));
        if (entryCount == 0)
        {
            throw new InvalidOperationException("Скачанный список IPSet пуст.");
        }

        if (string.Equals(currentMode, "loaded", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(listFile, content, Utf8NoBom, cancellationToken);
            return new IpSetUpdateResult(true, entryCount);
        }

        await File.WriteAllTextAsync(backupFile, content, Utf8NoBom, cancellationToken);
        return new IpSetUpdateResult(false, entryCount);
    }

    public async Task<HostsUpdateResult> UpdateHostsFileAsync(CancellationToken cancellationToken = default)
    {
        var downloadedText = NormalizeRemoteText(await DownloadTextAsync(HostsUrl, "Не удалось обновить hosts для zapret", cancellationToken));
        var downloadedLines = SplitLines(downloadedText)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (downloadedLines.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить актуальный шаблон hosts.");
        }

        var hostnames = ExtractHostnames(downloadedLines);
        if (hostnames.Count == 0)
        {
            throw new InvalidOperationException("В скачанном шаблоне hosts не найдено ни одной записи.");
        }

        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");

        Directory.CreateDirectory(Path.GetDirectoryName(hostsPath)!);
        var existingText = File.Exists(hostsPath)
            ? await File.ReadAllTextAsync(hostsPath, cancellationToken)
            : string.Empty;

        var existingLines = SplitLines(NormalizeLineEndings(existingText));
        var cleanedLines = RemoveManagedHostsBlock(existingLines);
        var (filteredLines, removedEntries) = RemoveOldZapretHostEntries(cleanedLines, hostnames);

        while (filteredLines.Count > 0 && string.IsNullOrWhiteSpace(filteredLines[^1]))
        {
            filteredLines.RemoveAt(filteredLines.Count - 1);
        }

        if (filteredLines.Count > 0)
        {
            filteredLines.Add(string.Empty);
        }

        filteredLines.Add(HostsBlockStart);
        filteredLines.AddRange(downloadedLines);
        filteredLines.Add(HostsBlockEnd);

        var finalText = string.Join(Environment.NewLine, filteredLines) + Environment.NewLine;
        await File.WriteAllTextAsync(hostsPath, finalText, Utf8NoBom, cancellationToken);
        await FlushDnsAsync();

        return new HostsUpdateResult(removedEntries, downloadedLines.Count);
    }

    public async Task<int> RemoveManagedHostsBlockAsync(CancellationToken cancellationToken = default)
    {
        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");

        if (!File.Exists(hostsPath))
        {
            return 0;
        }

        var existingText = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        var existingLines = SplitLines(NormalizeLineEndings(existingText));

        var insideManagedBlock = false;
        var removedEntries = 0;
        foreach (var rawLine in existingLines)
        {
            var line = rawLine.Trim();
            if (string.Equals(line, HostsBlockStart, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = true;
                continue;
            }

            if (string.Equals(line, HostsBlockEnd, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = false;
                continue;
            }

            if (!insideManagedBlock)
            {
                continue;
            }

            var sanitized = RemoveComment(line).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            var parts = sanitized
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length >= 2)
            {
                removedEntries++;
            }
        }

        var cleanedLines = RemoveManagedHostsBlock(existingLines);
        while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
        {
            cleanedLines.RemoveAt(cleanedLines.Count - 1);
        }

        var finalText = cleanedLines.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, cleanedLines) + Environment.NewLine;

        await File.WriteAllTextAsync(hostsPath, finalText, Utf8NoBom, cancellationToken);
        await FlushDnsAsync();
        return removedEntries;
    }

    public int GetActiveIpSetEntryCount(ZapretInstallation installation)
    {
        var listFile = Path.Combine(installation.ListsPath, "ipset-all.txt");
        if (!File.Exists(listFile))
        {
            return 0;
        }

        return File.ReadLines(listFile)
            .Select(line => line.Trim())
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    public int GetManagedHostsEntryCount()
    {
        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");

        if (!File.Exists(hostsPath))
        {
            return 0;
        }

        var lines = File.ReadAllLines(hostsPath);
        var insideManagedBlock = false;
        var entryCount = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.Equals(line, HostsBlockStart, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = true;
                continue;
            }

            if (string.Equals(line, HostsBlockEnd, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = false;
                continue;
            }

            if (!insideManagedBlock)
            {
                continue;
            }

            var sanitized = RemoveComment(line).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            var parts = sanitized
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length >= 2)
            {
                entryCount++;
            }
        }

        return entryCount;
    }

    private static async Task<string> DownloadTextAsync(string url, string action, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, action);
        }
    }

    private static string NormalizeRemoteText(string text)
    {
        var normalized = NormalizeLineEndings(text).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized + Environment.NewLine;
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static List<string> SplitLines(string text)
    {
        return NormalizeLineEndings(text)
            .Split('\n')
            .ToList();
    }

    private static HashSet<string> ExtractHostnames(IEnumerable<string> lines)
    {
        var hostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var sanitized = RemoveComment(line).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            var parts = sanitized
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length < 2)
            {
                continue;
            }

            for (var index = 1; index < parts.Length; index++)
            {
                hostnames.Add(parts[index]);
            }
        }

        return hostnames;
    }

    private static List<string> RemoveManagedHostsBlock(List<string> lines)
    {
        var result = new List<string>();
        var insideManagedBlock = false;

        foreach (var line in lines)
        {
            if (string.Equals(line.Trim(), HostsBlockStart, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = true;
                continue;
            }

            if (insideManagedBlock)
            {
                if (string.Equals(line.Trim(), HostsBlockEnd, StringComparison.OrdinalIgnoreCase))
                {
                    insideManagedBlock = false;
                }

                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static (List<string> Lines, int RemovedEntries) RemoveOldZapretHostEntries(
        IEnumerable<string> lines,
        HashSet<string> targetHostnames)
    {
        var result = new List<string>();
        var removedEntries = 0;

        foreach (var line in lines)
        {
            var sanitized = RemoveComment(line).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                result.Add(line);
                continue;
            }

            var parts = sanitized
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length < 2)
            {
                result.Add(line);
                continue;
            }

            var matchesManagedHost = false;
            for (var index = 1; index < parts.Length; index++)
            {
                if (targetHostnames.Contains(parts[index]))
                {
                    matchesManagedHost = true;
                    break;
                }
            }

            if (matchesManagedHost)
            {
                removedEntries++;
                continue;
            }

            result.Add(line);
        }

        return (result, removedEntries);
    }

    private static string RemoveComment(string line)
    {
        var commentIndex = line.IndexOf('#');
        return commentIndex >= 0
            ? line[..commentIndex]
            : line;
    }

    private static async Task FlushDnsAsync()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretManager");
        return client;
    }
}

public readonly record struct IpSetUpdateResult(bool AppliedToActiveList, int EntryCount);
public readonly record struct HostsUpdateResult(int ReplacedEntryCount, int AddedEntryCount);
