namespace ZapretManager.Models;

public sealed class ServiceStatusInfo
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string? ProfileName { get; init; }
    public string? ExecutablePath { get; init; }
    public string? InstallationRootPath { get; init; }
    public string? ProfileToken { get; init; }
}
