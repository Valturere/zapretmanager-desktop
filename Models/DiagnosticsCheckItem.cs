namespace ZapretManager.Models;

public enum DiagnosticsSeverity
{
    Success,
    Warning,
    Error
}

public sealed class DiagnosticsCheckItem
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public DiagnosticsSeverity Severity { get; init; }
}
