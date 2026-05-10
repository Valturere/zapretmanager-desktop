namespace ZapretManager.Models;

public sealed record TcpFreezeWindowContext
{
    public required IReadOnlyList<TcpFreezeConfigDescriptor> Configs { get; init; }
    public string? InitiallySelectedFilePath { get; init; }
}
