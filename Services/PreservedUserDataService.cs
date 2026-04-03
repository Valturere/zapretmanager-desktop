using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class PreservedUserDataService
{
    private static readonly string[] PreservedRelativePaths =
    [
        @"lists\list-general-user.txt",
        @"lists\list-exclude-user.txt",
        @"lists\ipset-exclude-user.txt",
        @"utils\targets.txt",
        @"utils\game_filter.enabled"
    ];

    private readonly string _storageRoot;

    public PreservedUserDataService()
    {
        _storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZapretManager",
            "preserved-user-data");
    }

    public int BackupFromInstallation(ZapretInstallation installation)
    {
        Clear();
        Directory.CreateDirectory(_storageRoot);

        var preservedCount = 0;
        foreach (var relativePath in PreservedRelativePaths)
        {
            var sourcePath = Path.Combine(installation.RootPath, relativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(_storageRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            preservedCount++;
        }

        return preservedCount;
    }

    public int RestoreToInstallation(ZapretInstallation installation)
    {
        if (!Directory.Exists(_storageRoot))
        {
            return 0;
        }

        var restoredCount = 0;
        foreach (var relativePath in PreservedRelativePaths)
        {
            var sourcePath = Path.Combine(_storageRoot, relativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(installation.RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            restoredCount++;
        }

        if (restoredCount > 0)
        {
            Clear();
        }

        return restoredCount;
    }

    public void Clear()
    {
        if (!Directory.Exists(_storageRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
        catch
        {
        }
    }
}
