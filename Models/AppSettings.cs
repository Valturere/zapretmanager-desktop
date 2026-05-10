using System.Collections.Generic;
using ZapretManager.Services;

namespace ZapretManager.Models;

public sealed class AppSettings
{
    public string? LastInstallationPath { get; set; }
    public string? LastSelectedConfigPath { get; set; }
    public string? LastStartedConfigPath { get; set; }
    public string? LastInstalledServiceConfigPath { get; set; }
    public string? LastInstalledServiceProfileName { get; set; }
    public string? LastInstalledServiceProfileFileName { get; set; }
    public bool AutoCheckUpdatesEnabled { get; set; } = true;
    public bool StartWithWindowsEnabled { get; set; } = false;
    public bool StartWithWindowsPreferenceInitialized { get; set; }
    public bool CloseToTrayEnabled { get; set; } = true;
    public bool MinimizeToTrayEnabled { get; set; } = false;
    public bool UseLightTheme { get; set; }
    public string ApplicationThemeMode { get; set; } = ApplicationThemeService.SystemMode;
    public string UpdateCheckIntervalValue { get; set; } = "2h";
    public DateTime? LastAutomaticUpdateCheckUtc { get; set; }
    public bool AutoUpdateIpSetOnStartupEnabled { get; set; } = true;
    public string PreferredGameModeValue { get; set; } = "all";
    public string PreferredDnsProfileKey { get; set; } = "system";
    public bool DnsOverHttpsEnabled { get; set; }
    public string? CustomDnsPrimary { get; set; }
    public string? CustomDnsSecondary { get; set; }
    public string? CustomDnsDohTemplate { get; set; }
    public List<CustomTargetGroup> CustomTargetGroups { get; set; } = [];
    public List<string> SelectedTargetGroupKeys { get; set; } = [];
    public List<string> HiddenConfigPaths { get; set; } = [];
    public bool SkipHideConfigConfirmation { get; set; }
    public double? ProbeAverageProfileSeconds { get; set; }
    public bool TgWsProxyAutoCheckUpdatesEnabled { get; set; } = true;
    public string? LastTgWsProxyExecutablePath { get; set; }
}
