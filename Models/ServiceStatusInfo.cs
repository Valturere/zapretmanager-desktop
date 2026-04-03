namespace ZapretManager.Models;

public sealed class ServiceStatusInfo
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string? ProfileName { get; init; }
}
