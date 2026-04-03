namespace ZapretManager.Models;

public sealed class ProbeProgressState
{
    public List<ConfigProbeResult> Results { get; init; } = [];
    public string? CurrentConfigName { get; init; }
    public string? RecommendedConfigName { get; init; }
    public int CompletedConfigs { get; init; }
    public int TotalConfigs { get; init; }
    public bool IsCompleted { get; init; }
}
