namespace ZapretManager.Models;

public sealed class ConfigProbeResult
{
    public required string ConfigName { get; init; }
    public ProbeOutcomeKind Outcome { get; init; } = ProbeOutcomeKind.Failure;
    public required int SuccessCount { get; init; }
    public required int TotalCount { get; init; }
    public int PartialCount { get; init; }
    public int PrimarySuccessCount { get; init; }
    public int PrimaryTotalCount { get; init; }
    public int PrimaryPartialCount { get; init; }
    public int SupplementarySuccessCount { get; init; }
    public int SupplementaryTotalCount { get; init; }
    public required string Summary { get; init; }
    public string Details { get; init; } = string.Empty;
    public List<string> FailedTargetNames { get; init; } = [];
    public List<string> PrimaryFailedTargetNames { get; init; } = [];
    public List<string> PartialTargetNames { get; init; } = [];
    public List<string> PrimaryPartialTargetNames { get; init; } = [];
    public List<string> SupplementaryFailedTargetNames { get; init; } = [];
    public IReadOnlyList<ConnectivityTargetResult> TargetResults { get; init; } = [];
    public long? AveragePingMilliseconds { get; init; }
    public double SuccessRate => TotalCount == 0 ? 0 : Math.Round((double)SuccessCount / TotalCount * 100, 1);
    public double PrimarySuccessRate => PrimaryTotalCount == 0 ? 0 : Math.Round((double)PrimarySuccessCount / PrimaryTotalCount * 100, 1);
    public double SupplementarySuccessRate => SupplementaryTotalCount == 0
        ? 100
        : Math.Round((double)SupplementarySuccessCount / SupplementaryTotalCount * 100, 1);
}
