namespace ZapretManager.Models;

public sealed record TcpFreezeRunReport
{
    public required IReadOnlyList<TcpFreezeConfigResult> ConfigResults { get; init; }
    public string? RecommendedConfigPath { get; init; }
}
