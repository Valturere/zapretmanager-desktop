using Microsoft.Win32;

namespace ZapretManager.Services;

public static class ApplicationThemeService
{
    public const string SystemMode = "system";
    public const string LightMode = "light";
    public const string DarkMode = "dark";

    public static string NormalizeThemeMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            LightMode => LightMode,
            DarkMode => DarkMode,
            _ => SystemMode
        };
    }

    public static bool ResolveUseLightTheme(string? mode, bool fallbackLightTheme = false)
    {
        return NormalizeThemeMode(mode) switch
        {
            LightMode => true,
            DarkMode => false,
            _ => GetSystemUsesLightTheme(fallbackLightTheme)
        };
    }

    public static bool GetSystemUsesLightTheme(bool fallbackLightTheme = false)
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);

            if (personalizeKey?.GetValue("AppsUseLightTheme") is int rawValue)
            {
                return rawValue != 0;
            }
        }
        catch
        {
        }

        return fallbackLightTheme;
    }
}
