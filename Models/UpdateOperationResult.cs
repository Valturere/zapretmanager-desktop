namespace ZapretManager.Models;

public sealed class UpdateOperationResult
{
    public required string ActiveRootPath { get; init; }
    public required string InstalledVersion { get; init; }
    public string? BackupRootPath { get; init; }
    public bool PreviousVersionWasBusy { get; init; }
    public string? PreviousVersionBusyProcessSummary { get; init; }
    public bool ServiceWasInstalled { get; init; }
    public bool ServiceWasRunning { get; init; }
}
