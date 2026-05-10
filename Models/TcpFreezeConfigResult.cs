namespace ZapretManager.Models;

public sealed record TcpFreezeConfigResult
{
    public required string ConfigName { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required int OkCount { get; init; }
    public required int FailCount { get; init; }
    public required int UnsupportedCount { get; init; }
    public required int BlockedCount { get; init; }
    public required IReadOnlyList<string> BlockedTargets { get; init; }
    public required IReadOnlyList<TcpFreezeTargetResult> TargetResults { get; init; }
}
