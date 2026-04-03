namespace ZapretManager.Models;

public sealed class ZapretInstallation
{
    public required string RootPath { get; init; }
    public required string BinPath { get; init; }
    public required string ListsPath { get; init; }
    public required string UtilsPath { get; init; }
    public required string ServiceBatPath { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<ConfigProfile> Profiles { get; init; }
}
