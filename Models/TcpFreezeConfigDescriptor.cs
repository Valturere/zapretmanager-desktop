namespace ZapretManager.Models;

public sealed record TcpFreezeConfigDescriptor
{
    public required string ConfigName { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
}
