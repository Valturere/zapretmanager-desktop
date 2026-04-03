namespace ZapretManager.Models;

public sealed class ProbeBatchResult
{
    public List<ConfigProbeResult> Results { get; init; } = [];
    public string? RecommendedConfigName { get; init; }
}
