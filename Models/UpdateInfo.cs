namespace ZapretManager.Models;

public sealed class UpdateInfo
{
    public string CurrentVersion { get; init; } = "unknown";
    public string LatestVersion { get; init; } = "unknown";
    public string? DownloadUrl { get; init; }
    public string? ReleasePageUrl { get; init; }
    public bool IsUpdateAvailable => !string.IsNullOrWhiteSpace(LatestVersion) &&
                                     !LatestVersion.Equals(CurrentVersion, StringComparison.OrdinalIgnoreCase);
}
