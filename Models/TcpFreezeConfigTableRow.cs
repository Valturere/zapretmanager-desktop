namespace ZapretManager.Models;

public sealed record TcpFreezeConfigTableRow
{
    public required string ConfigName { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public int? OkCount { get; init; }
    public int? BlockedCount { get; init; }
    public int? FailCount { get; init; }
    public int? UnsupportedCount { get; init; }
    public string SummaryText { get; init; } = string.Empty;
    public bool HasResult { get; init; }

    public string OkText => OkCount?.ToString() ?? string.Empty;
    public string BlockedText => BlockedCount?.ToString() ?? string.Empty;
    public string FailText => FailCount?.ToString() ?? string.Empty;
    public string UnsupportedText => UnsupportedCount?.ToString() ?? string.Empty;
    public string SummaryBadgeText
    {
        get
        {
            if (!HasResult)
            {
                return "—";
            }

            if ((FailCount ?? 0) == 0 && (BlockedCount ?? 0) == 0)
            {
                return "✓";
            }

            if ((OkCount ?? 0) == 0)
            {
                return "✕";
            }

            return "!";
        }
    }

    public double SortSuccessValue
    {
        get
        {
            if (!OkCount.HasValue || !BlockedCount.HasValue || !FailCount.HasValue || !UnsupportedCount.HasValue)
            {
                return -1;
            }

            var total = OkCount.Value + BlockedCount.Value + FailCount.Value + UnsupportedCount.Value;
            if (total == 0)
            {
                return -1;
            }

            return (OkCount.Value + (BlockedCount.Value * 0.35d)) / total;
        }
    }
}
