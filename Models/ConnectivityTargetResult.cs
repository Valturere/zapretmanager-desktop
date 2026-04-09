namespace ZapretManager.Models;

public sealed class ConnectivityTargetResult
{
    public required string TargetName { get; init; }
    public required bool Success { get; init; }
    public ProbeOutcomeKind Outcome { get; init; } = ProbeOutcomeKind.Failure;
    public bool HasDnsFallback { get; init; }
    public string HttpStatus { get; init; } = "n/a";
    public long? PingMilliseconds { get; init; }
    public string Details { get; init; } = string.Empty;
    public bool IsDiagnosticOnly { get; init; }
    public bool IsSupplementary { get; init; }
}
