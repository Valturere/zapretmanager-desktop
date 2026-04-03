using System.Text.RegularExpressions;
using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class ZapretDiscoveryService
{
    public ZapretInstallation? Discover(string startDirectory)
    {
        foreach (var candidate in EnumerateSearchRoots(startDirectory))
        {
            var installation = TryLoad(candidate);
            if (installation is not null)
            {
                return installation;
            }
        }

        return null;
    }

    public ZapretInstallation? DiscoverQuick(string startDirectory)
    {
        foreach (var candidate in EnumerateQuickSearchRoots(startDirectory))
        {
            var installation = TryLoad(candidate);
            if (installation is not null)
            {
                return installation;
            }
        }

        return null;
    }

    public ZapretInstallation? TryLoad(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return null;
        }

        var binPath = Path.Combine(rootPath, "bin");
        var listsPath = Path.Combine(rootPath, "lists");
        var utilsPath = Path.Combine(rootPath, "utils");
        var serviceBatPath = Path.Combine(rootPath, "service.bat");
        var winwsPath = Path.Combine(binPath, "winws.exe");

        if (!File.Exists(serviceBatPath) || !File.Exists(winwsPath))
        {
            return null;
        }

        var profiles = Directory
            .GetFiles(rootPath, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("service", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => BuildNaturalSortKey(Path.GetFileName(path)))
            .Select(path => new ConfigProfile(Path.GetFileNameWithoutExtension(path), path))
            .ToArray();

        return new ZapretInstallation
        {
            RootPath = rootPath,
            BinPath = binPath,
            ListsPath = listsPath,
            UtilsPath = utilsPath,
            ServiceBatPath = serviceBatPath,
            Version = ReadLocalVersion(serviceBatPath),
            Profiles = profiles
        };
    }

    private static IEnumerable<string> EnumerateSearchRoots(string startDirectory)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }

        AddPath(startDirectory);
        AddPath(AppContext.BaseDirectory);
        AddPath(Path.GetDirectoryName(AppContext.BaseDirectory));
        AddPath(Directory.GetCurrentDirectory());
        AddPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddPath(userProfile);
        AddPath(Path.Combine(userProfile, "Downloads"));

        foreach (var root in result.ToArray())
        {
            foreach (var child in SafeEnumerateDirectories(root))
            {
                AddPath(child);

                if (LooksPromising(child))
                {
                    foreach (var grandChild in SafeEnumerateDirectories(child))
                    {
                        AddPath(grandChild);
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateQuickSearchRoots(string startDirectory)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }

        foreach (var path in EnumerateSearchRoots(startDirectory))
        {
            AddPath(path);
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            AddPath(drive.RootDirectory.FullName);
        }

        foreach (var root in result.ToArray())
        {
            foreach (var child in SafeEnumerateDirectories(root))
            {
                AddPath(child);

                var deepScan = LooksPromising(child)
                               || string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
                               || root.Contains("Desktop", StringComparison.OrdinalIgnoreCase)
                               || root.Contains("Downloads", StringComparison.OrdinalIgnoreCase)
                               || root.Contains("Documents", StringComparison.OrdinalIgnoreCase);

                if (!deepScan)
                {
                    continue;
                }

                foreach (var grandChild in SafeEnumerateDirectories(child))
                {
                    AddPath(grandChild);

                    if (!LooksPromising(grandChild))
                    {
                        continue;
                    }

                    foreach (var greatGrandChild in SafeEnumerateDirectories(grandChild))
                    {
                        AddPath(greatGrandChild);
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    private static bool LooksPromising(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("zapret", StringComparison.OrdinalIgnoreCase)
               || name.Contains("discord", StringComparison.OrdinalIgnoreCase)
               || name.Contains("youtube", StringComparison.OrdinalIgnoreCase)
               || name.Contains("dpi", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadLocalVersion(string serviceBatPath)
    {
        foreach (var line in File.ReadLines(serviceBatPath))
        {
            if (!line.Contains("LOCAL_VERSION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return parts[1].Trim('"');
            }
        }

        return "unknown";
    }

    private static string BuildNaturalSortKey(string fileName)
    {
        return Regex.Replace(fileName, "(\\d+)", match => match.Value.PadLeft(8, '0'));
    }
}
