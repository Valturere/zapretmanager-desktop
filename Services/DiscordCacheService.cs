using System.IO;

namespace ZapretManager.Services;

public sealed class DiscordCacheService
{
    private static readonly string[] DiscordFolders =
    [
        "discord",
        "discordptb",
        "discordcanary"
    ];

    private static readonly string[] RelativeCachePaths =
    [
        "Cache",
        "Code Cache",
        "GPUCache",
        Path.Combine("Service Worker", "CacheStorage")
    ];

    public int Clear()
    {
        var clearedCount = 0;
        foreach (var root in GetCandidateRoots())
        {
            foreach (var discordFolder in DiscordFolders)
            {
                foreach (var relativePath in RelativeCachePaths)
                {
                    var fullPath = Path.Combine(root, discordFolder, relativePath);
                    if (!Directory.Exists(fullPath))
                    {
                        continue;
                    }

                    ClearDirectoryContents(fullPath);
                    clearedCount++;
                }
            }
        }

        return clearedCount;
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(roaming))
        {
            yield return roaming;
        }

        if (!string.IsNullOrWhiteSpace(local))
        {
            yield return local;
        }
    }

    private static void ClearDirectoryContents(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(item => item.Length))
        {
            try
            {
                Directory.Delete(directory, recursive: false);
            }
            catch
            {
            }
        }
    }
}
