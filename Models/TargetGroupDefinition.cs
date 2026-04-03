namespace ZapretManager.Models;

public sealed class TargetGroupDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public bool IsCustom { get; init; }
}
