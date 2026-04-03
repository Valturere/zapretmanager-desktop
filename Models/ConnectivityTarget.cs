namespace ZapretManager.Models;

public sealed class ConnectivityTarget
{
    public required string Name { get; init; }
    public required string PingHost { get; init; }
    public Uri? Url { get; init; }
    public bool IsDiagnosticOnly { get; init; }
    public bool IsSupplementary { get; init; }
}
