using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class IpSetService
{
    private const string DisabledSentinel = "203.0.113.113/32";

    public string GetModeValue(ZapretInstallation installation)
    {
        var listFile = GetListFilePath(installation);
        if (!File.Exists(listFile))
        {
            return "any";
        }

        var lines = File.ReadAllLines(listFile)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return "any";
        }

        return lines.Count == 1 && string.Equals(lines[0], DisabledSentinel, StringComparison.OrdinalIgnoreCase)
            ? "none"
            : "loaded";
    }

    public string GetModeLabel(ZapretInstallation installation)
    {
        return GetModeValue(installation) switch
        {
            "loaded" => "IPSet: по списку",
            "none" => "IPSet: выключен",
            _ => "IPSet: все IP"
        };
    }

    public void SetMode(ZapretInstallation installation, string targetMode)
    {
        var listFile = GetListFilePath(installation);
        var backupFile = listFile + ".backup";
        Directory.CreateDirectory(Path.GetDirectoryName(listFile)!);

        var currentMode = GetModeValue(installation);
        if (string.Equals(currentMode, targetMode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (targetMode)
        {
            case "loaded":
                SwitchToLoadedMode(listFile, backupFile);
                break;

            case "none":
                SwitchToDisabledMode(listFile, backupFile, currentMode);
                break;

            default:
                SwitchToAnyMode(listFile, backupFile, currentMode);
                break;
        }
    }

    private static string GetListFilePath(ZapretInstallation installation)
    {
        return Path.Combine(installation.ListsPath, "ipset-all.txt");
    }

    private static void SwitchToLoadedMode(string listFile, string backupFile)
    {
        if (!File.Exists(backupFile))
        {
            throw new InvalidOperationException("Нет сохранённого списка IPSet. Сначала обновите список или переключите режим после загрузки списка.");
        }

        if (File.Exists(listFile))
        {
            File.Delete(listFile);
        }

        File.Move(backupFile, listFile);
    }

    private static void SwitchToDisabledMode(string listFile, string backupFile, string currentMode)
    {
        if (string.Equals(currentMode, "loaded", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }

            if (File.Exists(listFile))
            {
                File.Move(listFile, backupFile);
            }
        }

        File.WriteAllText(listFile, DisabledSentinel + Environment.NewLine);
    }

    private static void SwitchToAnyMode(string listFile, string backupFile, string currentMode)
    {
        if (string.Equals(currentMode, "loaded", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }

            if (File.Exists(listFile))
            {
                File.Move(listFile, backupFile);
            }
        }

        File.WriteAllText(listFile, string.Empty);
    }
}
