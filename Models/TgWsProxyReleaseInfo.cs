namespace ZapretManager.Models;

public sealed class TgWsProxyReleaseInfo
{
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleasePageUrl { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
    public string? AssetFileName { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool IsUpdateAvailable { get; init; }
}
