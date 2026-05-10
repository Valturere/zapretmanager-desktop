namespace ZapretManager.Models;

public sealed record TcpFreezeTargetResult
{
    public required string TargetId { get; init; }
    public required string Provider { get; init; }
    public required string Country { get; init; }
    public required string Host { get; init; }
    public required IReadOnlyList<TcpFreezeProtocolResult> Checks { get; init; }
}
