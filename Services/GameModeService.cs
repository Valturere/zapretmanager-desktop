using ZapretManager.Models;

namespace ZapretManager.Services;

public sealed class GameModeService
{
    public string GetModeLabel(ZapretInstallation installation)
    {
        return GetModeValue(installation) switch
        {
            "all" => "TCP + UDP (обычно)",
            "tcp" => "Только TCP",
            "udp" => "Только UDP",
            _ => "Выключен"
        };
    }

    public string GetModeValue(ZapretInstallation installation)
    {
        var flagPath = Path.Combine(installation.UtilsPath, "game_filter.enabled");
        if (!File.Exists(flagPath))
        {
            return "disabled";
        }

        var mode = File.ReadLines(flagPath).FirstOrDefault()?.Trim().ToLowerInvariant();
        return mode switch
        {
            "all" => "all",
            "tcp" => "tcp",
            "udp" => "udp",
            _ => "disabled"
        };
    }

    public void SetMode(ZapretInstallation installation, string modeValue)
    {
        var flagPath = Path.Combine(installation.UtilsPath, "game_filter.enabled");

        if (string.Equals(modeValue, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(flagPath))
            {
                File.Delete(flagPath);
            }

            return;
        }

        Directory.CreateDirectory(installation.UtilsPath);
        File.WriteAllText(flagPath, modeValue + Environment.NewLine);
    }
}
