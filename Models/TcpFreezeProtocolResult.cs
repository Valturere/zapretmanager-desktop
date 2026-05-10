namespace ZapretManager.Models;

public sealed record TcpFreezeProtocolResult
{
    public required string Label { get; init; }
    public required TcpFreezeProtocolStatus Status { get; init; }
    public required string Code { get; init; }
    public required long UpBytes { get; init; }
    public required long DownBytes { get; init; }
    public required double TimeSeconds { get; init; }
    public required string Details { get; init; }
}
