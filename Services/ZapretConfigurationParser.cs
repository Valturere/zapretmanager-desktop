using System.Text;
using System.Text.RegularExpressions;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class ZapretConfigurationParser
{
    private const string DummyUserValue = "domain.example.abc";
    private const string DummyIpValue = "203.0.113.113/32";

    public string BuildArguments(ZapretInstallation installation, ConfigProfile profile)
    {
        EnsureUserFiles(installation);

        var combined = new StringBuilder();
        var capture = false;

        foreach (var rawLine in File.ReadLines(profile.FilePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!capture)
            {
                var match = Regex.Match(line, "winws\\.exe\"\\s*(.*)$", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    match = Regex.Match(line, "winws\\.exe\\s+(.*)$", RegexOptions.IgnoreCase);
                }

                if (!match.Success)
                {
                    continue;
                }

                capture = true;
                AppendNormalized(combined, match.Groups[1].Value);
            }
            else
            {
                AppendNormalized(combined, line);
            }

            if (!line.EndsWith("^", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (combined.Length == 0)
        {
            throw new InvalidOperationException($"Не удалось распарсить конфиг: {profile.FileName}");
        }

        var (gameFilter, gameFilterTcp, gameFilterUdp) = GetGameFilterValues(installation.UtilsPath);
        var normalized = combined.ToString()
            .Replace("^", " ", StringComparison.Ordinal)
            .Replace("%BIN%", installation.BinPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Replace("%LISTS%", installation.ListsPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterTCP%", gameFilterTcp, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterUDP%", gameFilterUdp, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilter%", gameFilter, StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    public void EnsureUserFiles(ZapretInstallation installation)
    {
        Directory.CreateDirectory(installation.ListsPath);
        Directory.CreateDirectory(installation.UtilsPath);

        EnsureFile(Path.Combine(installation.ListsPath, "ipset-exclude-user.txt"), DummyIpValue);
        EnsureFile(Path.Combine(installation.ListsPath, "list-general-user.txt"), DummyUserValue);
        EnsureFile(Path.Combine(installation.ListsPath, "list-exclude-user.txt"), DummyUserValue);
    }

    private static void AppendNormalized(StringBuilder builder, string value)
    {
        var chunk = value.TrimEnd('^').Trim();
        if (chunk.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(chunk);
    }

    private static void EnsureFile(string path, string content)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static (string GameFilter, string GameFilterTcp, string GameFilterUdp) GetGameFilterValues(string utilsPath)
    {
        var flagPath = Path.Combine(utilsPath, "game_filter.enabled");
        if (!File.Exists(flagPath))
        {
            return ("12", "12", "12");
        }

        var mode = File.ReadLines(flagPath).FirstOrDefault()?.Trim().ToLowerInvariant();
        return mode switch
        {
            "all" => ("1024-65535", "1024-65535", "1024-65535"),
            "tcp" => ("1024-65535", "1024-65535", "12"),
            "udp" => ("1024-65535", "12", "1024-65535"),
            _ => ("12", "12", "12")
        };
    }
}
