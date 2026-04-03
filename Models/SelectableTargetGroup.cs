using ZapretManager.Infrastructure;

namespace ZapretManager.Models;

public sealed class SelectableTargetGroup : ObservableObject
{
    private bool _isSelected;

    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public bool IsCustom { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
