namespace ZapretManager.Models;

public sealed record ConfigTableRow
{
    public required string ConfigName { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public ProbeOutcomeKind Outcome { get; init; } = ProbeOutcomeKind.Failure;
    public long? AveragePingMilliseconds { get; init; }
    public int? SuccessCount { get; init; }
    public int? TotalCount { get; init; }
    public int? PartialCount { get; init; }
    public string SummaryBadgeText { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;

    public bool HasProbeResult => SuccessCount.HasValue && TotalCount.HasValue;
    public string ResponseText => AveragePingMilliseconds?.ToString() ?? string.Empty;
    public string SuccessCountText => SuccessCount.HasValue && TotalCount.HasValue
        ? $"{SuccessCount.Value}/{TotalCount.Value}"
        : string.Empty;
    public string TotalCountText => TotalCount?.ToString() ?? string.Empty;
    public string SummaryBodyText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Summary))
            {
                return string.Empty;
            }

            return Summary.Length > 1
                ? Summary[1..].TrimStart()
                : string.Empty;
        }
    }
    public double SortSuccessValue => !SuccessCount.HasValue || !TotalCount.HasValue || TotalCount.Value == 0
        ? -1
        : (SuccessCount.Value + ((PartialCount ?? 0) * 0.5d)) / TotalCount.Value;
}
